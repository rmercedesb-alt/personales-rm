using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DashVisitas.Services;

// ── Núcleo compartido del módulo Presupuestos ───────────────────────────────
// Mapa estático familia×KPI -> PreTipo(s) de PresupuestosHistoricos, más las
// fórmulas de Alcance/Proyectado/Estimado. Sin estado, sin conexión propia
// (los métodos que consultan BD reciben la conexión ya abierta, igual que
// Services/TorreCore.cs).
public static class PresupuestosCore
{
    // Ref = cómo se interpreta/filtra PreReferencia para esta combinación:
    //   "none" -> siempre 'GENERAL' (no hay dimensión, es el total del vendedor)
    //   "cat1" -> Categoria1.Cat1ID (Categoría/Línea)
    //   "cat3" -> Categoria3.Cat3ID (Suplidor)
    //   "prod" -> Productos.ProID (quitar prefijo 'V' si viene, ~99.5% de UNDDACTPROD lo trae)
    //   "grupo"-> GrupoProductos (vía GrupoProductosDetalle)
    public record FamCfg(string TipoMensual, string? TipoDiarioA = null, string? TipoDiarioB = null, string Ref = "none");

    // TipoDiarioA/B solo están poblados donde existe historial diario real en
    // PresupuestosHistoricos (Categoría y Suplidor) — el resto se deja en null
    // a propósito: no hay que fingir una serie diaria que la data no tiene.
    public static readonly Dictionary<(string fam, string kpi), FamCfg> Mapa = new()
    {
        [("general", "dinero")] = new("MNTGVEN"),
        [("general", "unidades")] = new("UNDGVENCJ"),

        [("categoria", "dinero")] = new("MNTDCAT", "MNTDCAT1DIA", "MNTDCAT2DIA", "cat1"),
        [("categoria", "unidades")] = new("UNDDLIN", Ref: "cat1"),
        [("categoria", "facturas")] = new("UNDDLINFACT", "UNDDLINFACT1", Ref: "cat1"),
        // TipoDiarioA era "UNDDLINACT1" — PreTipo que NO EXISTE en PresupuestosHistoricos
        // (verificado en vivo: 0 filas en TODO el histórico) — corregido a "UNDDLINACTDIA1"
        // ("Act. Nuevas de Línea del día" en UsosMultiples/CodigoGrupo='Pretipo'), pedido
        // explícito del usuario 2026-08-29. "UNDDLINACTDIA" (sin el "1") es un PreTipo
        // DISTINTO — activaciones TOTALES del día (no solo las nuevas) — no se usa aquí.
        [("categoria", "activaciones")] = new("UNDDLINACT", "UNDDLINACTDIA1", Ref: "cat1"),

        [("suplidor", "dinero")] = new("MNTDCAT3VEN", Ref: "cat3"),
        [("suplidor", "unidades")] = new("UNDDCAT3VEN", Ref: "cat3"),
        [("suplidor", "activaciones")] = new("UNDDACTCAT3", "UNDDACTCAT3DIA", Ref: "cat3"),

        [("producto", "dinero")] = new("MNTDPROD", Ref: "prod"),
        [("producto", "unidades")] = new("Unddprod", Ref: "prod"),
        [("producto", "activaciones")] = new("UNDDACTPROD", Ref: "prod"),

        [("grupoproducto", "dinero")] = new("MNTDGRPP", Ref: "grupo"),
        [("grupoproducto", "unidades")] = new("UNDGRPP", Ref: "grupo"),
        [("grupoproducto", "facturas")] = new("UNDDGRPPFAC", Ref: "grupo"),
    };

    // PreTipo de las 4 métricas "Extras" (no encajan en el modelo Alcance/Proyectado/Estimado).
    public const string TipoDevoluciones = "MNTGDEV";       // siempre PreReferencia='GENERAL'
    public const string TipoMetaGerencial = "MNTCATGER";    // PreEjecutado siempre 0, solo objetivo
    public const string TipoEfectividadVisitas = "UNDGEFTVIS";
    public const string TipoRetencion2Meses = "UNDDLINACT2Mes"; // Cat1ID, sin meta real

    // Días hábiles (lun-sáb, excluye domingo) desde el 1 hasta 'hastaDia' del mes.
    // Copiado literal del patrón ya probado en ApiHistoricosController.DiasHabiles.
    public static int DiasHabiles(int anio, int mes, int hastaDia)
    {
        int n = 0;
        for (int d = 1; d <= hastaDia; d++)
            if (new DateTime(anio, mes, d).DayOfWeek != DayOfWeek.Sunday) n++;
        return n;
    }

