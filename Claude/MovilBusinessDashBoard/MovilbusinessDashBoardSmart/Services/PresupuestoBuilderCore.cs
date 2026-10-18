using Microsoft.Data.SqlClient;

namespace DashVisitas.Services;

// ── Motor de cálculo del módulo "Presupuestos Smart" (creación/edición) ────
// Sin estado, sin conexión propia — igual patrón que PresupuestosCore.cs: los
// métodos reciben la conexión ya abierta. Este archivo cubre la GENERACIÓN de
// propuestas (base histórica + variables); PresupuestosCore.cs sigue siendo el
// dueño de las fórmulas de Alcance/Proyectado/Estimado usadas en consulta.
//
// Dos bases, misma instancia de SQL Server. Pedido explícito del usuario
// 2026-08-30: la conexión de este módulo es SIEMPRE contra MovilBusiness5STD
// (producción) — ahí viven la data real (ventas, rutas, productos), la
// configuración/metodología (PresupuestoVariables_RM/Valores_RM) Y las tablas
// de trabajo de cada propuesta (PresupuestoLotes_RM/Staging_RM/Publicaciones_RM).
// SOLO PresupuestosHistoricos (el presupuesto real del sistema) se referencia
// con 3 partes hacia MovilBusiness5STD_Test mientras dure la etapa de prueba —
// es el único INSERT/UPDATE/DELETE que no se quiere hacer directo en
// producción todavía. Cuando el usuario decida consolidar, "destinoDb" pasa a
// "" y las queries de este archivo quedan apuntando a la misma base sin tocar
// el SQL.
public static class PresupuestoBuilderCore
{
    private static string Pref(string db) => string.IsNullOrEmpty(db) ? "dbo." : $"{db}.dbo.";