    // Días hábiles transcurridos para un RepTipo+mes dado, a partir de la fecha real
    // del último dato procesado (MAX(PreFechaActualizacion)) — NO una fórmula fija
    // "hoy menos 1". Si ultima cae en el mes/año pedido, cuenta hasta ese día; si el
    // mes ya cerró (ultima es de un mes posterior), cuenta el mes completo.
    //
    // Un mes que ya pasó en el CALENDARIO REAL (antes del mes/año de hoy) se considera
    // SIEMPRE cerrado, sin importar qué diga 'ultima' — bug reportado por el usuario
    // 2026-08-29 ("el Ejecutado y el Proyectado de los meses anteriores difiere, y esto
    // no puede ser posible"), verificado en vivo contra la BD: para UNDDLINACT
    // (Categoría/Activaciones) y UNDDLINFACT (Categoría/Facturas), TI dejó de actualizar
    // PreFechaActualizacion de mayo-2026 el 30 de mayo (DENTRO del mismo mes, no en
    // junio como sí pasa con Dinero/MNTDCAT) — con la regla vieja (basada solo en
    // 'ultima'), mayo se seguía tratando como "mes en curso" 3 meses después de haber
    // cerrado, inflando Proyectado por encima de Ejecutado. Solo el mes/año ACTUAL (el
    // de hoy) sigue calculando días transcurridos a partir de 'ultima' — ahí sí tiene
    // sentido que el avance real de los datos determine cuánto ha corrido el mes.
    public static int DiasTranscurridos(DateTime? ultima, int anio, int mes)
    {
        var hoy = DateTime.Today;
        if (anio < hoy.Year || (anio == hoy.Year && mes < hoy.Month))
            return DiasHabiles(anio, mes, DateTime.DaysInMonth(anio, mes));
        if (ultima is null) return 0;
        var u = ultima.Value;
        if (u.Year == anio && u.Month == mes) return DiasHabiles(anio, mes, u.Day);
        if (u.Year > anio || (u.Year == anio && u.Month > mes)) return DiasHabiles(anio, mes, DateTime.DaysInMonth(anio, mes));
        return 0; // ultima es anterior al mes pedido -> aun no hay nada procesado
    }

    // Total de días hábiles (L-S) en el año completo — denominador de "Del Año".
    public static int DiasHabilesAnioCompleto(int anio)
    {
        int total = 0;
        for (int m = 1; m <= 12; m++) total += DiasHabiles(anio, m, DateTime.DaysInMonth(anio, m));
        return total;
    }

    // Días hábiles transcurridos DESDE EL 1 DE ENERO hasta 'ultima' (acumulado de varios
    // meses, no solo el mes de 'ultima') — usado como numerador de "Del Año". Mismo
    // ajuste que DiasTranscurridos: un año ya cerrado en el calendario real (anterior
    // al año de hoy) cuenta SIEMPRE el año completo, sin depender de 'ultima'.
    public static int DiasTranscurridosAnio(DateTime? ultima, int anio)
    {
        if (anio < DateTime.Today.Year) return DiasHabilesAnioCompleto(anio);
        if (ultima is null) return 0;
        var u = ultima.Value;
        if (u.Year < anio) return 0;
        int hastaMes = u.Year > anio ? 12 : u.Month;
        int hastaDia = u.Year > anio ? DateTime.DaysInMonth(anio, 12) : u.Day;
        int total = 0;
        for (int m = 1; m < hastaMes; m++) total += DiasHabiles(anio, m, DateTime.DaysInMonth(anio, m));
        total += DiasHabiles(anio, hastaMes, hastaDia);
        return total;
    }

    // OJO: deben ser PROPIEDADES (get/set), no campos públicos — Program.cs configura
    // System.Text.Json sin IncludeFields, así que campos públicos se serializan como
    // "{}" vacío (verificado empíricamente: causó que /resumen y /drill devolvieran
    // objetos KPI vacíos al front hasta que se detectó y corrigió aquí).
    public class KpiResultado
    {
        public double Presupuesto { get; set; }
        public double PresupuestoAlDia { get; set; }
        public double Ejecutado { get; set; }
        public double? AlcancePct { get; set; }
        public double? VentasProyectadas { get; set; }
        public double? ProyectadoPct { get; set; }
        public double? VentasEstimadas { get; set; }
        public double? EstimadoPct { get; set; }
        public int DiasTranscurridos { get; set; }
        public int DiasDelPeriodo { get; set; }
        public DateTime? Ultima { get; set; }
        // Agregado 2026-09-06, pedido explícito del usuario (tarjetas "días del mes/
        // transcurridos/restantes" + columna "Necesidad Diaria" en todos los entornos de
        // Presupuestos). DiasTranscurridosAjustado reusa DiasTranscurridosAjustado(...) —
        // el mismo rezago de 1 día hábil (preventa de HOY se factura mañana, salvo el
        // último día del mes que factura 2 días de golpe) que ya se aplicaba solo dentro
        // de AjustarProyectadoPorCanal — aquí queda expuesto para CUALQUIER pantalla, sin
        // tocar el DiasTranscurridos "crudo" de arriba (que sigue igual, lo sigue usando
        // FactorAvance/Estimado tal cual estaba).
        public int DiasTranscurridosAjustado { get; set; }
        public int DiasRestantes { get; set; }
        // (Presupuesto − Ejecutado) / DiasRestantes — cuánto hace falta vender POR DÍA
        // HÁBIL restante para llegar exacto al presupuesto del período. Null cuando ya no
        // quedan días restantes (mes cerrado) — nada que repartir.
        public double? NecesidadDiaria { get; set; }
    }

    // Fracción esperada del período lograda al día hábil 'wd' según la curva histórica
    // (promedio de los últimos N meses completos), con respaldo lineal si no hay dato
    // para ese índice de día. Copiado literal del patrón de ApiHistoricosController.FactorAvance.
    public static double FactorAvance(double[]? curva, int wd, int diasPeriodo)
    {
        if (wd < 1) return 0;
        if (curva != null && wd < curva.Length && curva[wd] > 0) return Math.Min(1.0, curva[wd]);
        return diasPeriodo > 0 ? (double)wd / diasPeriodo : 1.0;
    }

    // Monto neto sin ITBIS, mismo patrón que ApiHistoricosController.MontoNeto.
    private const string MontoNeto = "d.VenCantidad*d.VenPrecio - ISNULL(d.VenTotalDescuento,0)";

    // Convierte una serie diaria de UN mes (índice = día del mes, valor = monto de ese día) en
    // la curva acumulada por día hábil que consume FactorAvance: curva[wd] = fracción acumulada
    // del total del mes alcanzada tras 'wd' días hábiles (un domingo no avanza el índice, pero
    // su venta SÍ se suma al acumulado del día hábil anterior). Extraído de lo que antes era
    // ReducirACurva para reusarlo con la serie sintética de CurvaPorFranjas.
    private static double[] CurvaDesdeSerieDiaria(double[] dias, int anio, int mes)
    {
        int dim = DateTime.DaysInMonth(anio, mes);
        double total = 0;
        for (int d = 1; d <= dim; d++) total += dias[d];
        var curva = new double[32];
        if (total <= 0) return curva;
        // FIX 2026-09-07 (bug real encontrado por el usuario, "grave"): el domingo sumaba a
        // 'cum' DESPUÉS del 'continue' — es decir, nunca sumaba, se perdía por completo (el
        // comentario de arriba ya describía el comportamiento CORRECTO, el código no lo hacía).
        // Efecto medido con pesos reales: la curva nunca llegaba a 1.0 ni en el ÚLTIMO día hábil
        // del mes (0.87 en vez de 1.0 con 4 domingos/mes), inflando "Estimado" ~15% de más de
        // forma sistemática. Ahora 'cum' se actualiza SIEMPRE, antes de decidir si el día avanza
        // el índice — un domingo aporta su parte al acumulado que se lee en el próximo día hábil.
        double cum = 0; int wd = 0;
        for (int d = 1; d <= dim; d++)
        {
            cum += dias[d];
            if (new DateTime(anio, mes, d).DayOfWeek == DayOfWeek.Sunday) continue;
            wd++;
            if (wd <= 31) curva[wd] = cum / total;
        }
        return curva;
    }

    // Franjas del ciclo de venta dentro de un mes (pedido explícito del usuario 2026-09-05):
    // 1-7, 8-16, 17-22, 23-cierre (el corte final varía con los días reales del mes: 28/29/30/31).
    private static readonly int[] FranjaDesde = { 1, 8, 17, 23 };
    private static int FranjaDe(int dia) => dia <= 7 ? 0 : dia <= 16 ? 1 : dia <= 22 ? 2 : 3;
    private static int FranjaHasta(int franja, int dim) => franja == 3 ? dim : franja == 0 ? 7 : franja == 1 ? 16 : 22;