    // ── 1) Universo de productos por vendedor ───────────────────────────────
    // RepresentantesGrupoProductos.RepCodigo -> GrpCodigo(s) -> GrupoProductosDetalle.ProID.
    // Resuelve el caso de un cliente visitado por 2 vendedores de divisiones
    // distintas SIN tener que elegir "el vendedor del cliente": cada rep se
    // queda solo con la venta de SUS productos (grupos disjuntos, verificado en
    // vivo 2026-08-29: cliente 24165 -> rep 130 = grupo '11' "Minorista Espejo",
    // rep 160 = grupo '12' "Minoristas I", cero overlap).
    public static Dictionary<string, HashSet<int>> UniversoProductosPorRep(SqlConnection cn, IReadOnlyCollection<string>? repsFiltro = null)
    {
        var parms = new Dictionary<string, object>();
        string filtroRep = "";
        if (repsFiltro is { Count: > 0 })
        {
            var names = repsFiltro.Select((r, i) => { var n = $"@rf{i}"; parms[n] = r; return n; }).ToList();
            filtroRep = $"WHERE rgp.RepCodigo IN ({string.Join(",", names)})";
        }
        var sql = $@"
SELECT rgp.RepCodigo, gpd.ProID
FROM RepresentantesGrupoProductos rgp WITH (NOLOCK)
INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo = rgp.GrpCodigo
{filtroRep}";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        var resultado = new Dictionary<string, HashSet<int>>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue; // fila rara sin RepCodigo/ProID, no atribuible
            var rep = rd.GetString(0);
            var proId = rd.GetInt32(1);
            if (!resultado.TryGetValue(rep, out var set)) { set = new HashSet<int>(); resultado[rep] = set; }
            set.Add(proId);
        }
        return resultado;
    }

    // ── 2) Base histórica por Rep×Producto ───────────────────────────────────
    // Fuente: VWVentas+VWVentasDetalle (cambiado 2026-08-30, pedido explícito
    // del usuario, verificado en vivo que era la causa de una corrida de 7+
    // minutos: la fuente anterior, VwPedidosVsFacutadosMenosDevolucionRM,
    // ejecutaba por dentro un fragmento carísimo ligado a integración SAP).
    // YA ES NETO sin necesidad de restar nada: verificado en vivo (jul-2026)
    // que VWVentasDetalle trae las devoluciones como filas con VenCantidad
    // NEGATIVO mezcladas con las ventas positivas (7,411 filas negativas /
    // -66,196 unidades vs. 131,360 positivas / 1,094,446 unidades ese mes) —
    // un SUM() simple ya las netea.
    //
    // Reatribuida a la asignación de `clientesDetalle` (2026-09-01, reemplaza a
    // RutaVisitas — pedido explícito del usuario: "eso no debe ser así, debe
    // ser ClientesDetalle, quitemos el tema de RutaVisitas"). Verificado en
    // vivo que RutaVisitas dejaba fuera ~54,620 pares (Cliente,Vendedor) que
    // clientesDetalle SÍ tiene, de los cuales 635 tenían venta real reciente
    // (~RD$16-17MM/3 meses) — esa era una parte del hueco entre esta base y la
    // venta real. Un cliente puede tener varias filas en clientesDetalle (2+
    // vendedores atendiéndolo por categorías distintas, confirmado por el
    // usuario como normal) — igual que antes con RutaVisitas, el filtro de
    // universo de productos de CADA rep (paso 1) es lo que evita que un
    // cliente con 2+ vendedores duplique o se atribuya mal: cada rep solo se
    // queda con la venta de los productos que SÍ están en su grupo. Se
    // verificó que no hay pares (CliID,RepCodigo) duplicados exactos en
    // clientesDetalle, así que el JOIN no infla el monto por sí solo.
    //
    // Filtro `Representantes.RepCargo IN ('PreVendedor','Vendedor')` (2026-09-01,
    // pedido explícito del usuario: "los representantes tienen que ser
    // PreVendedor o Vendedores, solo estos pueden tener presupuestos en
    // unidades") — SIN este filtro, `clientesDetalle` sí duplicaba venta real:
    // muchos clientes tienen ahí, además del vendedor real (RepCargo=
    // 'PreVendedor'), un código de SUPERVISOR (RepCargo='Supervisor', ej. "S4"
    // = Ramon E. Pimentel) agregado para que el supervisor tenga visibilidad
    // de los clientes de su equipo — pero esos códigos cargan grupos de
    // producto amplios/catch-all (ej. grupo "10 - Grupo Minoristas
    // TodoProductos") que se solapan con CUALQUIER vendedor real del mismo
    // cliente. Verificado en vivo (jun-ago 2026): ~88% de la venta duplicada
    // desaparece con este filtro; el ~12% restante es solapamiento genuino
    // entre 2 vendedores PreVendedor reales (pendiente de investigar aparte —
    // la configuración de `RepresentantesGrupoProductos` de esos casos no es
    // tan disjunta como se asumía).
    public record BaseHistoricaFila(string RepCodigo, int ProId, double UnidadesNetas, double MontoNeto);

    public static List<BaseHistoricaFila> CalcularBaseHistorica(
        SqlConnection cn, DateTime desde, DateTime hastaExclusive,
        Dictionary<string, HashSet<int>> universoPorRep, IReadOnlyCollection<string>? repsFiltro = null)
    {
        var reps = repsFiltro is { Count: > 0 } ? repsFiltro : universoPorRep.Keys;
        var parms = new Dictionary<string, object> { ["@desde"] = desde, ["@hasta"] = hastaExclusive };
        var repNames = reps.Select((r, i) => { var n = $"@rp{i}"; parms[n] = r; return n; }).ToList();
        if (repNames.Count == 0) return new List<BaseHistoricaFila>();

        var sql = $@"
SELECT rv.RepCodigo, d.ProID, SUM(d.VenCantidad) Unidades, SUM(d.VenCantidad*d.VenPrecio - ISNULL(d.VenTotalDescuento,0)) Monto
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN clientesDetalle rv WITH (NOLOCK) ON rv.CliID = v.CliID
INNER JOIN Representantes rpv WITH (NOLOCK) ON rpv.RepCodigo = rv.RepCodigo AND rpv.RepCargo IN ('PreVendedor','Vendedor')
WHERE v.VenEstatus<>0 AND v.VenFecha >= @desde AND v.VenFecha < @hasta
  AND rv.RepCodigo IN ({string.Join(",", repNames)})
GROUP BY rv.RepCodigo, d.ProID";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);

        var resultado = new List<BaseHistoricaFila>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            // VWVentas/VWVentasDetalle son VISTAS — a diferencia de una tabla con
            // NOT NULL real, pueden traer RepCodigo/ProID nulos en filas raras
            // (verificado en vivo: causaba "Data is Null" al llamar GetString/
            // GetInt32 directo). Se descartan, no son atribuibles a ningún rep.
            if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
            var rep = rd.GetString(0);
            var proId = rd.GetInt32(1);
            if (!universoPorRep.TryGetValue(rep, out var universo) || !universo.Contains(proId)) continue; // fuera del grupo de producto de este rep
            double uni = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
            double mto = rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3));
            resultado.Add(new BaseHistoricaFila(rep, proId, uni, mto));
        }
        return resultado;
    }

    // ── 1b) Filtro de Canal + exclusión de Institucional ────────────────────
    // Pedido explícito del usuario 2026-08-30: ningún crecimiento/redistribución
    // debe considerar el canal Institucional. CanID=7000 es la convención YA
    // establecida en el resto de la app (Services/PresupuestosCore.cs
    // CanalesMinorista, Views/Presupuestos e Historicos — "todos los canales
    // excepto Institucional" es el default de esos análisis). Sin
    // 'canalesPermitidos' explícito, se aplica ese mismo default aquí.
    public const int CanalInstitucional = 7000;

    public static List<string> FiltrarRepsPorCanal(SqlConnection cn, IReadOnlyCollection<string> reps, IReadOnlyCollection<int>? canalesPermitidos)
    {
        if (reps.Count == 0) return new List<string>();
        var parms = new Dictionary<string, object>();
        var repNames = reps.Select((r, i) => { var n = $"@rc{i}"; parms[n] = r; return n; }).ToList();
        var canalPorRep = new Dictionary<string, int>();
        using (var cmd = new SqlCommand(
            $"SELECT RepCodigo, ISNULL(CanID,0) FROM RepresentantesAdicionales WITH (NOLOCK) WHERE RepCodigo IN ({string.Join(",", repNames)})", cn) { CommandTimeout = 30 })
        {
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) if (!rd.IsDBNull(0)) canalPorRep[rd.GetString(0)] = rd.GetInt32(1);
        }
        bool permitido(int canId) => canalesPermitidos is { Count: > 0 } ? canalesPermitidos.Contains(canId) : canId != CanalInstitucional;
        return reps.Where(r => permitido(canalPorRep.GetValueOrDefault(r, 0))).ToList();
    }

    // ── 2a-1) Filtro por Supervisor (2026-09-06, pantalla nueva "Editar
    // Presupuestos") ─────────────────────────────────────────────────────────
    // Mismo espíritu que FiltrarRepsPorCanal — se aplica ANTES de calcular
    // universo/base histórica para que ningún rep fuera del supervisor pedido
    // entre siquiera al cálculo.
    public static List<string> FiltrarRepsPorSupervisor(SqlConnection cn, IReadOnlyCollection<string> reps, IReadOnlyCollection<string>? supervisoresPermitidos)
    {
        if (supervisoresPermitidos is not { Count: > 0 } || reps.Count == 0) return reps.ToList();
        var parms = new Dictionary<string, object>();
        var repNames = reps.Select((r, i) => { var n = $"@sv{i}"; parms[n] = r; return n; }).ToList();
        var supervisorPorRep = new Dictionary<string, string>();
        using (var cmd = new SqlCommand(
            $"SELECT RepCodigo, ISNULL(RepSupervisor,'') FROM Representantes WITH (NOLOCK) WHERE RepCodigo IN ({string.Join(",", repNames)})", cn) { CommandTimeout = 30 })
        {
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) if (!rd.IsDBNull(0)) supervisorPorRep[rd.GetString(0)] = rd.GetString(1);
        }
        var permitidos = new HashSet<string>(supervisoresPermitidos);
        return reps.Where(r => permitidos.Contains(supervisorPorRep.GetValueOrDefault(r, ""))).ToList();
    }

    // ── 2b) Reps "activos" a evaluar cuando el usuario NO filtra por rep ─────
    // Pedido explícito del usuario 2026-08-30: no correr el motor para TODOS
    // los ~209 reps del catálogo — solo los que de verdad importan: los que
    // tuvieron venta en el rango base, o que tienen clientes asignados en
    // clientesDetalle (2026-09-01, reemplaza a RutaVisitas — ver nota en
    // CalcularBaseHistorica) — pueden no haber vendido ese rango exacto pero
    // siguen activos. Reduce drásticamente el trabajo cuando no se especifica
    // un subconjunto de vendedores.
    public static HashSet<string> ResolverRepsActivos(SqlConnection cn, DateTime desde, DateTime hastaExclusive)
    {
        var resultado = new HashSet<string>();
        using (var cmd = new SqlCommand(
            "SELECT DISTINCT RepCodigo FROM VWVentas WITH (NOLOCK) WHERE VenEstatus<>0 AND VenFecha >= @desde AND VenFecha < @hasta", cn) { CommandTimeout = 60 })
        {
            cmd.Parameters.AddWithValue("@desde", desde);
            cmd.Parameters.AddWithValue("@hasta", hastaExclusive);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) if (!rd.IsDBNull(0)) resultado.Add(rd.GetString(0));
        }
        using (var cmd = new SqlCommand(@"
SELECT DISTINCT cd.RepCodigo FROM clientesDetalle cd WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')", cn) { CommandTimeout = 60 })
        {
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) if (!rd.IsDBNull(0)) resultado.Add(rd.GetString(0));
        }
        return resultado;
    }

    // ── 2c) Días hábiles en un rango arbitrario (puede cruzar varios meses) ──
    // Pedido explícito del usuario 2026-08-30: si el reparto del propuesto entre
    // los meses destino es "lineal", debe tomar en cuenta los días hábiles de
    // cada mes (un mes con más días de venta debe llevar proporcionalmente más)
    // — antes se aplicaba el mismo promedio mensual plano a todos los meses
    // destino por igual. Reusa Services.PresupuestosCore.DiasHabiles (L-S,
    // excluye domingo) — no se duplica esa lógica aquí, mismo namespace.
    public static int DiasHabilesEnRango(DateTime desde, DateTime hastaExclusive)
    {
        var hastaInclusive = hastaExclusive.AddDays(-1);
        if (hastaInclusive < desde) return 0;
        int total = 0;
        var cursor = new DateTime(desde.Year, desde.Month, 1);
        while (cursor <= hastaInclusive)
        {
            int diasEnMes = DateTime.DaysInMonth(cursor.Year, cursor.Month);
            int diaInicio = (cursor.Year == desde.Year && cursor.Month == desde.Month) ? desde.Day : 1;
            int diaFin = (cursor.Year == hastaInclusive.Year && cursor.Month == hastaInclusive.Month) ? hastaInclusive.Day : diasEnMes;
            int hastaFin = PresupuestosCore.DiasHabiles(cursor.Year, cursor.Month, diaFin);
            int hastaAntesInicio = diaInicio > 1 ? PresupuestosCore.DiasHabiles(cursor.Year, cursor.Month, diaInicio - 1) : 0;
            total += hastaFin - hastaAntesInicio;
            cursor = cursor.AddMonths(1);
        }
        return total;
    }

    // ── 3) Variables ajustables — resolución en cascada ─────────────────────
    // Viven en la misma base que la conexión (producción) — ver nota de
    // arriba. BUG DE RENDIMIENTO CORREGIDO 2026-08-30: la versión anterior
    // consultaba la BD 1 vez POR FILA (rep×producto) POR MES POR VARIABLE —
    // con la base histórica completa eso eran decenas de miles de idas y
    // vueltas a la BD en una sola request, y el usuario reportó una "Generar"
    // colgada más de 10 minutos. Ahora se CARGA UNA SOLA VEZ toda la tabla de
    // la variable pedida (es un catálogo de configuración, siempre chico) y
    // la cascada se resuelve en memoria por cada fila.
    public sealed record VariableRegla(string AmbitoTipo, string? AmbitoValor, string PeriodoTipo, int? PeriodoTrimestre, int? PeriodoMes, double Valor);

    public static List<VariableRegla> CargarVariable(SqlConnection cn, string varCodigo, int anio)
    {
        using var cmd = new SqlCommand(@"
SELECT AmbitoTipo, AmbitoValor, PeriodoTipo, PeriodoTrimestre, PeriodoMes, Valor
FROM PresupuestoVariablesValores_RM WITH (NOLOCK)
WHERE VarCodigo=@var AND PeriodoAnio=@anio", cn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@var", varCodigo);
        cmd.Parameters.AddWithValue("@anio", anio);
        var resultado = new List<VariableRegla>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(2) || rd.IsDBNull(5)) continue; // AmbitoTipo/PeriodoTipo/Valor son NOT NULL en el schema, pero por si acaso
            resultado.Add(new VariableRegla(
                rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetString(1), rd.GetString(2),
                rd.IsDBNull(3) ? null : Convert.ToInt32(rd.GetValue(3)), rd.IsDBNull(4) ? null : Convert.ToInt32(rd.GetValue(4)),
                Convert.ToDouble(rd.GetValue(5))));
        }
        return resultado;
    }

    // Resolución en memoria (sin BD) — más específico gana: Mes > Trimestre >
    // Año (mismo Año); RutaSuplidor > Suplidor/Categoria > Global. Sin ninguna
    // fila que aplique, devuelve 'valorDefault' (neutro) — el motor nunca
    // inventa un ajuste que Gerencia no haya configurado.
    public static double ResolverVariableEnMemoria(
        List<VariableRegla> reglas, int mes, string? repCodigo, string? cat3Id, string? cat1Id, double valorDefault = 1.0)
    {
        if (reglas.Count == 0) return valorDefault;
        int trimestre = (mes - 1) / 3 + 1;
        var candidatos = new List<(string ambitoTipo, string? ambitoValor, int prioridadAmbito)>();
        if (repCodigo != null && cat3Id != null) candidatos.Add(("RutaSuplidor", $"{repCodigo}|{cat3Id}", 3));
        if (cat3Id != null) candidatos.Add(("Suplidor", cat3Id, 2));
        if (cat1Id != null) candidatos.Add(("Categoria", cat1Id, 2));
        candidatos.Add(("Global", null, 1));

        bool aplica(VariableRegla r) => r.PeriodoTipo switch
        {
            "Mes" => r.PeriodoMes == mes,
            "Trimestre" => r.PeriodoTrimestre == trimestre,
            _ => true, // Anio
        };
        int prioridadPeriodo(string p) => p switch { "Mes" => 3, "Trimestre" => 2, _ => 1 };

        foreach (var (ambitoTipo, ambitoValor, _) in candidatos.OrderByDescending(c => c.prioridadAmbito))
        {
            var match = reglas
                .Where(r => r.AmbitoTipo == ambitoTipo && r.AmbitoValor == ambitoValor && aplica(r))
                .OrderByDescending(r => prioridadPeriodo(r.PeriodoTipo))
                .FirstOrDefault();
            if (match != null) return match.Valor;
        }
        return valorDefault;
    }

    // ── 4) Snapshot de lo ya publicado en PresupuestosHistoricos ────────────
    // Para el diff Propuesto-vs-Actual en la pantalla de revisión, y para el
    // publicar (delete+insert del rango exacto afectado). ÚNICA tabla de todo
    // este archivo que se referencia hacia una base distinta a la conexión —
    // 'destinoDb' = "MovilBusiness5STD_Test" mientras dure la prueba (viene de
    // appsettings.json "PresupuestosHistoricosDb"); pasar "" cuando se
    // consolide todo en una sola base.
    public static Dictionary<(string rep, string referencia), double> ObtenerPresupuestoActual(
        SqlConnection cn, string destinoDb, string preTipo, int anio, int mes, IReadOnlyCollection<string>? repsFiltro = null)
    {
        var p = Pref(destinoDb);
        var parms = new Dictionary<string, object> { ["@tipo"] = preTipo, ["@anio"] = anio, ["@mes"] = mes };
        string filtroRep = "";
        if (repsFiltro is { Count: > 0 })
        {
            var names = repsFiltro.Select((r, i) => { var n = $"@rp{i}"; parms[n] = r; return n; }).ToList();
            filtroRep = $"AND RepCodigo IN ({string.Join(",", names)})";
        }
        using var cmd = new SqlCommand($@"
SELECT RepCodigo, PreReferencia, PrePresupuesto
FROM {p}PresupuestosHistoricos WITH (NOLOCK)
WHERE PreTipo=@tipo AND PreAnio=@anio AND PreMes=@mes {filtroRep}", cn) { CommandTimeout = 60 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        var resultado = new Dictionary<(string, string), double>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            resultado[(rd.GetString(0), rd.GetString(1))] = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
        return resultado;
    }

    // ── 4b) Activación mínima/ideal por Suplidor×Categoría ──────────────────
    // Pedido explícito del usuario 2026-08-30 ("no sé dónde entraría el tema de
    // las activaciones por suplidor mínimas... obligatorio en algunos casos,
    // ideal en otros"). Decidido con el usuario (AskUserQuestion): "Obligatorio"
    // fuerza el piso del presupuesto (sube lo necesario, repartido entre los SKU
    // de esa categoría/suplidor según su mezcla actual) — configurable por fila
    // si además debe bloquear Publicar cuando ni forzando se alcanza la meta.
    // "Ideal" nunca fuerza nada, solo se muestra como brecha informativa.
    // CanId/RepTipe (2026-09-01, pedido explícito del usuario: "agregar
    // parametros por activaciones % Categoria/suplidor pero por canales y
    // division de vendedores (Reptipe) para que cada uno tenga su propio
    // escenario") + RepCodigo (2026-09-01, misma sesión: "esto me debería
    // llevar a asignar presupuestos categoría/Vendedor... en los casos que
    // tenga que dar seguimiento específico"): son 3 CAPAS DE OVERRIDE
    // OPCIONALES sobre el default Cat3×Cat1 de siempre — NO una llave
    // combinada de las dimensiones (mismo criterio ya confirmado con el
    // usuario vía AskUserQuestion para Canal/División, extendido igual a
    // Vendedor). Una fila puede traer RepCodigo, o CanId, o RepTipe, o
    // ninguno (default) — nunca 2 a la vez (ver ResolverActivacionMeta).
    public sealed record ActivacionMeta(int Cat3Id, int Cat1Id, double MetaPct, string Modo, bool BloqueaPublicar, int? CanId, string? RepTipe, string? RepCodigo);

    public static List<ActivacionMeta> CargarActivacionMetas(SqlConnection cn)
    {
        var resultado = new List<ActivacionMeta>();
        using var cmd = new SqlCommand("SELECT Cat3ID, Cat1ID, MetaPct, Modo, BloqueaPublicar, CanID, RepTipe, RepCodigo FROM PresupuestoActivacionMetas_RM WITH (NOLOCK) WHERE Activo=1", cn) { CommandTimeout = 30 };
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            resultado.Add(new ActivacionMeta(rd.GetInt32(0), rd.GetInt32(1), Convert.ToDouble(rd.GetValue(2)), rd.GetString(3), rd.GetBoolean(4),
                rd.IsDBNull(5) ? null : rd.GetInt32(5), rd.IsDBNull(6) ? null : rd.GetString(6), rd.IsDBNull(7) ? null : rd.GetString(7)));
        return resultado;
    }

    // Resuelve, para un Rep×Cat3×Cat1 dado, la meta de activación aplicable —
    // más específico gana: fila con el RepCodigo EXACTO del rep (seguimiento
    // puntual) > fila con RepTipe (división) del rep > fila con CanId (canal)
    // del rep > fila default (los 3 en NULL). Mismo espíritu de cascada que
    // ResolverVariableEnMemoria (PresupuestoVariablesValores_RM), pero sin
    // período (la activación no varía por mes/trimestre hoy).
    public static ActivacionMeta? ResolverActivacionMeta(List<ActivacionMeta> metas, int cat3, int cat1, int? canId, string? repTipe, string? repCodigo = null)
    {
        var candidatas = metas.Where(m => m.Cat3Id == cat3 && m.Cat1Id == cat1).ToList();
        if (candidatas.Count == 0) return null;
        if (repCodigo != null)
        {
            var porRep = candidatas.FirstOrDefault(m => m.RepCodigo == repCodigo);
            if (porRep != null) return porRep;
        }
        if (repTipe != null)
        {
            var porDivision = candidatas.FirstOrDefault(m => m.RepTipe == repTipe);
            if (porDivision != null) return porDivision;
        }
        if (canId != null)
        {
            var porCanal = candidatas.FirstOrDefault(m => m.CanId == canId);
            if (porCanal != null) return porCanal;
        }
        return candidatas.FirstOrDefault(m => m.CanId == null && m.RepTipe == null && m.RepCodigo == null);
    }

    // ── 4b-1) Dimensiones (Canal, División) por rep ─────────────────────────
    // Necesario para resolver la cascada de ActivacionMeta (y reusable para
    // cualquier otra regla futura por Canal/División) — RepresentantesAdicionales
    // ya trae ambos campos, mismo patrón de lookup que 'dims' en Comparativo.
    public static Dictionary<string, (int canId, string repTipe)> CargarDimensionesRep(SqlConnection cn, IReadOnlyCollection<string> reps)
    {
        var resultado = new Dictionary<string, (int, string)>();
        if (reps.Count == 0) return resultado;
        var parms = new Dictionary<string, object>();
        var repNames = reps.Select((r, i) => { var n = $"@rd{i}"; parms[n] = r; return n; }).ToList();
        using var cmd = new SqlCommand($@"
SELECT RepCodigo, ISNULL(CanID,0), ISNULL(RepTipe,'') FROM RepresentantesAdicionales WITH (NOLOCK)
WHERE RepCodigo IN ({string.Join(",", repNames)})", cn) { CommandTimeout = 30 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) resultado[rd.GetString(0)] = (rd.GetInt32(1), rd.GetString(2));
        return resultado;
    }

    // ── 4d) Límites (piso/techo) por vendedor, % sobre su propia base ───────
    // Pedido explícito del usuario 2026-09-01 dentro del rediseño de premisas
    // del motor: "Minimos, Maximos" por vendedor, como % de lo que ese MISMO
    // vendedor vendió en el Rango Base (no un monto fijo en RD$ — decisión
    // confirmada vía AskUserQuestion). Ver SQL/PresupuestoLimitesRep_RM.sql.
    public sealed record LimiteRep(double? PctMinimo, double? PctMaximo);

    public static Dictionary<string, LimiteRep> CargarLimitesRep(SqlConnection cn, IReadOnlyCollection<string> reps)
    {
        var resultado = new Dictionary<string, LimiteRep>();
        if (reps.Count == 0) return resultado;
        var parms = new Dictionary<string, object>();
        var repNames = reps.Select((r, i) => { var n = $"@lr{i}"; parms[n] = r; return n; }).ToList();
        using var cmd = new SqlCommand($@"
SELECT RepCodigo, PctMinimo, PctMaximo FROM PresupuestoLimitesRep_RM WITH (NOLOCK)
WHERE Activo=1 AND RepCodigo IN ({string.Join(",", repNames)})", cn) { CommandTimeout = 30 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            resultado[rd.GetString(0)] = new LimiteRep(rd.IsDBNull(1) ? null : Convert.ToDouble(rd.GetValue(1)), rd.IsDBNull(2) ? null : Convert.ToDouble(rd.GetValue(2)));
        return resultado;
    }

    // ── 4e) Efectividad por vendedor (Visitados / ClientesConPedido) ────────
    // Pedido explícito del usuario 2026-09-01 dentro del rediseño de premisas
    // del motor: "un entorno para configurar la efectividad minima ideal...
    // por division (Reptipe)". Fórmula = MISMA que ya usa el resto de la app
    // (TorreCore.cs "EFECTIVIDAD_VISITA", ApiProductividadController.cs):
    // Efectividad% = ClientesConPedido ÷ Visitados. Reusa las mismas 2
    // fuentes (ResumenVisitasRm, VWPedidos) — NO se inventa una fórmula
    // nueva, solo se le quita el recorte "de hoy" (HOY) para aceptar
    // cualquier rango (el Rango Base del lote).
    public sealed record EfectividadRep(int Visitados, int ClientesConPedido);

    public static Dictionary<string, EfectividadRep> EfectividadPorRep(
        SqlConnection cn, DateTime desde, DateTime hastaExclusive, IReadOnlyCollection<string> reps)
    {
        var acumulado = new Dictionary<string, (int visitados, int conPedido)>();
        if (reps.Count == 0) return new();
        var parms = new Dictionary<string, object> { ["@desde"] = desde, ["@hasta"] = hastaExclusive };
        var repNames = reps.Select((r, i) => { var n = $"@ef{i}"; parms[n] = r; return n; }).ToList();
        using (var cmd = new SqlCommand($@"
SELECT RepCodigo, SUM(ISNULL(Visitados,0)) Visitados
FROM ResumenVisitasRm WITH (NOLOCK)
WHERE FechaVisita>=@desde AND FechaVisita<@hasta AND RepCodigo IN ({string.Join(",", repNames)})
GROUP BY RepCodigo", cn) { CommandTimeout = 60 })
        {
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) acumulado[rd.GetString(0)] = (rd.GetInt32(1), 0);
        }
        using (var cmd = new SqlCommand($@"
SELECT p.RepCodigo, COUNT(DISTINCT p.CliID) CliConPed
FROM VWPedidos p WITH (NOLOCK)
WHERE p.PedFecha>=@desde AND p.PedFecha<@hasta AND p.RepCodigo IN ({string.Join(",", repNames)})
GROUP BY p.RepCodigo", cn) { CommandTimeout = 60 })
        {
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var rep = rd.GetString(0); var conPedido = rd.GetInt32(1);
                var previo = acumulado.GetValueOrDefault(rep, (visitados: 0, conPedido: 0));
                acumulado[rep] = (previo.visitados, conPedido);
            }
        }
        return acumulado.ToDictionary(kv => kv.Key, kv => new EfectividadRep(kv.Value.visitados, kv.Value.conPedido));
    }

    // Modo Ideal/Obligatorio, mismo espíritu que ActivacionMeta pero con 1
    // sola dimensión (División/RepTipe) — RepTipe='' representa la fila
    // default (aplica a cualquier división sin fila propia).
    public sealed record EfectividadMeta(string RepTipe, double MetaPct, string Modo, bool BloqueaPublicar);

    public static List<EfectividadMeta> CargarEfectividadMetas(SqlConnection cn)
    {
        var resultado = new List<EfectividadMeta>();
        using var cmd = new SqlCommand("SELECT RepTipe, MetaPct, Modo, BloqueaPublicar FROM PresupuestoEfectividadMetas_RM WITH (NOLOCK) WHERE Activo=1", cn) { CommandTimeout = 30 };
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            resultado.Add(new EfectividadMeta(rd.GetString(0), Convert.ToDouble(rd.GetValue(1)), rd.GetString(2), rd.GetBoolean(3)));
        return resultado;
    }

    // Más específico gana: fila con el RepTipe exacto del rep > fila default (RepTipe='').
    public static EfectividadMeta? ResolverEfectividadMeta(List<EfectividadMeta> metas, string repTipe)
    {
        return metas.FirstOrDefault(m => m.RepTipe == repTipe) ?? metas.FirstOrDefault(m => m.RepTipe == "");
    }

    // Clientes DISTINTOS (reatribuidos vía clientesDetalle, igual que la base
    // histórica — ver nota en CalcularBaseHistorica) que compraron algo > 0 de
    // cada Categoría (Cat1ID), por rep, acotado al universo de productos de
    // CADA rep (mismo criterio que CalcularBaseHistorica — un cliente con 2+
    // vendedores no se cuenta para el rep que no le corresponde ese producto).
    public static Dictionary<(string rep, int cat1), HashSet<int>> ClientesActivosPorCategoria(
        SqlConnection cn, DateTime desde, DateTime hastaExclusive,
        Dictionary<string, HashSet<int>> universoPorRep, IReadOnlyCollection<string> reps)
    {
        var resultado = new Dictionary<(string, int), HashSet<int>>();
        if (reps.Count == 0) return resultado;
        var parms = new Dictionary<string, object> { ["@desde"] = desde, ["@hasta"] = hastaExclusive };
        var repNames = reps.Select((r, i) => { var n = $"@rp{i}"; parms[n] = r; return n; }).ToList();
        var sql = $@"
SELECT rv.RepCodigo, d.ProID, pr.Cat1ID, v.CliID
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN clientesDetalle rv WITH (NOLOCK) ON rv.CliID = v.CliID
INNER JOIN Representantes rpv WITH (NOLOCK) ON rpv.RepCodigo = rv.RepCodigo AND rpv.RepCargo IN ('PreVendedor','Vendedor')
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
WHERE v.VenEstatus<>0 AND v.VenFecha>=@desde AND v.VenFecha<@hasta AND d.VenCantidad>0
  AND rv.RepCodigo IN ({string.Join(",", repNames)})";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(1) || rd.IsDBNull(2) || rd.IsDBNull(3)) continue;
            var rep = rd.GetString(0); var proId = rd.GetInt32(1); var cat1 = rd.GetInt32(2); var cli = rd.GetInt32(3);
            if (!universoPorRep.TryGetValue(rep, out var universo) || !universo.Contains(proId)) continue;
            var key = (rep, cat1);
            if (!resultado.TryGetValue(key, out var set)) { set = new HashSet<int>(); resultado[key] = set; }
            set.Add(cli);
        }
        return resultado;
    }

    // Cartera (denominador): total de clientes DISTINTOS asignados a cada rep en
    // clientesDetalle (2026-09-01, reemplaza a RutaVisitas — ver nota en
    // CalcularBaseHistorica) — mismo denominador para todas las categorías de ese rep.
    public static Dictionary<string, int> CarteraPorRep(SqlConnection cn, IReadOnlyCollection<string> reps)
    {
        var resultado = new Dictionary<string, int>();
        if (reps.Count == 0) return resultado;
        var parms = new Dictionary<string, object>();
        var repNames = reps.Select((r, i) => { var n = $"@rp{i}"; parms[n] = r; return n; }).ToList();
        using var cmd = new SqlCommand($@"
SELECT cd.RepCodigo, COUNT(DISTINCT cd.CliID) FROM clientesDetalle cd WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
WHERE cd.RepCodigo IN ({string.Join(",", repNames)}) GROUP BY cd.RepCodigo", cn) { CommandTimeout = 60 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) resultado[rd.GetString(0)] = rd.GetInt32(1);
        return resultado;
    }

    // ── 4b-2) Modelo Rappel generalizado (2026-09-06) ───────────────────────
    // Pedido explícito del usuario: además del Monto (META_RAPPEL, catálogo
    // genérico, sigue funcionando igual), ahora también Activaciones (conteo
    // de clientes activos) y Factura (conteo de facturas/pedidos distintos),
    // cada uno por Suplidor O por Categoría — pueden coexistir varios tipos
    // para el mismo Suplidor/Categoría, cada uno actúa como PISO independiente
    // (igual mecanismo "factor máximo" que ya usa Monto).
    public sealed record RappelMeta(int Id, string TipoRequerimiento, string AmbitoTipo, int? Cat3Id, int? Cat1Id, int PeriodoAnio, int? PeriodoMes, double MetaValor);

    public static List<RappelMeta> CargarRappelMetas(SqlConnection cn, int anio)
    {
        var resultado = new List<RappelMeta>();
        using var cmd = new SqlCommand(
            "SELECT Id, TipoRequerimiento, AmbitoTipo, Cat3ID, Cat1ID, PeriodoAnio, PeriodoMes, MetaValor FROM PresupuestoRappelMetas_RM WITH (NOLOCK) WHERE Activo=1 AND PeriodoAnio=@anio", cn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@anio", anio);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            resultado.Add(new RappelMeta(rd.GetInt32(0), rd.GetString(1), rd.GetString(2),
                rd.IsDBNull(3) ? null : rd.GetInt32(3), rd.IsDBNull(4) ? null : rd.GetInt32(4),
                rd.GetInt16(5), rd.IsDBNull(6) ? null : Convert.ToInt32(rd.GetValue(6)), Convert.ToDouble(rd.GetValue(7))));
        return resultado;
    }

    // Más específico gana: fila con PeriodoMes exacto > fila sin mes (todo el año) — mismo
    // criterio que ResolverVariableEnMemoria. AmbitoTipo='Suplidor' compara contra cat3;
    // AmbitoTipo='Categoria' compara contra cat1 — nunca ambos combinados en una fila.
    public static RappelMeta? ResolverRappelMeta(List<RappelMeta> metas, string tipo, int mes, int? cat3, int? cat1)
    {
        var candidatas = metas.Where(m => m.TipoRequerimiento == tipo &&
            ((m.AmbitoTipo == "Suplidor" && cat3 != null && m.Cat3Id == cat3) ||
             (m.AmbitoTipo == "Categoria" && cat1 != null && m.Cat1Id == cat1))).ToList();
        if (candidatas.Count == 0) return null;
        return candidatas.FirstOrDefault(m => m.PeriodoMes == mes) ?? candidatas.FirstOrDefault(m => m.PeriodoMes == null);
    }

    // Facturas (PedSecuencia DISTINTAS) por Rep×Categoría/Suplidor en un rango — para el
    // requerimiento de Rappel tipo 'Factura'. Mismo criterio de atribución que
    // ClientesActivosPorCategoria (clientesDetalle + universo de productos por rep), pero
    // sobre VWPedidos/VWPedidosDetalle (mismo patrón ya usado en otros controllers de este
    // proyecto para "número de facturas": COUNT(DISTINCT PedSecuencia)).
    private static Dictionary<(string rep, int cat), int> FacturasPorGrupo(
        SqlConnection cn, DateTime desde, DateTime hastaExclusive,
        Dictionary<string, HashSet<int>> universoPorRep, IReadOnlyCollection<string> reps, bool porCat1)
    {
        var acumulado = new Dictionary<(string, int), HashSet<int>>();
        if (reps.Count == 0) return new();
        var campoCat = porCat1 ? "pr.Cat1ID" : "pr.Cat3ID";
        var parms = new Dictionary<string, object> { ["@desde"] = desde, ["@hasta"] = hastaExclusive };
        var repNames = reps.Select((r, i) => { var n = $"@rp{i}"; parms[n] = r; return n; }).ToList();
        var sql = $@"
SELECT rv.RepCodigo, d.ProID, {campoCat}, d.PedSecuencia
FROM VWPedidos p WITH (NOLOCK)
INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=p.PedSecuencia AND d.RepCodigo=p.RepCodigo
INNER JOIN clientesDetalle rv WITH (NOLOCK) ON rv.CliID = p.CliID
INNER JOIN Representantes rpv WITH (NOLOCK) ON rpv.RepCodigo = rv.RepCodigo AND rpv.RepCargo IN ('PreVendedor','Vendedor')
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
WHERE p.PedFecha>=@desde AND p.PedFecha<@hasta
  AND rv.RepCodigo IN ({string.Join(",", repNames)})";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(1) || rd.IsDBNull(2) || rd.IsDBNull(3)) continue;
            var rep = rd.GetString(0); var proId = rd.GetInt32(1); var cat = rd.GetInt32(2); var pedSec = rd.GetInt32(3);
            if (!universoPorRep.TryGetValue(rep, out var universo) || !universo.Contains(proId)) continue;
            var key = (rep, cat);
            if (!acumulado.TryGetValue(key, out var set)) { set = new HashSet<int>(); acumulado[key] = set; }
            set.Add(pedSec);
        }
        return acumulado.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
    }

    public static Dictionary<(string rep, int cat1), int> FacturasPorCategoria(
        SqlConnection cn, DateTime desde, DateTime hastaExclusive, Dictionary<string, HashSet<int>> universoPorRep, IReadOnlyCollection<string> reps)
        => FacturasPorGrupo(cn, desde, hastaExclusive, universoPorRep, reps, true);

    public static Dictionary<(string rep, int cat3), int> FacturasPorSuplidor(
        SqlConnection cn, DateTime desde, DateTime hastaExclusive, Dictionary<string, HashSet<int>> universoPorRep, IReadOnlyCollection<string> reps)
        => FacturasPorGrupo(cn, desde, hastaExclusive, universoPorRep, reps, false);

    // Clientes activos por Rep×Suplidor (mismo criterio que ClientesActivosPorCategoria, pero
    // agrupado por Cat3ID) — pedido del usuario 2026-09-06: "una activación de una categoría
    // impacta directamente en 1 activación de ese suplidor" — se resuelve calculando el conteo
    // A NIVEL SUPLIDOR directo (COUNT DISTINCT sobre TODAS sus categorías de una vez), en vez de
    // intentar sumar/unir los conteos ya agregados por categoría (evita doble conteo de un
    // cliente activo en 2+ categorías del mismo suplidor).
    public static Dictionary<(string rep, int cat3), HashSet<int>> ClientesActivosPorSuplidor(
        SqlConnection cn, DateTime desde, DateTime hastaExclusive,
        Dictionary<string, HashSet<int>> universoPorRep, IReadOnlyCollection<string> reps)
    {
        var resultado = new Dictionary<(string, int), HashSet<int>>();
        if (reps.Count == 0) return resultado;
        var parms = new Dictionary<string, object> { ["@desde"] = desde, ["@hasta"] = hastaExclusive };
        var repNames = reps.Select((r, i) => { var n = $"@rp{i}"; parms[n] = r; return n; }).ToList();
        var sql = $@"
SELECT rv.RepCodigo, d.ProID, pr.Cat3ID, v.CliID
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN clientesDetalle rv WITH (NOLOCK) ON rv.CliID = v.CliID
INNER JOIN Representantes rpv WITH (NOLOCK) ON rpv.RepCodigo = rv.RepCodigo AND rpv.RepCargo IN ('PreVendedor','Vendedor')
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
WHERE v.VenEstatus<>0 AND v.VenFecha>=@desde AND v.VenFecha<@hasta AND d.VenCantidad>0
  AND rv.RepCodigo IN ({string.Join(",", repNames)})";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(1) || rd.IsDBNull(2) || rd.IsDBNull(3)) continue;
            var rep = rd.GetString(0); var proId = rd.GetInt32(1); var cat3 = rd.GetInt32(2); var cli = rd.GetInt32(3);
            if (!universoPorRep.TryGetValue(rep, out var universo) || !universo.Contains(proId)) continue;
            var key = (rep, cat3);
            if (!resultado.TryGetValue(key, out var set)) { set = new HashSet<int>(); resultado[key] = set; }
            set.Add(cli);
        }
        return resultado;
    }

    // ── 4c) Peso real de cada producto dentro de Rep×Categoría ──────────────
    // Pedido explícito del usuario 2026-08-31: el presupuesto real de Agosto/
    // Septiembre a menudo concentra TODO el monto de una Categoría en un solo
    // producto (a veces uno que ni se vende) — este método calcula, para cada
    // Rep×Cat1ID, el % real de venta de CADA producto (excluyendo combos, que
    // "solo suman en unidades") dentro de esa categoría en un rango de
    // referencia — para poder REPARTIR el monto ya asignado según lo que el
    // vendedor realmente comercializa, en vez de dejarlo todo en un SKU.
    // Reusa CalcularBaseHistorica tal cual (ya reatribuye a la ruta vigente y
    // acota al universo de productos de cada rep) — solo agrega Cat1ID +
    // exclusión de combos y la agregación del peso.
    public sealed record PesoProducto(int ProId, double PesoPct, double MontoBase);

    public static Dictionary<(string rep, int cat1), List<PesoProducto>> CalcularPesosReales(
        SqlConnection cn, DateTime desde, DateTime hastaExclusive,
        Dictionary<string, HashSet<int>> universoPorRep, IReadOnlyCollection<string> reps)
    {
        var baseHist = CalcularBaseHistorica(cn, desde, hastaExclusive, universoPorRep, reps);
        var proIds = baseHist.Select(b => b.ProId).Distinct().ToList();
        var cat1PorProducto = new Dictionary<int, int?>();
        var esCombo = new Dictionary<int, bool>();
        if (proIds.Count > 0)
        {
            var parms = new Dictionary<string, object>();
            var names = proIds.Select((p, i) => { var n = $"@p{i}"; parms[n] = p; return n; }).ToList();
            using var cmd = new SqlCommand($"SELECT ProID, Cat1ID, ProDescripcion FROM Productos WITH (NOLOCK) WHERE ProID IN ({string.Join(",", names)})", cn) { CommandTimeout = 60 };
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                int proId = rd.GetInt32(0);
                cat1PorProducto[proId] = rd.IsDBNull(1) ? null : rd.GetInt32(1);
                string desc = rd.IsDBNull(2) ? "" : rd.GetString(2);
                esCombo[proId] = desc.IndexOf("COMBO", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
        var montoPorGrupo = new Dictionary<(string, int), double>();
        var montoPorProducto = new Dictionary<(string rep, int cat1, int proId), double>();
        foreach (var b in baseHist)
        {
            if (!cat1PorProducto.TryGetValue(b.ProId, out var cat1n) || cat1n == null) continue;
            if (esCombo.GetValueOrDefault(b.ProId)) continue; // combos no entran al pool de reparto
            if (b.MontoNeto <= 0) continue;
            var grupoKey = (b.RepCodigo, cat1n.Value);
            var prodKey = (b.RepCodigo, cat1n.Value, b.ProId);
            montoPorGrupo[grupoKey] = montoPorGrupo.GetValueOrDefault(grupoKey) + b.MontoNeto;
            montoPorProducto[prodKey] = montoPorProducto.GetValueOrDefault(prodKey) + b.MontoNeto;
        }
        var resultado = new Dictionary<(string, int), List<PesoProducto>>();
        foreach (var kv in montoPorProducto)
        {
            var (rep, cat1, proId) = kv.Key;
            double totalGrupo = montoPorGrupo[(rep, cat1)];
            if (totalGrupo <= 0) continue;
            var grupoKey = (rep, cat1);
            if (!resultado.TryGetValue(grupoKey, out var lista)) { lista = new List<PesoProducto>(); resultado[grupoKey] = lista; }
            lista.Add(new PesoProducto(proId, kv.Value / totalGrupo, kv.Value));
        }
        return resultado;
    }

    // ── 4d) Precio de Lista REAL por Rep×Producto (canal/lista del vendedor) ─
    // Extraído del cálculo ya verificado en vivo en Comparativo (reproduce
    // exacto PreTipo='MNTDPROD'): RepresentantesAdicionales.LipCodigo → el
    // precio de ESA lista para el producto en ListaPrecios. Reusado por la
    // herramienta de rebalanceo (2026-08-31) para convertir monto reasignado
    // de vuelta a unidades.
    public static Dictionary<(string rep, int proId), double> PrecioListaPorRepYProducto(
        SqlConnection cn, IReadOnlyCollection<string> reps, IReadOnlyCollection<int> proIds)
    {
        var resultado = new Dictionary<(string, int), double>();
        if (reps.Count == 0 || proIds.Count == 0) return resultado;
        var lipPorRep = new Dictionary<string, string>();
        var parmsR = new Dictionary<string, object>();
        var repNames = reps.Select((r, i) => { var n = $"@lr{i}"; parmsR[n] = r; return n; }).ToList();
        using (var cmd = new SqlCommand($"SELECT RepCodigo, ISNULL(LipCodigo,'') FROM RepresentantesAdicionales WITH (NOLOCK) WHERE RepCodigo IN ({string.Join(",", repNames)})", cn) { CommandTimeout = 30 })
        {
            foreach (var kv in parmsR) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) lipPorRep[rd.GetString(0)] = rd.GetString(1);
        }
        var lips = lipPorRep.Values.Where(l => !string.IsNullOrEmpty(l)).Distinct().ToList();
        if (lips.Count == 0) return resultado;
        var precios = new Dictionary<(string lip, int proId), double>();
        var parmsP = new Dictionary<string, object>();
        var lipNames = lips.Select((l, i) => { var n = $"@lp{i}"; parmsP[n] = l; return n; }).ToList();
        var proNames = proIds.Select((p, i) => { var n = $"@pp{i}"; parmsP[n] = p; return n; }).ToList();
        using (var cmd = new SqlCommand($"SELECT LipCodigo, ProID, LipPrecio FROM ListaPrecios WITH (NOLOCK) WHERE LipCodigo IN ({string.Join(",", lipNames)}) AND ProID IN ({string.Join(",", proNames)})", cn) { CommandTimeout = 60 })
        {
            foreach (var kv in parmsP) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) precios[(rd.GetString(0), rd.GetInt32(1))] = Convert.ToDouble(rd.GetValue(2));
        }
        foreach (var rep in reps)
        {
            var lip = lipPorRep.GetValueOrDefault(rep, "");
            if (string.IsNullOrEmpty(lip)) continue;
            foreach (var proId in proIds)
                if (precios.TryGetValue((lip, proId), out var precio))
                    resultado[(rep, proId)] = precio;
        }
        return resultado;
    }

    // ── 5) Anomalías (recomendación por defecto, no bloquean) ───────────────
    // El umbral viene de la variable UMBRAL_ANOMALIA_PCT (Global); si no está
    // configurada, 50% (propuesto entre 0.5x y 2x la base) como default razonable.
    public static (bool anomalia, string? detalle) EvaluarAnomalia(double baseNeta, double propuesto, double umbralPct)
    {
        if (baseNeta <= 0 && propuesto > 0)
            return (true, "Sin ninguna venta en el rango base — presupuesto nuevo desde cero.");
        if (baseNeta > 0)
        {
            double variacion = (propuesto - baseNeta) / baseNeta;
            if (Math.Abs(variacion) * 100.0 > umbralPct)
                return (true, $"Variación de {variacion:+0.0%;-0.0%} vs. la base (umbral configurado: {umbralPct:0.#}%).");
        }
        return (false, null);
    }
}