    // Curva de estacionalidad para "Estimado" — 100% desde VWVentas/VWVentasDetalle (reemplaza
    // CurvaCategoria/CurvaSuplidor/CurvaGeneral: unifica el origen de datos y agrega Canal como
    // eje filtrable, pedido explícito del usuario 2026-09-05: "las estimaciones tienen que venir
    // de un patrón de ventas que se realce en el canal... con el cual observe... el comportamiento
    // en cada ciclo del mes... dividido en 4 partes: 1 al 7, 8 al 16, 17 al 22, 23 al cierre").
    // Ventana: los 6 meses calendario completos anteriores a (anioRef,mesRef) — igual que antes.
    // Por cada uno de esos meses (con datos), se calcula qué % del total del mes cayó en cada una
    // de las 4 franjas; se promedian esos 4 pesos entre los meses disponibles (más estable que
    // promediar día a día cuando el historial ya viene acotado por Canal/Suplidor/Categoría
    // específicos, con menos transacciones por día) y se materializan sobre el calendario del mes
    // que se está estimando (reparto uniforme dentro de cada franja) para producir una curva
    // compatible con FactorAvance/Calcular sin tocar ninguna de las dos.
    // Peso relativo del día 'd' DENTRO de su franja al materializar sobre un mes concreto
    // (pedido explícito del usuario 2026-09-05, confirmado en vivo contra VWVentas: día 1
    // del mes casi no factura nada —la preventa de HOY se factura al día siguiente, salvo
    // el ÚLTIMO día del mes, que factura 2 días de venta de golpe (la propia + la
    // pendiente)—. Reparto uniforme (1 peso por día) distorsionaría exactamente los 2 días
    // donde `FactorAvance` más se consulta: el arranque del mes y su cierre. Día 1 -> no
    // aporta nada de la franja 1 (ese "hueco" se reparte entre los demás días de la
    // franja); último día del mes -> aporta el doble de un día normal.
    // FIX 2026-09-07 (pedido explícito del usuario): los domingos NO se calculan, NUNCA
    // tienen peso — el negocio no opera los domingos, los pesos son de lunes a sábado. Antes
    // un domingo SÍ recibía un peso "normal" (1) que luego se replegaba sobre el lunes
    // siguiente (ver CurvaDesdeSerieDiaria) — ahora directamente no participa del reparto: el
    // peso de esa franja se concentra 100% entre sus días L-S (uno de ellos ya no compite con
    // un domingo por una porción del pastel).
    private static double PesoDiaEnFranja(DateTime fecha, int dim) =>
        fecha.DayOfWeek == DayOfWeek.Sunday ? 0 : fecha.Day == 1 ? 0 : fecha.Day == dim ? 2 : 1;

    public static double[] CurvaPorFranjas(SqlConnection cn, int anioRef, int mesRef, string? cat1Id, string? cat3Id, int canId)
    {
        var (pesos, mesesConDatos) = PesosFranjas(cn, anioRef, mesRef, cat1Id, cat3Id, canId);
        if (mesesConDatos == 0) return new double[32];

        // Materializar sobre el mes objetivo: dentro de cada franja, el peso de la franja se
        // reparte según PesoDiaEnFranja (no uniforme) — ver comentario arriba.
        int dimRef = DateTime.DaysInMonth(anioRef, mesRef);
        var unidadesPorDia = new double[32];
        var unidadesPorFranja = new double[4];
        for (int d = 1; d <= dimRef; d++)
        {
            int f = FranjaDe(d);
            unidadesPorDia[d] = PesoDiaEnFranja(new DateTime(anioRef, mesRef, d), dimRef);
            unidadesPorFranja[f] += unidadesPorDia[d];
        }
        var diasRef = new double[32];
        for (int d = 1; d <= dimRef; d++)
        {
            int f = FranjaDe(d);
            diasRef[d] = unidadesPorFranja[f] > 0 ? pesos[f] * unidadesPorDia[d] / unidadesPorFranja[f] : 0;
        }
        return CurvaDesdeSerieDiaria(diasRef, anioRef, mesRef);
    }

    // Pesos promedio de las 4 franjas (sin materializar a un mes específico) — motor real de
    // CurvaPorFranjas y también lo que se MUESTRA en pantalla como "patrón descubierto" (pedido
    // explícito del usuario 2026-09-05). Ventana: 6 meses calendario completos anteriores a
    // (anioRef,mesRef), 100% desde VWVentas/VWVentasDetalle.
    public static (double[] pesos, int mesesConDatos) PesosFranjas(SqlConnection cn, int anioRef, int mesRef, string? cat1Id, string? cat3Id, int canId)
    {
        var fin = new DateTime(anioRef, mesRef, 1);
        var inicio = fin.AddMonths(-6);
        var filtros = new List<string>(); var parms = new List<(string nombre, object valor)>();
        if (!string.IsNullOrEmpty(cat1Id) && int.TryParse(cat1Id, out var c1)) { filtros.Add("pr.Cat1ID=@cat1"); parms.Add(("@cat1", c1)); }
        if (!string.IsNullOrEmpty(cat3Id) && int.TryParse(cat3Id, out var c3)) { filtros.Add("pr.Cat3ID=@cat3"); parms.Add(("@cat3", c3)); }
        if (canId > 0) { filtros.Add("ISNULL(ra.CanID,0)=@can"); parms.Add(("@can", canId)); }
        var where = filtros.Count > 0 ? " AND " + string.Join(" AND ", filtros) : "";

        var porMesFranja = new Dictionary<(int anio, int mes), double[]>();
        // OJO — v.RepCodigo en VWVentas es el REPARTIDOR, no el vendedor (verificado en vivo
        // 2026-09-05: los 7 vendedores marcados Canal=Mayorista tenían 0 filas por v.RepCodigo,
        // pero cientos por v.RepVendedor — bug real encontrado por el usuario). El Canal se
        // resuelve SIEMPRE por el vendedor real (v.RepVendedor), nunca por el repartidor.
        using (var cmd = new SqlCommand($@"
SELECT YEAR(v.VenFecha) A, MONTH(v.VenFecha) M, DAY(v.VenFecha) D, SUM({MontoNeto}) Monto
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=v.RepVendedor
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE v.VenEstatus<>0 AND v.VenFecha>=@desde AND v.VenFecha<@hasta{where}
GROUP BY YEAR(v.VenFecha), MONTH(v.VenFecha), DAY(v.VenFecha)", cn) { CommandTimeout = 90 })
        {
            cmd.Parameters.AddWithValue("@desde", inicio);
            cmd.Parameters.AddWithValue("@hasta", fin);
            foreach (var (nombre, valor) in parms) cmd.Parameters.AddWithValue(nombre, valor);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                int y = rd.GetInt32(0), m = rd.GetInt32(1), d = rd.GetInt32(2);
                double monto = rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3));
                var key = (y, m);
                if (!porMesFranja.TryGetValue(key, out var arr)) { arr = new double[4]; porMesFranja[key] = arr; }
                if (d >= 1 && d <= 31) arr[FranjaDe(d)] += monto;
            }
        }
        var sumPesos = new double[4]; int mesesConDatos = 0;
        foreach (var (_, franjas) in porMesFranja)
        {
            double total = franjas.Sum();
            if (total <= 0) continue;
            for (int i = 0; i < 4; i++) sumPesos[i] += franjas[i] / total;
            mesesConDatos++;
        }
        var pesos = new double[4];
        if (mesesConDatos > 0) for (int i = 0; i < 4; i++) pesos[i] = sumPesos[i] / mesesConDatos;
        return (pesos, mesesConDatos);
    }

    // ── Persistencia de curvas en PresupuestosCurvas_RM (tabla creada por TI, ver
    // SQL/PresupuestosCurvas_RM.sql) — pedido explícito del usuario 2026-08-29: "que no
    // tengas que cargarla diariamente y cada vez que se llame". Una curva calculada
    // para un (tipo,clave,año,mes) dado es un promedio de los 6 MESES ANTERIORES,
    // TODOS ya cerrados para cuando se pide — una vez calculada es estable para
    // siempre (no cambia salvo que TI corrija datos históricos muy atrás), así que se
    // guarda 1 sola vez, SIN vencimiento, en vez de recalcularse cada 24h como antes
    // (solo en memoria, que además se perdía en cada reciclado de IIS). Si la tabla
    // todavía no existe (TI no ha corrido el script) o el usuario de BD no tiene
    // permiso de escritura ahí, ambos métodos se degradan silenciosamente a "no
    // persistido" — la app sigue funcionando exactamente igual que antes (recalcula
    // contra VWVentas cada vez que haga falta, sin romper nada).
    // canId (default 0 = "todos los canales", el único valor que existía antes de agregar la
    // columna CurCanID vía SQL/PresupuestosCurvas_RM_Canal.sql) — con default 0, cualquier
    // caller que no sepa de canales (ej. ApiPdfPresController, PresupuestosWarmupService) sigue
    // compilando y funcionando idéntico a antes.
    // 'logger' opcional (agregado 2026-09-24, pedido "eficientizar" el tiempo de
    // Presupuestos): antes esta lectura/escritura se degradaba en TOTAL silencio si
    // fallaba (tabla sin crear, columna faltante, o sin permiso) — lo cual es correcto
    // para no romper la app, pero significa que si la persistencia lleva meses fallando
    // en producción (ej. permiso de escritura nunca otorgado sobre esta tabla _RM en
    // particular), NADIE se entera y cada curva se recalcula contra VWVentas (~17s) en
    // CADA request, para siempre, sin que se vea en ningún lado. Con logger, ese fallo
    // ahora queda en el log (Warning, no rompe nada) — diagnóstico, no cambia el
    // comportamiento de degradación silenciosa hacia el resto de la app.
    public static double[]? LeerCurvaDeTabla(SqlConnection cn, string tipo, string claveRef, int anio, int mes, int canId = 0, ILogger? logger = null)
    {
        try
        {
            using var cmd = new SqlCommand(
                "SELECT CurValorJSON FROM PresupuestosCurvas_RM WITH (NOLOCK) WHERE CurTipo=@t AND CurClaveRef=@c AND CurCanID=@can AND CurAnio=@a AND CurMes=@m",
                cn) { CommandTimeout = 15 };
            cmd.Parameters.AddWithValue("@t", tipo);
            cmd.Parameters.AddWithValue("@c", claveRef);
            cmd.Parameters.AddWithValue("@can", canId);
            cmd.Parameters.AddWithValue("@a", anio);
            cmd.Parameters.AddWithValue("@m", mes);
            var json = cmd.ExecuteScalar() as string;
            return json is null ? null : JsonSerializer.Deserialize<double[]>(json);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "LeerCurvaDeTabla falló para {Tipo}/{Clave}/can{Can}/{Anio}{Mes:00} — se recalculará contra VWVentas (tabla PresupuestosCurvas_RM sin crear, columna faltante, o sin permiso de lectura)", tipo, claveRef, canId, anio, mes);
            return null;
        }
    }

    public static void GuardarCurvaEnTabla(SqlConnection cn, string tipo, string claveRef, int anio, int mes, double[] valor, int canId = 0, ILogger? logger = null)
    {
        try
        {
            using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM PresupuestosCurvas_RM WHERE CurTipo=@t AND CurClaveRef=@c AND CurCanID=@can AND CurAnio=@a AND CurMes=@m)
    UPDATE PresupuestosCurvas_RM SET CurValorJSON=@j, CurFechaCalculo=GETDATE() WHERE CurTipo=@t AND CurClaveRef=@c AND CurCanID=@can AND CurAnio=@a AND CurMes=@m
ELSE
    INSERT INTO PresupuestosCurvas_RM (CurTipo, CurClaveRef, CurCanID, CurAnio, CurMes, CurValorJSON, CurFechaCalculo) VALUES (@t,@c,@can,@a,@m,@j,GETDATE())",
                cn) { CommandTimeout = 15 };
            cmd.Parameters.AddWithValue("@t", tipo);
            cmd.Parameters.AddWithValue("@c", claveRef);
            cmd.Parameters.AddWithValue("@can", canId);
            cmd.Parameters.AddWithValue("@a", anio);
            cmd.Parameters.AddWithValue("@m", mes);
            cmd.Parameters.AddWithValue("@j", JsonSerializer.Serialize(valor));
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // Sigue degradándose en silencio hacia el resto de la app (no se relanza) —
            // solo ahora queda constancia en el log de POR QUÉ no quedó persistida.
            logger?.LogWarning(ex, "GuardarCurvaEnTabla falló para {Tipo}/{Clave}/can{Can}/{Anio}{Mes:00} — no quedó persistida, se recalculará contra VWVentas la próxima vez que se pida (tabla sin crear, columna faltante, o sin permiso de escritura)", tipo, claveRef, canId, anio, mes);
        }
    }

    // Canales que agrupan como "Minorista" — único grupo donde se confirmó (con ejemplos
    // reales de fecha de pedido vs. fecha de factura) un rezago fijo de 1 día hábil entre
    // preventa y facturación. Mayorista (3000) e Institucional (7000) quedan fuera a
    // propósito: Mayorista es demasiado inestable (pedidos que tardan varios días en
    // facturarse) para asumir un patrón fijo.
    public static readonly int[] CanalesMinorista = { 2000, 4000, 5000, 6000, 9000 };

    // Días hábiles "efectivos" para el Proyectado (lineal) y el Presupuesto al día —
    // NO para Alcance%/Estimado%, que siguen usando diasT normal. Se aplica a TODOS
    // los canales por igual (decisión del usuario 2026-08-25, 4ª ronda: el rezago de
    // 1 día entre "se vende" y "se factura" se ve en Minorista con ejemplos reales
    // exactos —factura de HOY = pedido de AYER; cierre de mes factura ~2 días de golpe;
    // día 1 factura casi nada—, pero el usuario pidió aplicar el mismo −1 día lineal a
    // TODOS los canales, no solo Minorista). El caso de Mayorista (venta "totalmente
    // anormal/irregular" según el usuario) NO se corrige aquí con un ajuste fijo — se
    // maneja aparte con el patrón de 4 franjas en el Estimado (ver CurvaPorFranjas,
    // canId específico por Canal, 2026-09-05 — ya no está pendiente).
    public static int DiasTranscurridosAjustado(int diasHabilesTranscurridos, int diasHabilesDelMes)
    {
        if (diasHabilesTranscurridos >= diasHabilesDelMes) return diasHabilesDelMes; // mes ya cerrado, sin rezago
        return Math.Max(0, diasHabilesTranscurridos - 1);
    }

    // Recalcula PresupuestoAlDia/VentasProyectadas/ProyectadoPct de 'r' sumando el
    // aporte de N franjas (normalmente 1 por canal), cada una con su PROPIO diasT —
    // cada canal puede tener su propia MAX(PreFechaActualizacion) (Mayorista a veces
    // se queda varios días sin facturar, según el usuario), así que mezclarlas bajo
    // un solo diasT "global" sobreestima qué tan al día está el canal más lento.
    // Minorista además ya viene con el rezago pedido→factura aplicado por el llamador
    // (ver DiasTranscurridosMinorista) antes de pasar su franja aquí. Deja Alcance%
    // intacto (se calcula sobre el total combinado, sin split, en Calcular()) — Estimado
    // tiene su propio ajuste hermano, ver AjustarEstimadoPorCanal más abajo.
    public static void AjustarProyectadoPorCanal(KpiResultado r, IEnumerable<(double presu, double ejec, int diasT)> franjas, int diasPeriodo)
    {
        if (diasPeriodo <= 0) return;
        double presuAlDia = 0, proyectado = 0;
        foreach (var f in franjas)
        {
            presuAlDia += f.presu * f.diasT / diasPeriodo;
            // Franja/canal cerrado (diasT ya llegó al total del período) -> su aporte al
            // Proyectado es exactamente su Ejecutado, sin pasar por la división — mismo
            // motivo que el bypass de Calcular(): evita que un período ya terminado
            // muestre Proyectado≠Ejecutado por el más mínimo margen.
            if (f.diasT >= diasPeriodo) proyectado += f.ejec;
            else if (f.diasT > 0) proyectado += f.ejec / f.diasT * diasPeriodo;
        }
        r.PresupuestoAlDia = presuAlDia;
        r.VentasProyectadas = proyectado;
        r.ProyectadoPct = r.Presupuesto != 0 ? 100.0 * proyectado / r.Presupuesto : null;
    }

    // Recalcula VentasEstimadas/EstimadoPct de 'r' sumando el aporte de N franjas de CANAL, cada
    // una con SU PROPIA curva de estacionalidad — pedido explícito del usuario 2026-09-07: "el
    // peso tiene que ser sí o sí por canal, para la compañía completa, filtremos o no". Una
    // curva "general" (compañía completa mezclada) no representa bien a NINGÚN canal —
    // verificado en vivo que Mayorista concentra ~47% al cierre de mes vs. ~33% de Minorista;
    // usar la curva general infla o desinfla el Estimado de cada canal según para qué lado se
    // parezca menos. Mismo principio que AjustarProyectadoPorCanal, pero para Estimado: cada
    // franja/canal decide su propio "cerrado" (diasT alcanzó diasPeriodo) y su propio factor de
    // avance contra SU curva; se suman los aportes en RD$/unidades, no los factores.
    public static void AjustarEstimadoPorCanal(KpiResultado r, IEnumerable<(double presu, double ejec, int diasT, double[]? curva)> franjas, int diasPeriodo)
    {
        double estimado = 0; bool algunaCurva = false;
        foreach (var f in franjas)
        {
            if (f.curva == null)
            {
                // Sin curva para este canal (p.ej. sinCurva=true, o 0 meses con historial
                // real) -> no hay nada que proyectar más allá de lo ya ejecutado, igual que
                // el bypass equivalente en Calcular().
                estimado += f.ejec;
                continue;
            }
            algunaCurva = true;
            bool cerrado = diasPeriodo > 0 && f.diasT >= diasPeriodo;
            if (cerrado) { estimado += f.ejec; continue; }
            double factor = FactorAvance(f.curva, f.diasT, diasPeriodo);
            estimado += factor > 0.01 ? f.ejec / factor : f.ejec;
        }
        if (!algunaCurva) { r.VentasEstimadas = null; r.EstimadoPct = null; return; }
        r.VentasEstimadas = estimado;
        r.EstimadoPct = r.Presupuesto != 0 ? 100.0 * estimado / r.Presupuesto : null;
    }

    public static KpiResultado Calcular(double presupuesto, double ejecutado, int diasT, int diasPeriodo, double[]? curva, DateTime? ultima = null)
    {
        var r = new KpiResultado
        {
            Presupuesto = presupuesto,
            Ejecutado = ejecutado,
            DiasTranscurridos = diasT,
            DiasDelPeriodo = diasPeriodo,
            Ultima = ultima,
        };
        r.DiasTranscurridosAjustado = DiasTranscurridosAjustado(diasT, diasPeriodo);
        r.DiasRestantes = Math.Max(0, diasPeriodo - r.DiasTranscurridosAjustado);
        r.NecesidadDiaria = r.DiasRestantes > 0 ? (presupuesto - ejecutado) / r.DiasRestantes : null;
        r.PresupuestoAlDia = diasPeriodo > 0 ? presupuesto * diasT / diasPeriodo : presupuesto;
        r.AlcancePct = presupuesto != 0 ? 100.0 * ejecutado / presupuesto : null;
        // Período cerrado (diasT ya alcanzó el total de días hábiles del período, gracias
        // al ajuste de calendario en DiasTranscurridos/DiasTranscurridosAnio) -> Proyectado
        // y Estimado son SIEMPRE exactamente iguales a Ejecutado — no hay nada más que
        // "proyectar" ni "estimar" de un período que ya terminó. Pedido explícito del
        // usuario 2026-08-29: "solo puede cambiar el Proyectado/Estimado del mes actual;
        // los meses anteriores deben ser iguales [al Ejecutado]". Sin este bypass, la
        // curva de estacionalidad (factor de avance) podía dar un valor levemente
        // distinto de 1.0 en el último día del mes (promedio de varios meses históricos,
        // no cae exacto en 1.0) y seguía mostrando Estimado≠Ejecutado en meses cerrados.
        bool cerrado = diasPeriodo > 0 && diasT >= diasPeriodo;
        if (diasT > 0)
        {
            r.VentasProyectadas = cerrado ? ejecutado : ejecutado / diasT * diasPeriodo;
            r.ProyectadoPct = presupuesto != 0 ? 100.0 * r.VentasProyectadas.Value / presupuesto : null;
        }
        if (curva != null)
        {
            double est;
            if (cerrado) est = ejecutado;
            else { double factor = FactorAvance(curva, diasT, diasPeriodo); est = factor > 0.01 ? ejecutado / factor : ejecutado; }
            r.VentasEstimadas = est;
            r.EstimadoPct = presupuesto != 0 ? 100.0 * est / presupuesto : null;
        }
        return r;
    }

    // Mapa Cat1ID -> Cat3ID "dominante" (el suplidor con más presupuesto entre los
    // productos de esa categoría ese mes, vía MNTDPROD) — permite inferir Suplidor+
    // Dinero por día a partir de Categoría+Dinero (que sí tiene historial diario nativo,
    // ya que Suplidor+Dinero no lo tiene). Misma consulta y misma regla de negocio que
    // ApiPresupuestosController.ObtenerMapaCat1ACat3 (pedido explícito del usuario
    // 2026-08-25: "cada categoría pertenece a un suplidor... si está en 2 o más,
    // colócala en el que mayor presupuesto tenga") — vive aquí (sin caché de instancia,
    // sin IMemoryCache) para que informes de OTROS controllers (ej. ApiPdfPresController,
    // "Cierre Diario", 2026-09-03) puedan usarla sin instanciar ApiPresupuestosController.
    // Sin caché propia a propósito: se llama una vez por generación de reporte, no en un
    // loop de UI interactiva — no justifica la complejidad de un candado de concurrencia.
    public static Dictionary<int, int> MapaCat1ACat3Dia(SqlConnection cn, int anio, int mes)
    {
        var mejor = new Dictionary<int, (int cat3, double presu)>();
        using var cmd = new SqlCommand(@"
SELECT pr.Cat1ID, pr.Cat3ID, SUM(ph.PrePresupuesto) Presu
FROM PresupuestosHistoricos ph WITH (NOLOCK)
CROSS APPLY (SELECT TRY_CAST(CASE WHEN LEFT(ph.PreReferencia,1)='V' THEN SUBSTRING(ph.PreReferencia,2,20) ELSE ph.PreReferencia END AS INT) AS ProdId) x
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=x.ProdId
WHERE ph.PreTipo='MNTDPROD' AND ph.PreAnio=@anio AND ph.PreMes=@mes
  AND pr.Cat1ID IS NOT NULL AND pr.Cat3ID IS NOT NULL
GROUP BY pr.Cat1ID, pr.Cat3ID", cn) { CommandTimeout = 60 };
        cmd.Parameters.AddWithValue("@anio", anio);
        cmd.Parameters.AddWithValue("@mes", mes);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            int cat1 = rd.GetInt32(0), cat3 = rd.GetInt32(1);
            double presu = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
            if (!mejor.TryGetValue(cat1, out var actual) || presu > actual.presu) mejor[cat1] = (cat3, presu);
        }
        return mejor.ToDictionary(kv => kv.Key, kv => kv.Value.cat3);
    }
}
