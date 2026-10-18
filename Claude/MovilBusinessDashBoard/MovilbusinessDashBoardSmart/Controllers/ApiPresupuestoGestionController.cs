using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DashVisitas.Services;
using ClosedXML.Excel;

namespace DashVisitas.Controllers;

// ── API del módulo "Presupuestos Smart" (creación/edición/eliminación) ─────
// Acceso restringido a Rol='Gerencia' en SupervisionUsuarioMapa_RM — mismo
// mecanismo de identidad/alcance que ApiSupervisionController.cs (no se crea
// una tabla de accesos aparte, pedido explícito del usuario 2026-08-29).
// Flujo: Generar (a staging, sin tocar PresupuestosHistoricos) -> Revisar ->
// Publicar (único paso que escribe a PresupuestosHistoricos).
//
// ARQUITECTURA DE BASES (pedido explícito del usuario, revisado 2026-08-30):
// la conexión de este controller es SIEMPRE contra "Default" = MovilBusiness5STD
// (producción). Ahí viven la data real, la configuración/metodología
// (PresupuestoVariables_RM/Valores_RM) Y las tablas de trabajo de cada
// propuesta (PresupuestoLotes_RM/Staging_RM/Publicaciones_RM) — todas se leen
// y escriben SIN prefijo. SOLO PresupuestosHistoricos (el presupuesto real del
// sistema) se referencia con 3 partes hacia "PresupuestosHistoricosDb"
// (MovilBusiness5STD_Test mientras dure la prueba) vía Destino() — es el único
// INSERT/UPDATE/DELETE que no se quiere hacer directo en producción todavía.
// Cuando el usuario decida consolidar, basta con vaciar esa config y Destino()
// devuelve "" sin tocar el SQL de este archivo.
[ApiController]
[Route("apipresupuestogestion")]
[RequiereModulo("presupuestosGestion")]
public class ApiPresupuestoGestionController(IConfiguration config) : ControllerBase
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));
    private string DestinoDb => config["PresupuestosHistoricosDb"] ?? "";
    private string Destino() => string.IsNullOrEmpty(DestinoDb) ? "dbo." : $"{DestinoDb}.dbo.";
    private bool IsAuth => HttpContext.Session.GetString("user_login") is not null;

    private sealed class Ident { public string User = "", Rol = "Ninguno", Nombre = ""; public bool EsGerencia => Rol == "Gerencia"; }

    // Reusa la MISMA tabla que Supervisión Dinámica — un usuario marcado
    // Gerencia ahí automáticamente puede entrar aquí también.
    private Ident Identidad(SqlConnection cn)
    {
        var id = new Ident
        {
            User = HttpContext.Session.GetString("user_login") ?? "",
            Nombre = HttpContext.Session.GetString("user_nombre") ?? "",
        };
        using var cmd = new SqlCommand(
            "SELECT TOP 1 Rol, Nombre FROM dbo.SupervisionUsuarioMapa_RM WITH (NOLOCK) WHERE UsuInicioSesion=@u AND Activo=1", cn);
        cmd.Parameters.AddWithValue("@u", id.User);
        using var rd = cmd.ExecuteReader();
        if (rd.Read())
        {
            if (!rd.IsDBNull(0)) id.Rol = rd.GetString(0).Trim();
            if (!rd.IsDBNull(1) && !string.IsNullOrWhiteSpace(rd.GetString(1))) id.Nombre = rd.GetString(1).Trim();
        }
        return id;
    }

    // ── GET /apipresupuestogestion/identidad ────────────────────────────────
    [HttpGet("identidad")]
    public IActionResult GetIdentidad()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        using var cn = NewConn(); cn.Open();
        var id = Identidad(cn);
        return Ok(new { ok = true, rol = id.Rol, esGerencia = id.EsGerencia, nombre = id.Nombre, login = id.User });
    }

    private IActionResult? RequireGerencia(SqlConnection cn)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (!Identidad(cn).EsGerencia) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede usar este módulo." });
        return null;
    }

    public sealed record DestinoMes(int anio, int mes);
    // ObjetivoMes (2026-09-06, pedido explícito del usuario): objetivo POR MES,
    // alternativa/complemento a montoTotalSugerido/montoValorSugerido (el "Total").
    // 'modo': "valor" (RD$) o "unidades" — misma convención que el Total.
    public sealed record ObjetivoMes(int anio, int mes, double valor, string modo);
    public sealed record GenerarReq(
        DateTime baseFechaInicio, DateTime baseFechaFin, List<DestinoMes> destino,
        string preTipo, List<string>? reps, string? descripcion,
        // compAnio/compMes (opcionales, 2026-08-30): mes específico contra el que se
        // quiere comparar producto-por-producto en "2. Revisar" (columna "Actual").
        // Sin esto, se mantiene el comportamiento anterior (lo ya publicado en el
        // MISMO período destino, casi siempre 0 para meses futuros).
        int? compAnio, int? compMes,
        // montoTotalSugerido (opcional, 2026-08-30): si se da, TODA la propuesta se
        // reescala (mismas proporciones relativas por rep/producto que ya calculó el
        // motor con base+factores) para que la SUMA total dé exactamente este número
        // (en UNIDADES). montoValorSugerido (RD$) es la misma idea pero en Valor —
        // mutuamente excluyentes (si vienen los dos, gana montoValorSugerido). Ambos
        // son el "Total" — solo aplican a los meses que NO tengan su propio
        // 'objetivosPorMes' (ver abajo).
        double? montoTotalSugerido, double? montoValorSugerido,
        // objetivosPorMes (2026-09-06, pedido explícito del usuario: "Objetivo por
        // Mes y General... es como una combinación entre" fijar cada mes a mano Y
        // repartir un Total entre el resto): meses con entrada aquí se reescalan
        // EXACTO a su propio valor; los meses SIN entrada aquí (si además hay un
        // Total puesto) se reparten proporcional entre ELLOS con lo que quede del
        // Total menos lo ya fijado. Ver PASO 2.
        List<ObjetivoMes>? objetivosPorMes,
        // canales (opcional, 2026-08-30): CanID a incluir. Sin esto, default = todos
        // MENOS Institucional (7000) — misma convención ya usada en Presupuestos/
        // Historicos (ver PresupuestoBuilderCore.FiltrarRepsPorCanal).
        List<int>? canales,
        // supervisores/suplidores/categorias/productos (2026-09-06, pedido explícito
        // del usuario — pantalla nueva "Editar Presupuestos"): filtros ADICIONALES
        // opcionales para reusar este MISMO endpoint acotado a un bloque específico
        // en vez de generar TODO el universo. 'reps' (Vendedor) y 'canales' (Canal)
        // ya cubrían 2 de las 6 dimensiones pedidas — estas 4 completan el resto.
        // suplidores=Cat3ID, categorias=Cat1ID, productos=ProID (todos como string/
        // int según el tipo nativo de cada catálogo en este proyecto).
        List<string>? supervisores, List<string>? suplidores, List<string>? categorias, List<int>? productos);

    // ── POST /apipresupuestogestion/generar ─────────────────────────────────
    // Genera una propuesta a STAGING (PresupuestoLotes_RM + PresupuestoStaging_RM,
    // en producción). No toca PresupuestosHistoricos. v1: solo PreTipo='Unddprod'
    // (el origen del que dependen los demás cálculos, pedido explícito del
    // usuario). El caso 'UNDDLINACT' (Activaciones -> reparto proporcional de
    // unidades entre los SKU de la categoría) queda para la siguiente fase.
    [HttpPost("generar")]
    public IActionResult Generar([FromBody] GenerarReq req)
    {
        Console.WriteLine($"[Generar] >>> INICIO {DateTime.Now:HH:mm:ss.fff} - req={(req == null ? "NULL" : $"preTipo={req.preTipo}, destino={req.destino?.Count}, baseIni={req.baseFechaInicio}, baseFin={req.baseFechaFin}, reps={string.Join(',', req.reps ?? new())}")}");
        using var cn0 = NewConn(); cn0.Open();
        Console.WriteLine($"[Generar] cn0 abierta {DateTime.Now:HH:mm:ss.fff}");
        var deny = RequireGerencia(cn0); if (deny != null) { Console.WriteLine("[Generar] RequireGerencia denegó"); return deny; }
        Console.WriteLine($"[Generar] Gerencia OK {DateTime.Now:HH:mm:ss.fff}");
        if (req is null) return BadRequest(new { ok = false, error = "Cuerpo de la solicitud vacío o mal formado." });

        if (!string.Equals(req.preTipo, "Unddprod", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { ok = false, error = "v1 solo genera propuestas para PreTipo='Unddprod'." });
        if (req.destino is null || req.destino.Count == 0)
            return BadRequest(new { ok = false, error = "Falta el rango destino (Año/Mes)." });
        if (req.baseFechaFin < req.baseFechaInicio)
            return BadRequest(new { ok = false, error = "El rango base es inválido (fin < inicio)." });

        try
        {
            using var cn = NewConn(); cn.Open();
            Console.WriteLine($"[Generar] cn (2da conexion) abierta {DateTime.Now:HH:mm:ss.fff}");
            var user = HttpContext.Session.GetString("user_login") ?? "desconocido";

            // Sin filtro explícito de reps: no correr para TODO el catálogo (~209
            // reps) — solo los que de verdad tuvieron venta en el rango base o
            // tienen clientes en RutaVisitas (pedido explícito del usuario
            // 2026-08-30, para no gastar tiempo evaluando reps inactivos/sin uso).
            List<string>? repsEfectivo = req.reps;
            if (repsEfectivo is null || repsEfectivo.Count == 0)
            {
                Console.WriteLine($"[Generar] llamando ResolverRepsActivos {DateTime.Now:HH:mm:ss.fff}");
                var activos = PresupuestoBuilderCore.ResolverRepsActivos(cn, req.baseFechaInicio, req.baseFechaFin.AddDays(1));
                repsEfectivo = activos.ToList();
                Console.WriteLine($"[Generar] ResolverRepsActivos devolvio {repsEfectivo.Count} reps {DateTime.Now:HH:mm:ss.fff}");
            }

            // Filtro de Canal (pedido explícito del usuario 2026-08-30): por defecto,
            // fuera Institucional (CanID=7000) — igual criterio que Presupuestos/
            // Historicos. Se aplica ANTES de calcular universo/base histórica para que
            // ningún rep de ese canal entre siquiera al cálculo (no solo se le oculte
            // en pantalla).
            Console.WriteLine($"[Generar] llamando FiltrarRepsPorCanal (canales={string.Join(',', req.canales ?? new())}) {DateTime.Now:HH:mm:ss.fff}");
            repsEfectivo = PresupuestoBuilderCore.FiltrarRepsPorCanal(cn, repsEfectivo, req.canales);
            Console.WriteLine($"[Generar] FiltrarRepsPorCanal dejo {repsEfectivo.Count} reps {DateTime.Now:HH:mm:ss.fff}");
            if (repsEfectivo.Count == 0)
                return BadRequest(new { ok = false, error = "Ningún rep quedó dentro del filtro de canal indicado." });

            // Filtro de Supervisor (2026-09-06, pantalla "Editar Presupuestos" — pedido
            // explícito del usuario: poder acotar por Supervisor además de Canal/Vendedor).
            if (req.supervisores is { Count: > 0 })
            {
                repsEfectivo = PresupuestoBuilderCore.FiltrarRepsPorSupervisor(cn, repsEfectivo, req.supervisores);
                Console.WriteLine($"[Generar] FiltrarRepsPorSupervisor dejo {repsEfectivo.Count} reps {DateTime.Now:HH:mm:ss.fff}");
                if (repsEfectivo.Count == 0)
                    return BadRequest(new { ok = false, error = "Ningún rep quedó dentro del filtro de supervisor indicado." });
            }

            Console.WriteLine($"[Generar] llamando UniversoProductosPorRep {DateTime.Now:HH:mm:ss.fff}");
            var universo = PresupuestoBuilderCore.UniversoProductosPorRep(cn, repsEfectivo);
            Console.WriteLine($"[Generar] UniversoProductosPorRep devolvio {universo.Count} reps {DateTime.Now:HH:mm:ss.fff}");
            if (universo.Count == 0)
                return BadRequest(new { ok = false, error = "Ninguno de los reps pedidos tiene grupos de producto asignados (RepresentantesGrupoProductos)." });
            // Intersección final: solo reps que están activos Y tienen grupo de producto.
            repsEfectivo = repsEfectivo.Where(universo.ContainsKey).ToList();

            Console.WriteLine($"[Generar] llamando CalcularBaseHistorica con {repsEfectivo.Count} reps {DateTime.Now:HH:mm:ss.fff}");
            var baseHist = PresupuestoBuilderCore.CalcularBaseHistorica(
                cn, req.baseFechaInicio, req.baseFechaFin.AddDays(1), universo, repsEfectivo);
            Console.WriteLine($"[Generar] CalcularBaseHistorica devolvio {baseHist.Count} filas {DateTime.Now:HH:mm:ss.fff}");

            // Meses del rango base, para promediar (la base histórica es una SUMA
            // del rango completo; el presupuesto es mensual, así que se divide
            // entre la cantidad de meses calendario que cubre el rango base).
            int mesesBase = ((req.baseFechaFin.Year - req.baseFechaInicio.Year) * 12
                + req.baseFechaFin.Month - req.baseFechaInicio.Month) + 1;
            if (mesesBase < 1) mesesBase = 1;

            // Días hábiles del rango base (para repartir el propuesto entre los meses
            // destino PROPORCIONAL a cuántos días de venta tiene cada uno — pedido
            // explícito del usuario 2026-08-30: "si fue lineal deberías tomar en
            // cuenta los días hábiles"). 'baseNetaMensual' (promedio simple) se
            // conserva tal cual para mostrar "Ventas Promedio"/anomalías — el ajuste
            // por días solo cambia CÓMO se reparte el propuesto entre meses destino.
            int diasHabilesBase = PresupuestoBuilderCore.DiasHabilesEnRango(req.baseFechaInicio, req.baseFechaFin.AddDays(1));
            if (diasHabilesBase < 1) diasHabilesBase = 1;

            // Productos -> Cat3ID/Cat1ID (para resolver variables por Suplidor y, más
            // abajo, la meta de activación por Suplidor×Categoría).
            var proIds = baseHist.Select(b => b.ProId).Distinct().ToList();
            var cat3PorProducto = new Dictionary<int, string?>();
            var cat1PorProducto = new Dictionary<int, int?>();
            if (proIds.Count > 0)
            {
                var parms = new Dictionary<string, object>();
                var names = proIds.Select((p, i) => { var n = $"@p{i}"; parms[n] = p; return n; }).ToList();
                using var cmdP = new SqlCommand($"SELECT ProID, Cat3ID, Cat1ID FROM Productos WITH (NOLOCK) WHERE ProID IN ({string.Join(",", names)})", cn) { CommandTimeout = 60 };
                foreach (var kv in parms) cmdP.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rdP = cmdP.ExecuteReader();
                while (rdP.Read())
                {
                    var proId = rdP.GetInt32(0);
                    cat3PorProducto[proId] = rdP.IsDBNull(1) ? null : rdP.GetInt32(1).ToString();
                    cat1PorProducto[proId] = rdP.IsDBNull(2) ? null : rdP.GetInt32(2);
                }
            }

            // Filtros adicionales de "Editar Presupuestos" (2026-09-06, pedido explícito
            // del usuario): acotar la base histórica a un bloque específico por Suplidor/
            // Categoría/Producto. El resto del motor (factores/rapel/activación/
            // efectividad/límites/umbral/reescalado) ya respeta el scope automáticamente
            // porque TODO opera sobre 'baseHist'/'calculadas' ya filtrados aquí.
            if (req.suplidores is { Count: > 0 } || req.categorias is { Count: > 0 } || req.productos is { Count: > 0 })
            {
                var suplidoresSet = req.suplidores is { Count: > 0 } ? new HashSet<string>(req.suplidores) : null;
                var categoriasSet = req.categorias is { Count: > 0 } ? new HashSet<string>(req.categorias) : null;
                var productosSet = req.productos is { Count: > 0 } ? new HashSet<int>(req.productos) : null;
                baseHist = baseHist.Where(b =>
                    (suplidoresSet == null || (cat3PorProducto.GetValueOrDefault(b.ProId) is string cat3s && suplidoresSet.Contains(cat3s))) &&
                    (categoriasSet == null || (cat1PorProducto.GetValueOrDefault(b.ProId) is int cat1n && categoriasSet.Contains(cat1n.ToString()))) &&
                    (productosSet == null || productosSet.Contains(b.ProId))
                ).ToList();
                Console.WriteLine($"[Generar] filtro Editar (Suplidor/Categoría/Producto) dejó {baseHist.Count} filas {DateTime.Now:HH:mm:ss.fff}");
                if (baseHist.Count == 0)
                    return BadRequest(new { ok = false, error = "Ningún producto quedó dentro del filtro de Suplidor/Categoría/Producto indicado." });
            }

            // Precarga TODAS las variables relevantes de una sola vez (1 query por
            // variable por año distinto en el destino, no por fila) — ver nota en
            // PresupuestoBuilderCore sobre el bug de rendimiento que esto corrige.
            var aniosDistintos = req.destino.Select(d => d.anio).Distinct().ToList();
            var cacheFactor = new Dictionary<int, List<PresupuestoBuilderCore.VariableRegla>>();
            var cacheFactorRuta = new Dictionary<int, List<PresupuestoBuilderCore.VariableRegla>>();
            var cachePiso = new Dictionary<int, List<PresupuestoBuilderCore.VariableRegla>>();
            var cacheUmbral = new Dictionary<int, List<PresupuestoBuilderCore.VariableRegla>>();
            var cacheRappel = new Dictionary<int, List<PresupuestoBuilderCore.VariableRegla>>();
            foreach (var anio in aniosDistintos)
            {
                cacheFactor[anio] = PresupuestoBuilderCore.CargarVariable(cn, "FACTOR_CRECIMIENTO_MES", anio);
                cacheFactorRuta[anio] = PresupuestoBuilderCore.CargarVariable(cn, "FACTOR_RUTA_PROVEEDOR", anio);
                cachePiso[anio] = PresupuestoBuilderCore.CargarVariable(cn, "PISO_MINIMO_PCT", anio);
                cacheUmbral[anio] = PresupuestoBuilderCore.CargarVariable(cn, "UMBRAL_ANOMALIA_PCT", anio);
                cacheRappel[anio] = PresupuestoBuilderCore.CargarVariable(cn, "META_RAPPEL", anio);
            }
            double umbralAnomalia = PresupuestoBuilderCore.ResolverVariableEnMemoria(cacheUmbral[req.destino[0].anio], req.destino[0].mes, null, null, null, 50.0);
            Console.WriteLine($"[Generar] variables cargadas {DateTime.Now:HH:mm:ss.fff}");

            // META_RAPPEL (2026-08-30 — antes esta variable existía en el catálogo pero
            // el motor nunca la usaba): cuando hay una meta de rappel configurada para un
            // Suplidor/mes, ESA meta ÷ el valor promedio mensual (RD$) que hoy vende ese
            // Suplidor REEMPLAZA (no multiplica) el FACTOR_CRECIMIENTO_MES normal para las
            // filas de ese Suplidor en ese mes — es una meta comercial más específica que
            // un factor de crecimiento genérico. factorRuta y piso se siguen aplicando igual.
            var baseValorPorCat3 = new Dictionary<string, double>();
            foreach (var fila in baseHist)
            {
                var cat3 = cat3PorProducto.GetValueOrDefault(fila.ProId);
                if (cat3 == null) continue;
                baseValorPorCat3[cat3] = baseValorPorCat3.GetValueOrDefault(cat3) + fila.MontoNeto;
            }
            foreach (var k in baseValorPorCat3.Keys.ToList()) baseValorPorCat3[k] /= mesesBase;

            // "Actual" por cada mes destino — calculado ANTES de abrir la transacción
            // de abajo (una vez que 'cn' entra en transacción, todo SqlCommand sobre
            // esa misma conexión debe asociarse a ella o ADO.NET lanza excepción).
            // Con compAnio/compMes (pedido 2026-08-30): se usa el PRESUPUESTO de ESE
            // mes específico en PRODUCCIÓN (histórico real, ya cerrado) como "Actual"
            // para TODOS los meses destino — así "2. Revisar" compara producto a
            // producto contra el mes que Gerencia eligió, no siempre contra el mismo
            // período (que para meses futuros casi siempre da 0). Sin esto, se
            // mantiene el comportamiento anterior (lo ya publicado en el MISMO
            // período, en la base DESTINO de prueba).
            Console.WriteLine($"[Generar] llamando Actual x{req.destino.Count} meses, compAnio={req.compAnio}, compMes={req.compMes} {DateTime.Now:HH:mm:ss.fff}");
            Dictionary<(string, string), double> actualUnico;
            if (req.compAnio is > 0 && req.compMes is >= 1 and <= 12)
                actualUnico = PresupuestoBuilderCore.ObtenerPresupuestoActual(cn, "", req.preTipo, req.compAnio.Value, req.compMes.Value, repsEfectivo);
            else
                actualUnico = new();
            var actualPorDestino = req.destino.ToDictionary(
                d => (d.anio, d.mes),
                d => (req.compAnio is > 0 && req.compMes is >= 1 and <= 12)
                    ? actualUnico
                    : PresupuestoBuilderCore.ObtenerPresupuestoActual(cn, DestinoDb, req.preTipo, d.anio, d.mes, repsEfectivo));
            Console.WriteLine($"[Generar] Actual OK {DateTime.Now:HH:mm:ss.fff}");

            // PASO 1: calcular el propuesto "natural" (base x factores x piso) de cada
            // fila, sin insertar nada todavía — hace falta el TOTAL antes de saber si
            // hay que reescalar por 'montoTotalSugerido'. Puro cálculo en memoria, no
            // necesita transacción — se hace ANTES de abrirla.
            // Modelo Rappel generalizado (2026-09-06, pedido explícito del usuario) — además de
            // META_RAPPEL (Monto por Suplidor, catálogo genérico, ya aplicado más abajo),
            // PresupuestoRappelMetas_RM agrega Monto por Categoría + Activaciones (conteo de
            // clientes activos) + Factura (conteo de facturas/pedidos distintos), cada uno por
            // Suplidor O Categoría, pudiendo coexistir varios — cada uno actúa como PISO
            // independiente (mismo mecanismo Math.Max que ya usa Monto). Las consultas caras
            // (clientes activos/facturas) solo se disparan si de verdad hay metas de ese tipo.
            var rappelMetas = PresupuestoBuilderCore.CargarRappelMetas(cn, req.destino.Select(d => d.anio).Min());
            bool hayRappelMonto = rappelMetas.Any(m => m.TipoRequerimiento == "Monto");
            bool hayRappelActivaciones = rappelMetas.Any(m => m.TipoRequerimiento == "Activaciones");
            bool hayRappelFactura = rappelMetas.Any(m => m.TipoRequerimiento == "Factura");

            var baseValorPorCat1 = new Dictionary<int, double>();
            if (hayRappelMonto)
            {
                foreach (var fila in baseHist)
                {
                    var cat1n = cat1PorProducto.GetValueOrDefault(fila.ProId);
                    if (cat1n == null) continue;
                    baseValorPorCat1[cat1n.Value] = baseValorPorCat1.GetValueOrDefault(cat1n.Value) + fila.MontoNeto;
                }
                foreach (var k in baseValorPorCat1.Keys.ToList()) baseValorPorCat1[k] /= mesesBase;
            }

            var clientesActivosPorCat1Rappel = new Dictionary<(string, int), HashSet<int>>();
            var clientesActivosPorCat3Rappel = new Dictionary<(string, int), HashSet<int>>();
            if (hayRappelActivaciones)
            {
                clientesActivosPorCat1Rappel = PresupuestoBuilderCore.ClientesActivosPorCategoria(cn, req.baseFechaInicio, req.baseFechaFin.AddDays(1), universo, repsEfectivo);
                clientesActivosPorCat3Rappel = PresupuestoBuilderCore.ClientesActivosPorSuplidor(cn, req.baseFechaInicio, req.baseFechaFin.AddDays(1), universo, repsEfectivo);
            }
            var facturasPorCat1Rappel = new Dictionary<(string, int), int>();
            var facturasPorCat3Rappel = new Dictionary<(string, int), int>();
            if (hayRappelFactura)
            {
                facturasPorCat1Rappel = PresupuestoBuilderCore.FacturasPorCategoria(cn, req.baseFechaInicio, req.baseFechaFin.AddDays(1), universo, repsEfectivo);
                facturasPorCat3Rappel = PresupuestoBuilderCore.FacturasPorSuplidor(cn, req.baseFechaInicio, req.baseFechaFin.AddDays(1), universo, repsEfectivo);
            }
            Console.WriteLine($"[Generar] Rappel generalizado: {rappelMetas.Count} metas ({(hayRappelMonto ? "Monto " : "")}{(hayRappelActivaciones ? "Activaciones " : "")}{(hayRappelFactura ? "Factura" : "")}) {DateTime.Now:HH:mm:ss.fff}");

            // Combina TODOS los requerimientos de Rappel aplicables a esta fila (Monto/
            // Activaciones/Factura × Suplidor/Categoría) en un solo factor — el mayor de todos
            // (cada uno es un piso independiente, nunca bajan el propuesto).
            double FactorRappelGeneralizado(string rep, int mesFila, int? cat3, int? cat1)
            {
                double mejor = 1.0;
                if (cat3 != null)
                {
                    var m = PresupuestoBuilderCore.ResolverRappelMeta(rappelMetas, "Monto", mesFila, cat3, null);
                    if (m != null && baseValorPorCat3.TryGetValue(cat3.Value.ToString(), out var baseCat3) && baseCat3 > 0)
                        mejor = Math.Max(mejor, m.MetaValor / baseCat3);
                }
                if (cat1 != null)
                {
                    var m = PresupuestoBuilderCore.ResolverRappelMeta(rappelMetas, "Monto", mesFila, null, cat1);
                    if (m != null && baseValorPorCat1.TryGetValue(cat1.Value, out var baseCat1) && baseCat1 > 0)
                        mejor = Math.Max(mejor, m.MetaValor / baseCat1);
                }
                if (cat3 != null)
                {
                    var m = PresupuestoBuilderCore.ResolverRappelMeta(rappelMetas, "Activaciones", mesFila, cat3, null);
                    if (m != null && clientesActivosPorCat3Rappel.TryGetValue((rep, cat3.Value), out var setSup) && setSup.Count > 0)
                        mejor = Math.Max(mejor, m.MetaValor / setSup.Count);
                }
                if (cat1 != null)
                {
                    var m = PresupuestoBuilderCore.ResolverRappelMeta(rappelMetas, "Activaciones", mesFila, null, cat1);
                    if (m != null && clientesActivosPorCat1Rappel.TryGetValue((rep, cat1.Value), out var setCat) && setCat.Count > 0)
                        mejor = Math.Max(mejor, m.MetaValor / setCat.Count);
                }
                if (cat3 != null)
                {
                    var m = PresupuestoBuilderCore.ResolverRappelMeta(rappelMetas, "Factura", mesFila, cat3, null);
                    if (m != null && facturasPorCat3Rappel.TryGetValue((rep, cat3.Value), out var nFacSup) && nFacSup > 0)
                        mejor = Math.Max(mejor, m.MetaValor / nFacSup);
                }
                if (cat1 != null)
                {
                    var m = PresupuestoBuilderCore.ResolverRappelMeta(rappelMetas, "Factura", mesFila, null, cat1);
                    if (m != null && facturasPorCat1Rappel.TryGetValue((rep, cat1.Value), out var nFacCat) && nFacCat > 0)
                        mejor = Math.Max(mejor, m.MetaValor / nFacCat);
                }
                return mejor;
            }

            var calculadas = new List<(DestinoMes destino, string rep, int proId, double baseNetaMensual, double propuestoNatural, double valorActual)>();
            foreach (var destino in req.destino)
            {
                var actual = actualPorDestino[(destino.anio, destino.mes)];
                var fFactor = cacheFactor[destino.anio]; var fFactorRuta = cacheFactorRuta[destino.anio]; var fPiso = cachePiso[destino.anio];
                var fRappel = cacheRappel[destino.anio];

                int diasHabilesDestino = PresupuestosCore.DiasHabiles(destino.anio, destino.mes, DateTime.DaysInMonth(destino.anio, destino.mes));
                foreach (var fila in baseHist)
                {
                    double baseNetaMensual = fila.UnidadesNetas / mesesBase;
                    double propuestoBaseDiario = (fila.UnidadesNetas / diasHabilesBase) * diasHabilesDestino;
                    string? cat3 = cat3PorProducto.GetValueOrDefault(fila.ProId);
                    int? cat1 = cat1PorProducto.GetValueOrDefault(fila.ProId);
                    int? cat3Int = (cat3 != null && int.TryParse(cat3, out var cat3Parsed)) ? cat3Parsed : (int?)null;
                    double factor = PresupuestoBuilderCore.ResolverVariableEnMemoria(fFactor, destino.mes, fila.RepCodigo, cat3, null);
                    double factorRuta = PresupuestoBuilderCore.ResolverVariableEnMemoria(fFactorRuta, destino.mes, fila.RepCodigo, cat3, null);
                    double piso = PresupuestoBuilderCore.ResolverVariableEnMemoria(fPiso, destino.mes, fila.RepCodigo, cat3, null, 0);

                    if (cat3 != null)
                    {
                        // Rapel = PISO, no reemplazo (pedido explícito del usuario 2026-09-06:
                        // "esta condición debe ser la Mínima, cual si fuera una Base") — el rapel
                        // nunca hace bajar el propuesto por debajo de lo que ya daba el crecimiento
                        // normal; solo empuja hacia arriba si exige más que el crecimiento.
                        double metaRappel = PresupuestoBuilderCore.ResolverVariableEnMemoria(fRappel, destino.mes, fila.RepCodigo, cat3, null, double.NaN);
                        if (!double.IsNaN(metaRappel) && baseValorPorCat3.TryGetValue(cat3, out var baseValorCat3) && baseValorCat3 > 0)
                            factor = Math.Max(factor, metaRappel / baseValorCat3);
                    }
                    if (rappelMetas.Count > 0)
                        factor = Math.Max(factor, FactorRappelGeneralizado(fila.RepCodigo, destino.mes, cat3Int, cat1));

                    double propuesto = propuestoBaseDiario * factor * factorRuta;
                    double minimo = baseNetaMensual * (piso / 100.0);
                    if (propuesto < minimo) propuesto = minimo;
                    if (propuesto <= 0) continue;

                    actual.TryGetValue((fila.RepCodigo, fila.ProId.ToString()), out var valorActual);
                    calculadas.Add((destino, fila.RepCodigo, fila.ProId, baseNetaMensual, propuesto, valorActual));
                }
            }
            Console.WriteLine($"[Generar] {calculadas.Count} filas calculadas (natural, sin escalar) {DateTime.Now:HH:mm:ss.fff}");

            // PASO 1b: Activación mínima OBLIGATORIA (pedido explícito del usuario
            // 2026-08-30) — para cada Suplidor×Categoría con meta 'Obligatorio', si el %
            // de clientes activos del rep queda por debajo de la meta, se sube el
            // propuesto de ESE rep×categoría lo necesario (convertido a unidades vía el
            // promedio de unidades/cliente activo de ese mismo grupo) y se reparte entre
            // sus SKU según el peso que YA tenían — mismo mecanismo de reparto proporcional
            // usado en el resto del motor. 'Ideal' nunca llega aquí (se filtra abajo);
            // se deja solo como referencia informativa en el drill (Comparativo).
            // Dimensiones (Canal/División) por rep — 2026-09-01, necesarias para
            // resolver la cascada de ActivacionMeta (RepTipe > CanId > default) Y
            // la meta de Efectividad (por RepTipe) de más abajo — se carga una
            // sola vez y se comparte entre ambos hooks.
            var dimsRep = PresupuestoBuilderCore.CargarDimensionesRep(cn, repsEfectivo);

            var metasActivacion = PresupuestoBuilderCore.CargarActivacionMetas(cn).Where(m => m.Modo == "Obligatorio").ToList();
            bool bloqueaPublicar = false;
            var notasActivacion = new List<string>();
            // Categorías (rep,cat1) que TIENEN una meta de Activación Obligatoria configurada
            // (la alcancen ya o la alcancen tras forzar más abajo) — pedido explícito del usuario
            // 2026-09-06 para que el hook de Efectividad (PASO 1b-2) sepa qué categorías están
            // "cubiertas" por Activación y no les redirija también su propio incremento.
            var categoriasConMetaObligatoria = new HashSet<(string rep, int cat1)>();
            // Subconjunto de las anteriores que Activación efectivamente dejó cubiertas (ya
            // cumplían, o recibieron su gap completo) — si el tope de 5% recortó el gap de
            // una categoría y no llegó a su meta, se EXCLUYE de aquí para que Efectividad
            // (más abajo) todavía la considere disponible.
            var categoriasCubiertasActivacion = new HashSet<(string rep, int cat1)>();
            if (metasActivacion.Count > 0)
            {
                Console.WriteLine($"[Generar] {metasActivacion.Count} metas de activación OBLIGATORIAS configuradas — calculando clientes activos {DateTime.Now:HH:mm:ss.fff}");
                var clientesActivos = PresupuestoBuilderCore.ClientesActivosPorCategoria(cn, req.baseFechaInicio, req.baseFechaFin.AddDays(1), universo, repsEfectivo);
                var cartera = PresupuestoBuilderCore.CarteraPorRep(cn, repsEfectivo);

                var gruposList = calculadas
                    .Select((c, idx) => (c, idx))
                    .Where(t => cat1PorProducto.GetValueOrDefault(t.c.proId) != null)
                    .GroupBy(t => (t.c.destino, t.c.rep, cat1: cat1PorProducto[t.c.proId]!.Value))
                    .ToList();

                // PASADA 1 (dry-run): calcular el gap de cada grupo SIN aplicarlo todavía —
                // hace falta el total pedido por TODOS los grupos para poder acotarlo al tope
                // global (pedido explícito del usuario 2026-09-06: "la incidencia de este
                // crecimiento no incida en más del 5% del presupuesto total").
                var gapPorGrupo = new double[gruposList.Count];
                var totalUnidadesPorGrupo = new double[gruposList.Count];
                var metaPorGrupo = new PresupuestoBuilderCore.ActivacionMeta?[gruposList.Count];
                var clientesFaltantesPorGrupo = new int[gruposList.Count];
                for (int gi = 0; gi < gruposList.Count; gi++)
                {
                    var grupo = gruposList[gi];
                    var (destino, rep, cat1) = grupo.Key;
                    var cat3Str = cat3PorProducto.GetValueOrDefault(grupo.First().c.proId);
                    if (cat3Str == null || !int.TryParse(cat3Str, out var cat3)) continue;
                    var (dimCanId, dimRepTipe) = dimsRep.GetValueOrDefault(rep, (0, ""));
                    var meta = PresupuestoBuilderCore.ResolverActivacionMeta(metasActivacion, cat3, cat1, dimCanId, string.IsNullOrEmpty(dimRepTipe) ? null : dimRepTipe, rep);
                    if (meta == null) continue;
                    categoriasConMetaObligatoria.Add((rep, cat1));
                    int carteraRep = cartera.GetValueOrDefault(rep, 0);
                    if (carteraRep <= 0) continue;
                    int activos = clientesActivos.GetValueOrDefault((rep, cat1))?.Count ?? 0;
                    double actualPct = (double)activos / carteraRep;
                    if (actualPct >= meta.MetaPct) continue; // ya cumple, no se fuerza nada

                    int clientesFaltantes = (int)Math.Ceiling((meta.MetaPct - actualPct) * carteraRep);
                    double totalUnidadesGrupo = grupo.Sum(t => t.c.propuestoNatural);
                    if (totalUnidadesGrupo <= 0 || clientesFaltantes <= 0)
                    {
                        if (meta.BloqueaPublicar) bloqueaPublicar = true;
                        continue;
                    }
                    double unidadesPorClienteActivo = activos > 0 ? totalUnidadesGrupo / activos : totalUnidadesGrupo / grupo.Count();
                    gapPorGrupo[gi] = clientesFaltantes * unidadesPorClienteActivo;
                    totalUnidadesPorGrupo[gi] = totalUnidadesGrupo;
                    metaPorGrupo[gi] = meta;
                    clientesFaltantesPorGrupo[gi] = clientesFaltantes;
                }

                // Tope global (configurable — TOPE_ACTIVACION_PCT, Global, default 5% si no está
                // configurada) sobre el presupuesto TOTAL del lote en RD$ (no en unidades — sumar
                // unidades de productos distintos no es comparable). Reusa el mismo precio de
                // Lista real que ya usa el hook de Umbral RD$ (PrecioListaPorRepYProducto).
                var proIdsLote = calculadas.Select(c => c.proId).Distinct().ToList();
                var precioListaTope = PresupuestoBuilderCore.PrecioListaPorRepYProducto(cn, repsEfectivo, proIdsLote);
                double Monto((DestinoMes destino, string rep, int proId, double baseNetaMensual, double propuestoNatural, double valorActual) c)
                    => c.propuestoNatural * precioListaTope.GetValueOrDefault((c.rep, c.proId), 0);
                double totalLotePropuestoRD = calculadas.Sum(Monto);
                var reglasTope = PresupuestoBuilderCore.CargarVariable(cn, "TOPE_ACTIVACION_PCT", req.destino.Select(d => d.anio).Min());
                double topePct = PresupuestoBuilderCore.ResolverVariableEnMemoria(reglasTope, req.destino.First().mes, null, null, null, 5.0);
                double totalGapRD = 0;
                for (int gi = 0; gi < gruposList.Count; gi++)
                {
                    if (gapPorGrupo[gi] <= 0) continue;
                    foreach (var (c, _) in gruposList[gi])
                        totalGapRD += (gapPorGrupo[gi] * (c.propuestoNatural / totalUnidadesPorGrupo[gi])) * precioListaTope.GetValueOrDefault((c.rep, c.proId), 0);
                }
                double topeRD = totalLotePropuestoRD * (topePct / 100.0);
                double escalaTope = (totalGapRD > topeRD && totalGapRD > 0) ? topeRD / totalGapRD : 1.0;
                if (escalaTope < 1.0)
                    notasActivacion.Add($"Tope de Activación aplicado: se pidió +RD${totalGapRD:0,0.##} entre todas las categorías, recortado a +RD${topeRD:0,0.##} ({topePct:0.#}% del presupuesto del lote).");

                foreach (var (rep, cat1) in categoriasConMetaObligatoria) categoriasCubiertasActivacion.Add((rep, cat1));
                for (int gi = 0; gi < gruposList.Count; gi++)
                    if (gapPorGrupo[gi] > 0 && escalaTope < 1.0)
                        categoriasCubiertasActivacion.Remove((gruposList[gi].Key.rep, gruposList[gi].Key.cat1));

                // PASADA 2: aplicar cada gap ya escalado por 'escalaTope'.
                for (int gi = 0; gi < gruposList.Count; gi++)
                {
                    if (gapPorGrupo[gi] <= 0) continue;
                    var grupo = gruposList[gi];
                    var (destino, rep, cat1) = grupo.Key;
                    double gapUnidades = gapPorGrupo[gi] * escalaTope;
                    double totalUnidadesGrupo = totalUnidadesPorGrupo[gi];
                    foreach (var (c, idx) in grupo)
                    {
                        double peso = c.propuestoNatural / totalUnidadesGrupo;
                        calculadas[idx] = (c.destino, c.rep, c.proId, c.baseNetaMensual, c.propuestoNatural + gapUnidades * peso, c.valorActual);
                    }
                    var meta = metaPorGrupo[gi]!;
                    if (escalaTope < 1.0 && meta.BloqueaPublicar) bloqueaPublicar = true;
                    notasActivacion.Add($"Activación forzada {rep}/Cat1={cat1}/{destino.anio}-{destino.mes:00}: +{clientesFaltantesPorGrupo[gi]} clientes (+{gapUnidades:0.##} u.){(escalaTope < 1.0 ? " [recortado por tope]" : "")}");
                }
                if (notasActivacion.Count > 0) Console.WriteLine($"[Generar] activación aplicada, tope={topePct:0.#}% (escala={escalaTope:0.####}) {DateTime.Now:HH:mm:ss.fff}");
            }

            // PASO 1b-2: Efectividad mínima OBLIGATORIA por División (pedido
            // explícito del usuario 2026-09-01, Fase B del rediseño de premisas) —
            // mismo mecanismo que Activación, pero a nivel TOTAL del rep (no por
            // Suplidor×Categoría, porque Efectividad = ClientesConPedido/Visitados
            // es un concepto de visita, no de producto). Si la efectividad actual
            // del rep queda por debajo de la meta de SU división, se calculan los
            // "clientes con pedido" que faltan (Visitados ya ocurrió, es fijo) y
            // se traduce a unidades asumiendo que cada cliente adicional activado
            // compra el ticket promedio actual de ESE rep (mismo supuesto que ya
            // usa Activación) — se reparte entre TODAS sus filas (todos los meses/
            // productos) según el peso que ya tenían.
            var metasEfectividad = PresupuestoBuilderCore.CargarEfectividadMetas(cn).Where(m => m.Modo == "Obligatorio").ToList();
            if (metasEfectividad.Count > 0)
            {
                Console.WriteLine($"[Generar] {metasEfectividad.Count} metas de efectividad OBLIGATORIAS configuradas {DateTime.Now:HH:mm:ss.fff}");
                var efectividad = PresupuestoBuilderCore.EfectividadPorRep(cn, req.baseFechaInicio, req.baseFechaFin.AddDays(1), repsEfectivo);

                var gruposEf = calculadas.Select((c, idx) => (c, idx)).GroupBy(t => (t.c.destino, t.c.rep));
                foreach (var grupo in gruposEf)
                {
                    var (destino, rep) = grupo.Key;
                    var (_, dimRepTipe) = dimsRep.GetValueOrDefault(rep, (0, ""));
                    var meta = PresupuestoBuilderCore.ResolverEfectividadMeta(metasEfectividad, dimRepTipe ?? "");
                    if (meta == null) continue;
                    if (!efectividad.TryGetValue(rep, out var ef) || ef.Visitados <= 0) continue;
                    double actualPct = (double)ef.ClientesConPedido / ef.Visitados;
                    if (actualPct >= meta.MetaPct) continue; // ya cumple, no se fuerza nada

                    int clientesFaltantes = (int)Math.Ceiling((meta.MetaPct - actualPct) * ef.Visitados);
                    double totalUnidadesRep = grupo.Sum(t => t.c.propuestoNatural);
                    if (totalUnidadesRep <= 0 || clientesFaltantes <= 0)
                    {
                        if (meta.BloqueaPublicar) bloqueaPublicar = true;
                        continue;
                    }
                    double unidadesPorClienteConPedido = ef.ClientesConPedido > 0 ? totalUnidadesRep / ef.ClientesConPedido : totalUnidadesRep / ef.Visitados;
                    double gapUnidades = clientesFaltantes * unidadesPorClienteConPedido;

                    // Cascada por categoría de mayor volumen (pedido explícito del usuario
                    // 2026-09-06): en vez de repartir el gap entre TODO lo que vende el rep, se
                    // concentra en la categoría de mayor volumen que Activación aún no tenga
                    // cubierta — coordina ambos hooks en vez de competir por el mismo presupuesto.
                    var categoriasRep = grupo
                        .Where(t => cat1PorProducto.GetValueOrDefault(t.c.proId) != null)
                        .GroupBy(t => cat1PorProducto[t.c.proId]!.Value)
                        .Select(g => (cat1: g.Key, total: g.Sum(t => t.c.propuestoNatural), filas: g.ToList()))
                        .OrderByDescending(x => x.total)
                        .ToList();

                    // Tier 1: la categoría de mayor volumen que TIENE meta de Activación
                    // Obligatoria configurada pero Activación aún no la cubrió (incluye el caso
                    // recortado por el tope de 5%).
                    var candidataTier1 = categoriasRep.FirstOrDefault(x =>
                        categoriasConMetaObligatoria.Contains((rep, x.cat1)) && !categoriasCubiertasActivacion.Contains((rep, x.cat1)));
                    // Tier 2 (fallback): categorías SIN ninguna meta de Activación Obligatoria
                    // configurada — se reparte proporcional entre ellas (no se concentra en 1 sola).
                    var candidatasTier2 = categoriasRep.Where(x => !categoriasConMetaObligatoria.Contains((rep, x.cat1))).ToList();

                    if (candidataTier1.filas != null && candidataTier1.filas.Count > 0)
                    {
                        double totalCat = candidataTier1.total;
                        foreach (var (c, idx) in candidataTier1.filas)
                        {
                            double peso = c.propuestoNatural / totalCat;
                            calculadas[idx] = (c.destino, c.rep, c.proId, c.baseNetaMensual, c.propuestoNatural + gapUnidades * peso, c.valorActual);
                        }
                        notasActivacion.Add($"Efectividad forzada {rep}/{destino.anio}-{destino.mes:00}: +{clientesFaltantes} clientes con pedido (+{gapUnidades:0.##} u.) concentrado en Cat1={candidataTier1.cat1} (mayor volumen no cubierta por Activación)");
                    }
                    else if (candidatasTier2.Count > 0)
                    {
                        double totalTier2 = candidatasTier2.Sum(x => x.total);
                        foreach (var cat in candidatasTier2)
                            foreach (var (c, idx) in cat.filas)
                            {
                                double peso = c.propuestoNatural / totalTier2;
                                calculadas[idx] = (c.destino, c.rep, c.proId, c.baseNetaMensual, c.propuestoNatural + gapUnidades * peso, c.valorActual);
                            }
                        notasActivacion.Add($"Efectividad forzada {rep}/{destino.anio}-{destino.mes:00}: +{clientesFaltantes} clientes con pedido (+{gapUnidades:0.##} u.) repartido entre categorías sin meta de Activación");
                    }
                    else
                    {
                        // Tier 3 (red de seguridad final, comportamiento original): proporcional
                        // entre TODO lo que vende el rep — solo llega aquí si TODAS sus categorías
                        // tienen meta de Activación Obligatoria y todas ya están cubiertas.
                        foreach (var (c, idx) in grupo)
                        {
                            double peso = c.propuestoNatural / totalUnidadesRep;
                            calculadas[idx] = (c.destino, c.rep, c.proId, c.baseNetaMensual, c.propuestoNatural + gapUnidades * peso, c.valorActual);
                        }
                        notasActivacion.Add($"Efectividad forzada {rep}/{destino.anio}-{destino.mes:00}: +{clientesFaltantes} clientes con pedido (+{gapUnidades:0.##} u.) repartido proporcional (sin categorías disponibles)");
                    }
                }
            }

            // PASO 1c: Mínimo/Máximo por vendedor (pedido explícito del usuario
            // 2026-09-01, dentro del rediseño de premisas del motor) — % sobre la
            // propia base histórica de CADA vendedor (no un monto fijo). Se aplica
            // DESPUÉS de crecimiento/rappel/piso Y de activación/efectividad
            // obligatorias (para no pelear con esas reglas), y ANTES del reescalado
            // a "Monto Total Sugerido" (que opera sobre el total ya recortado).
            var limitesRep = PresupuestoBuilderCore.CargarLimitesRep(cn, repsEfectivo);
            if (limitesRep.Count > 0)
            {
                var gruposRep = calculadas.Select((c, idx) => (c, idx)).GroupBy(t => (t.c.destino, t.c.rep));
                foreach (var grupo in gruposRep)
                {
                    var (destino, rep) = grupo.Key;
                    if (!limitesRep.TryGetValue(rep, out var limite) || (limite.PctMinimo == null && limite.PctMaximo == null)) continue;
                    double baseNetaRep = grupo.Sum(t => t.c.baseNetaMensual);
                    double propuestoRep = grupo.Sum(t => t.c.propuestoNatural);
                    if (baseNetaRep <= 0 || propuestoRep <= 0) continue;
                    double? piso = limite.PctMinimo is > 0 ? baseNetaRep * limite.PctMinimo.Value : null;
                    double? techo = limite.PctMaximo is > 0 ? baseNetaRep * limite.PctMaximo.Value : null;
                    double? objetivo = piso is not null && propuestoRep < piso ? piso
                                      : techo is not null && propuestoRep > techo ? techo
                                      : null;
                    if (objetivo == null) continue;
                    double escalaRep = objetivo.Value / propuestoRep;
                    foreach (var (c, idx) in grupo)
                        calculadas[idx] = (c.destino, c.rep, c.proId, c.baseNetaMensual, c.propuestoNatural * escalaRep, c.valorActual);
                    notasActivacion.Add($"Límite aplicado {rep}/{destino.anio}-{destino.mes:00}: {propuestoRep:0.##} → {objetivo.Value:0.##} u. ({(escalaRep >= 1 ? "piso" : "techo")})");
                }
            }

            // PASO 1d: Umbral ABSOLUTO en RD$ (mínimo/máximo) por Suplidor y por
            // Categoría (pedido explícito del usuario 2026-09-01: "si el
            // presupuesto propuesto de un suplidor es 100000 y quiero que el
            // mínimo sea 1500000, ¿dónde coloco esto?"). NO es una tabla nueva —
            // reusa el catálogo genérico PresupuestoVariablesValores_RM (mismo
            // que FACTOR_CRECIMIENTO_MES/PISO_MINIMO_PCT) con 2 variables nuevas,
            // "UMBRAL_MINIMO_RD"/"UMBRAL_MAXIMO_RD" (ver
            // SQL/PresupuestoUmbralesRD_Seed.sql) — hereda gratis la cascada
            // Mes>Trimestre>Año y el ámbito Suplidor/Categoría/Global que el
            // usuario preguntó si ya existía. Es un monto en RD$ (no unidades),
            // así que cada fila se convierte a RD$ con el mismo precio de Lista
            // real que usa "Sugerido" en el resto del módulo
            // (PrecioListaPorRepYProducto, extraída en la 34ª parte).
            //
            // Global se excluye A PROPÓSITO de este hook (filtrado abajo): un
            // umbral "Global" evaluado por cada subgrupo de Suplidor/Categoría
            // por separado no tendría sentido (se dispararía en cada uno) — para
            // un piso/techo de TODA la compañía ya existe "Monto Total Sugerido"
            // en el paso de Generar.
            var cacheUmbralMinRD = new Dictionary<int, List<PresupuestoBuilderCore.VariableRegla>>();
            var cacheUmbralMaxRD = new Dictionary<int, List<PresupuestoBuilderCore.VariableRegla>>();
            foreach (var anio in aniosDistintos)
            {
                cacheUmbralMinRD[anio] = PresupuestoBuilderCore.CargarVariable(cn, "UMBRAL_MINIMO_RD", anio).Where(r => r.AmbitoTipo != "Global").ToList();
                cacheUmbralMaxRD[anio] = PresupuestoBuilderCore.CargarVariable(cn, "UMBRAL_MAXIMO_RD", anio).Where(r => r.AmbitoTipo != "Global").ToList();
            }
            if (cacheUmbralMinRD.Values.Any(l => l.Count > 0) || cacheUmbralMaxRD.Values.Any(l => l.Count > 0))
            {
                var proIdsTodos = calculadas.Select(c => c.proId).Distinct().ToList();
                var precioLista = PresupuestoBuilderCore.PrecioListaPorRepYProducto(cn, repsEfectivo, proIdsTodos);
                double Monto((DestinoMes destino, string rep, int proId, double baseNetaMensual, double propuestoNatural, double valorActual) c)
                    => c.propuestoNatural * precioLista.GetValueOrDefault((c.rep, c.proId), 0);

                void AplicarUmbralAbsoluto(Func<int, string?> claveDim, string ambitoNombre)
                {
                    var grupos = calculadas.Select((c, idx) => (c, idx))
                        .Where(t => claveDim(t.c.proId) != null)
                        .GroupBy(t => (t.c.destino, clave: claveDim(t.c.proId)!));
                    foreach (var grupo in grupos)
                    {
                        var (destino, clave) = grupo.Key;
                        string? cat3Arg = ambitoNombre == "Suplidor" ? clave : null;
                        string? cat1Arg = ambitoNombre == "Categoria" ? clave : null;
                        double minimo = PresupuestoBuilderCore.ResolverVariableEnMemoria(cacheUmbralMinRD[destino.anio], destino.mes, null, cat3Arg, cat1Arg, double.NaN);
                        double maximo = PresupuestoBuilderCore.ResolverVariableEnMemoria(cacheUmbralMaxRD[destino.anio], destino.mes, null, cat3Arg, cat1Arg, double.NaN);
                        if (double.IsNaN(minimo) && double.IsNaN(maximo)) continue;
                        double montoGrupo = grupo.Sum(t => Monto(t.c));
                        if (montoGrupo <= 0) continue;
                        double? objetivo = !double.IsNaN(minimo) && montoGrupo < minimo ? minimo
                                          : !double.IsNaN(maximo) && montoGrupo > maximo ? maximo
                                          : null;
                        if (objetivo == null) continue;
                        double escala = objetivo.Value / montoGrupo;
                        foreach (var (c, idx) in grupo)
                            calculadas[idx] = (c.destino, c.rep, c.proId, c.baseNetaMensual, c.propuestoNatural * escala, c.valorActual);
                        notasActivacion.Add($"Umbral RD$ aplicado {ambitoNombre}={clave}/{destino.anio}-{destino.mes:00}: {montoGrupo:0.##} → {objetivo.Value:0.##} ({(escala >= 1 ? "mínimo" : "máximo")})");
                    }
                }
                AplicarUmbralAbsoluto(proId => cat3PorProducto.GetValueOrDefault(proId), "Suplidor");
                AplicarUmbralAbsoluto(proId => cat1PorProducto.GetValueOrDefault(proId)?.ToString(), "Categoria");
            }

            // PASO 2: reescalado POR MES (rediseño "Objetivo por Mes + Total híbrido",
            // pedido explícito del usuario 2026-09-06) — antes era UN SOLO reescalado
            // global; ahora cada mes Destino queda garantizado en su propia "porción
            // justa" (propia o prorrateada), aplicado AL FINAL (después de Límites/
            // Umbral RD$) para que ningún hook lo descuadre después.
            // - Mes con entrada en 'objetivosPorMes' -> se fija EXACTO a ese valor.
            // - Meses sin entrada propia -> reparten proporcional (según su peso
            //   natural ENTRE ELLOS) lo que quede del Total (montoTotalSugerido/
            //   montoValorSugerido) menos lo que los meses ya fijados representan.
            // - Sin ningún objetivo ni Total -> escala=1 (natural tal cual, como
            //   siempre). Simplificación asumida: el modo (Valor/Unidades) es el
            //   MISMO para el Total y para todos los 'objetivosPorMes' de una misma
            //   corrida (el front solo expone un selector de modo compartido).
            var totalNaturalPorMes = req.destino.ToDictionary(d => (d.anio, d.mes),
                d => calculadas.Where(c => c.destino.anio == d.anio && c.destino.mes == d.mes).Sum(c => c.propuestoNatural));

            // Precio de LISTA, no promedio histórico (2026-09-06, bug real reportado por el
            // usuario en vivo): "MontoPropuesto" en Comparativo/Excel/MNTDPROD SIEMPRE se
            // calcula como unidades × precio de Lista (RepresentantesAdicionales.LipCodigo →
            // ListaPrecios) — verificado en vivo desde antes (2026-08-30) que esto reproduce
            // EXACTO el monto real ya guardado en MNTDPROD. Si aquí convertimos el Objetivo
            // Valor a unidades usando el precio PROMEDIO realizado (que ya trae descuentos,
            // normalmente MENOR al de Lista), las unidades resultantes quedan de más — y al
            // revalorizarlas a precio de Lista en cualquier otro lado, el monto sale MAYOR al
            // Objetivo pedido. Usando el MISMO precio de Lista aquí, el monto final cuadra
            // exacto con lo pedido en todos lados.
            Dictionary<(string rep, int proId), double>? precioListaCache = null;
            Dictionary<(string rep, int proId), double> PrecioLista()
            {
                if (precioListaCache == null)
                {
                    var proIdsCalc = calculadas.Select(c => c.proId).Distinct().ToList();
                    precioListaCache = PresupuestoBuilderCore.PrecioListaPorRepYProducto(cn, repsEfectivo, proIdsCalc);
                }
                return precioListaCache;
            }
            double NaturalRD((int anio, int mes) d) => calculadas.Where(c => c.destino.anio == d.anio && c.destino.mes == d.mes)
                .Sum(c => c.propuestoNatural * PrecioLista().GetValueOrDefault((c.rep, c.proId), 0));

            var escalaPorMes = new Dictionary<(int anio, int mes), double>();
            var mesesConObjetivoPropio = new HashSet<(int anio, int mes)>();
            if (req.objetivosPorMes is { Count: > 0 })
            {
                foreach (var om in req.objetivosPorMes)
                {
                    var key = (om.anio, om.mes);
                    if (!totalNaturalPorMes.ContainsKey(key)) continue;
                    double naturalMes = om.modo == "valor" ? NaturalRD(key) : totalNaturalPorMes[key];
                    escalaPorMes[key] = naturalMes > 0 ? om.valor / naturalMes : 0;
                    mesesConObjetivoPropio.Add(key);
                }
            }

            var mesesRestantes = req.destino.Select(d => (d.anio, d.mes)).Where(k => !mesesConObjetivoPropio.Contains(k)).ToList();
            double? totalObjetivo = req.montoValorSugerido is > 0 ? req.montoValorSugerido
                                   : req.montoTotalSugerido is > 0 ? req.montoTotalSugerido
                                   : (double?)null;
            bool objetivoEsValor = req.montoValorSugerido is > 0;
            if (mesesRestantes.Count > 0)
            {
                if (totalObjetivo is > 0)
                {
                    double yaFijado = mesesConObjetivoPropio.Sum(k => req.objetivosPorMes!.First(om => (om.anio, om.mes) == k).valor);
                    double restante = totalObjetivo.Value - yaFijado;
                    double naturalRestante = objetivoEsValor ? mesesRestantes.Sum(NaturalRD) : mesesRestantes.Sum(k => totalNaturalPorMes[k]);
                    foreach (var key in mesesRestantes)
                    {
                        double naturalMes = objetivoEsValor ? NaturalRD(key) : totalNaturalPorMes[key];
                        double porcion = naturalRestante > 0 ? restante * (naturalMes / naturalRestante) : 0;
                        escalaPorMes[key] = naturalMes > 0 ? porcion / naturalMes : 0;
                    }
                    Console.WriteLine($"[Generar] Total repartido entre {mesesRestantes.Count} mes(es) restantes: restante={restante:0.##} (ya fijado={yaFijado:0.##}) {DateTime.Now:HH:mm:ss.fff}");
                }
                else
                {
                    foreach (var key in mesesRestantes) escalaPorMes[key] = 1.0;
                }
            }

            var loteId = Guid.NewGuid();
            using var tx = cn.BeginTransaction(); // 1 solo commit al final -> decenas de miles de inserts dejan de pagar fsync uno por uno
            Console.WriteLine($"[Generar] transaccion abierta {DateTime.Now:HH:mm:ss.fff}");
            try
            {
                // Nota de contexto (comparación/redistribución elegidas) — se anexa a la
                // descripción (compatibilidad con lotes viejos) Y, desde el rediseño
                // 2026-09-06, además se persiste estructurado en PresupuestoLotes_RM
                // (Objetivo*) para que el Excel de revisión (Bloque B) lo pueda mostrar
                // sin tener que re-parsear texto libre.
                var notas = new List<string>();
                if (req.compAnio is > 0 && req.compMes is >= 1 and <= 12) notas.Add($"Comparado vs {req.compAnio}-{req.compMes:00}");
                if (req.objetivosPorMes is { Count: > 0 })
                    foreach (var om in req.objetivosPorMes)
                        notas.Add($"Objetivo {om.anio}-{om.mes:00} fijado a {(om.modo == "valor" ? "RD$" : "")}{om.valor:0,0.##} (escala={escalaPorMes.GetValueOrDefault((om.anio, om.mes), 1):0.####})");
                if (totalObjetivo is > 0) notas.Add($"Total {(objetivoEsValor ? "valor" : "unidades")}={(objetivoEsValor ? "RD$" : "")}{totalObjetivo:0,0.##} repartido entre {mesesRestantes.Count} mes(es) sin objetivo propio");
                notas.AddRange(notasActivacion);
                if (bloqueaPublicar) notas.Add("⚠BLOQUEA_PUBLICAR: hay meta(s) de activación obligatoria que no se alcanzan ni forzando el presupuesto (revisar antes de publicar)");
                var descripcionFinal = string.Join(" · ", new[] { req.descripcion }.Concat(notas).Where(s => !string.IsNullOrWhiteSpace(s)));
                // Tope defensivo (2026-09-06, bug real reportado por el usuario en vivo: "String
                // or binary data would be truncated... column 'Descripcion'") — el rediseño de
                // reglas agrega MUCHAS más notas (por mes, tope de Activación, cascada de
                // Efectividad) que antes, y pueden superar el tamaño de la columna aunque se
                // amplíe (ver SQL/PresupuestoLotes_RM_Descripcion_Alter.sql). Se corta acá TAMBIÉN
                // en código para que un lote con muchísimas notas nunca vuelva a tronar el INSERT
                // — se prefiere una descripción incompleta a que Generar falle por completo.
                const int descripcionMaxLen = 4000;
                if (descripcionFinal.Length > descripcionMaxLen)
                    descripcionFinal = descripcionFinal.Substring(0, descripcionMaxLen - 3) + "...";

                string? objetivoModoPersist = totalObjetivo is > 0 ? (objetivoEsValor ? "valor" : "unidades")
                    : req.objetivosPorMes is { Count: > 0 } ? (req.objetivosPorMes[0].modo) : null;
                double? objetivoTotalPersist = totalObjetivo is > 0 ? totalObjetivo
                    : req.objetivosPorMes is { Count: > 0 } ? req.objetivosPorMes.Sum(om => om.valor) : (double?)null;
                string? objetivoPorMesJson = req.objetivosPorMes is { Count: > 0 }
                    ? "[" + string.Join(",", req.objetivosPorMes.Select(om => $"{{\"anio\":{om.anio},\"mes\":{om.mes},\"valor\":{om.valor.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}")) + "]"
                    : null;

                using (var cmdLote = new SqlCommand(@"
INSERT INTO PresupuestoLotes_RM(LoteId,Descripcion,BaseFechaInicio,BaseFechaFin,DestinoDetalle,PreTiposIncluidos,Estado,UsuInicioSesion,ObjetivoModo,ObjetivoTotalRD,ObjetivoPorMesJson)
VALUES(@id,@desc,@bi,@bf,@dd,@pt,'Borrador',@u,@om,@ot,@opm)", cn, tx) { CommandTimeout = 30 })
                {
                    cmdLote.Parameters.AddWithValue("@id", loteId);
                    cmdLote.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(descripcionFinal) ? (object)DBNull.Value : descripcionFinal);
                    cmdLote.Parameters.AddWithValue("@bi", req.baseFechaInicio.Date);
                    cmdLote.Parameters.AddWithValue("@bf", req.baseFechaFin.Date);
                    cmdLote.Parameters.AddWithValue("@dd", string.Join(",", req.destino.Select(d => $"{d.anio}-{d.mes:00}")));
                    cmdLote.Parameters.AddWithValue("@pt", req.preTipo);
                    cmdLote.Parameters.AddWithValue("@u", user);
                    cmdLote.Parameters.AddWithValue("@om", (object?)objetivoModoPersist ?? DBNull.Value);
                    cmdLote.Parameters.AddWithValue("@ot", (object?)objetivoTotalPersist ?? DBNull.Value);
                    cmdLote.Parameters.AddWithValue("@opm", (object?)objetivoPorMesJson ?? DBNull.Value);
                    cmdLote.ExecuteNonQuery();
                }

                // BUG DE RENDIMIENTO CORREGIDO 2026-08-30 (2da vuelta): con la base
                // histórica completa (miles de filas x varios meses destino) esto
                // hacía un ExecuteNonQuery() POR FILA — decenas de miles de idas y
                // vueltas a la BD en una sola request. Confirmado en vivo con logs
                // paso a paso: todo el resto del método corría en ~3s, pero se
                // colgaba minutos enteros procesando este bucle. Ahora se arma TODO
                // en memoria (DataTable) y se sube de una sola vez con SqlBulkCopy
                // — miles de filas en menos de 1 segundo, sin importar cuántas sean.
                Console.WriteLine($"[Generar] lote insertado, armando tabla en memoria (bulk insert) {DateTime.Now:HH:mm:ss.fff}");
                var tabla = new System.Data.DataTable();
                tabla.Columns.Add("LoteId", typeof(Guid));
                tabla.Columns.Add("RepCodigo", typeof(string));
                tabla.Columns.Add("PreTipo", typeof(string));
                tabla.Columns.Add("PreAnio", typeof(short));
                tabla.Columns.Add("PreMes", typeof(short));
                tabla.Columns.Add("PreReferencia", typeof(string));
                tabla.Columns.Add("PrePresupuestoPropuesto", typeof(decimal));
                tabla.Columns.Add("PrePresupuestoActual", typeof(decimal));
                tabla.Columns.Add("Anomalia", typeof(bool));
                tabla.Columns.Add("AnomaliaDetalle", typeof(string));

                int anomaliasCount = 0;
                foreach (var c in calculadas)
                {
                    double propuestoFinal = Math.Round(c.propuestoNatural * escalaPorMes.GetValueOrDefault((c.destino.anio, c.destino.mes), 1.0), 2);
                    if (propuestoFinal <= 0) continue;
                    var (anomalia, detalle) = PresupuestoBuilderCore.EvaluarAnomalia(c.baseNetaMensual, propuestoFinal, umbralAnomalia);
                    if (anomalia) anomaliasCount++;

                    var row = tabla.NewRow();
                    row["LoteId"] = loteId;
                    row["RepCodigo"] = c.rep;
                    row["PreTipo"] = req.preTipo;
                    row["PreAnio"] = (short)c.destino.anio;
                    row["PreMes"] = (short)c.destino.mes;
                    row["PreReferencia"] = c.proId.ToString();
                    row["PrePresupuestoPropuesto"] = (decimal)propuestoFinal;
                    row["PrePresupuestoActual"] = (decimal)c.valorActual;
                    row["Anomalia"] = anomalia;
                    row["AnomaliaDetalle"] = (object?)detalle ?? DBNull.Value;
                    tabla.Rows.Add(row);
                }
                Console.WriteLine($"[Generar] tabla armada con {tabla.Rows.Count} filas, iniciando SqlBulkCopy {DateTime.Now:HH:mm:ss.fff}");

                using (var bulk = new SqlBulkCopy(cn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "PresupuestoStaging_RM", BulkCopyTimeout = 120 })
                {
                    foreach (System.Data.DataColumn col in tabla.Columns)
                        bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                    bulk.WriteToServer(tabla);
                }
                int filasInsertadas = tabla.Rows.Count;
                Console.WriteLine($"[Generar] SqlBulkCopy completado, {filasInsertadas} filas {DateTime.Now:HH:mm:ss.fff}");

                tx.Commit();
                Console.WriteLine($"[Generar] <<< FIN OK {DateTime.Now:HH:mm:ss.fff} - filas={filasInsertadas}");
                return Ok(new { ok = true, loteId, filasInsertadas, anomalias = anomaliasCount });
            }
            catch { tx.Rollback(); throw; }
        }
        catch (Exception ex) { Console.WriteLine($"[Generar] <<< EXCEPCION {DateTime.Now:HH:mm:ss.fff}: {ex}"); return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apipresupuestogestion/lote/{loteId} ────────────────────────────
    [HttpGet("lote/{loteId:guid}")]
    public IActionResult GetLote(Guid loteId)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            object? cabecera = null;
            using (var cmd = new SqlCommand("SELECT LoteId,Descripcion,BaseFechaInicio,BaseFechaFin,DestinoDetalle,PreTiposIncluidos,Estado,UsuInicioSesion,FechaCreacion FROM PresupuestoLotes_RM WHERE LoteId=@id", cn))
            {
                cmd.Parameters.AddWithValue("@id", loteId);
                using var rd = cmd.ExecuteReader();
                if (!rd.Read()) return NotFound(new { ok = false, error = "Lote no encontrado." });
                cabecera = new
                {
                    LoteId = rd.GetGuid(0), Descripcion = rd.IsDBNull(1) ? null : rd.GetString(1),
                    BaseFechaInicio = rd.GetDateTime(2), BaseFechaFin = rd.GetDateTime(3),
                    DestinoDetalle = rd.GetString(4), PreTiposIncluidos = rd.GetString(5),
                    Estado = rd.GetString(6), UsuInicioSesion = rd.GetString(7), FechaCreacion = rd.GetDateTime(8),
                };
            }
            var filas = new List<object>();
            using (var cmd = new SqlCommand(@"
SELECT s.Id, s.RepCodigo, r.RepNombre, s.PreAnio, s.PreMes, s.PreReferencia, pr.ProDescripcion,
       s.PrePresupuestoPropuesto, s.PrePresupuestoActual, s.Anomalia, s.AnomaliaDetalle
FROM PresupuestoStaging_RM s
LEFT JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = s.RepCodigo
LEFT JOIN Productos pr WITH (NOLOCK) ON pr.ProID = TRY_CAST(s.PreReferencia AS INT)
WHERE s.LoteId=@id
ORDER BY s.RepCodigo, s.PreAnio, s.PreMes, s.PreReferencia", cn) { CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@id", loteId);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    filas.Add(new
                    {
                        Id = rd.GetInt32(0),
                        RepCodigo = rd.GetString(1), RepNombre = rd.IsDBNull(2) ? "" : rd.GetString(2),
                        PreAnio = rd.GetInt16(3), PreMes = rd.GetInt16(4), PreReferencia = rd.GetString(5),
                        ProductoNombre = rd.IsDBNull(6) ? "" : rd.GetString(6),
                        Propuesto = Convert.ToDouble(rd.GetValue(7)),
                        Actual = rd.IsDBNull(8) ? (double?)null : Convert.ToDouble(rd.GetValue(8)),
                        Anomalia = rd.GetBoolean(9), AnomaliaDetalle = rd.IsDBNull(10) ? null : rd.GetString(10),
                    });
            }
            return Ok(new { ok = true, lote = cabecera, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apipresupuestogestion/lote/{loteId}/comparativo ────────────────
    // Análisis "de lo general a lo particular" pedido por el usuario 2026-08-30:
    // agrega el lote a nivel Rep (Centro/Canal/Supervisor/Vendedor vienen en la
    // misma fila para que el drill-down sea 100% client-side, mismo patrón que
    // ApiPresupuestosController.DrillOrganizacion). Sin compAnio/compMes, compara
    // contra lo ya publicado en el MISMO período (PrePresupuestoActual, ya
    // guardado en cada fila de staging). Con compAnio/compMes, compara contra el
    // PrePresupuesto de ESE mes específico en producción — y como el propuesto
    // puede sumar varios meses destino, se muestra como PROMEDIO MENSUAL para
    // que la comparación contra 1 solo mes sea justa (pedido explícito del
    // usuario: comparar contra el presupuesto que se fijó ese mes, no el ejecutado).
    // Cajas = unidades / Productos.ProUnidades. Monto (2026-08-30, corregido a pedido
    // explícito del usuario, ya NO es un estimado): Sugerido/Ventas Promedio = unidades ×
    // el precio de la Lista de Precios que corresponde al canal/lista de CADA vendedor
    // (RepresentantesAdicionales.LipCodigo → ListaPrecios) — el mismo método real que usa
    // el proceso que arma PreTipo='MNTDPROD' (verificado en vivo, reproduce exacto ese
    // valor). Comparación = el MNTDPROD ya publicado directamente, sin recalcular nada.
    // FilaComparativo (2026-09-01, Fase D): antes 'resultado' era una List<anónimo> devuelta
    // directo desde el HttpGet — se extrajo a un record con nombre para que la Fase D
    // (export a Excel) pueda reusar EXACTAMENTE el mismo cálculo (mismos números que la
    // pantalla) desde un método privado, sin duplicar ~270 líneas de reglas de negocio ya
    // verificadas contra producción. Las propiedades tienen los MISMOS nombres que el
    // anónimo original — el JSON que devuelve /comparativo no cambia en absoluto.
    public sealed record FilaComparativo(
        string RepCodigo, string RepNombre, string CedCodigo, string CentroNombre,
        string SupCod, string SupNombre, int CanID, string CanNombre,
        int ProId, string ProDescripcion, int Cat1ID, string Cat1Nombre, int Cat3ID, string Cat3Nombre,
        double UnidadesPropuesto, double CajasPropuesto, double MontoPropuesto,
        double UnidadesComparacion, double CajasComparacion, double MontoComparacion,
        double UnidadesVentaPromedio, double CajasVentaPromedio, double MontoVentaPromedio);

    [HttpGet("lote/{loteId:guid}/comparativo")]
    public IActionResult Comparativo(Guid loteId, [FromQuery] int? compAnio, [FromQuery] int? compMes, [FromQuery] int? filtroAnio, [FromQuery] int? filtroMes)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            var (ok, error, esPromedioMensual, nMesesDestinoR, filasR) = CalcularComparativoInterno(cn, loteId, compAnio, compMes, filtroAnio, filtroMes);
            if (!ok) return NotFound(new { ok = false, error });
            return Ok(new { ok = true, esPromedioMensual, nMesesDestino = nMesesDestinoR, filas = filasR });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Cuerpo real de /comparativo, extraído tal cual (sin cambiar ninguna línea de lógica)
    // para poder reusarlo desde el export a Excel de la Fase D. Devuelve (ok, error, ...)
    // en vez de IActionResult para que ambos llamadores (el HttpGet de arriba y el export)
    // decidan cómo responder.
    //
    // filtroAnio/filtroMes (2026-09-01, pedido explícito del usuario: "no todos los
    // volúmenes son del mes actual, quiero ver/ajustar otros meses por separado"):
    // opcionales — si vienen, el Staging se filtra a ESE mes destino exacto en vez de
    // sumar todos los meses del lote, y ya no se divide por nMesesDestino (es un solo
    // mes, no un promedio). Sin ellos, comportamiento 100% igual que antes (promedio
    // mensual de todo el rango Destino).
    private (bool ok, string? error, bool esPromedioMensual, int nMesesDestino, List<FilaComparativo> filas) CalcularComparativoInterno(SqlConnection cn, Guid loteId, int? compAnio, int? compMes, int? filtroAnio = null, int? filtroMes = null)
    {
            bool filtrandoMes = filtroAnio is > 0 && filtroMes is >= 1 and <= 12;
            string preTipo; DateTime baseIni, baseFin; int nMesesDestino; string destinoDetalleRaw;
            using (var cmd = new SqlCommand("SELECT PreTiposIncluidos, BaseFechaInicio, BaseFechaFin, DestinoDetalle FROM PresupuestoLotes_RM WHERE LoteId=@id", cn))
            {
                cmd.Parameters.AddWithValue("@id", loteId);
                using var rd = cmd.ExecuteReader();
                if (!rd.Read()) return (false, "Lote no encontrado.", false, 0, new());
                preTipo = rd.GetString(0).Split(',')[0];
                baseIni = rd.GetDateTime(1); baseFin = rd.GetDateTime(2);
                destinoDetalleRaw = rd.GetString(3);
                nMesesDestino = destinoDetalleRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
            }
            if (nMesesDestino < 1) nMesesDestino = 1;
            int mesesBase = ((baseFin.Year - baseIni.Year) * 12 + baseFin.Month - baseIni.Month) + 1;
            if (mesesBase < 1) mesesBase = 1;

            // Staging agregado por Rep x Producto — sumando todos los meses destino del
            // lote (comportamiento original), o restringido a un único mes si filtrandoMes.
            var filas = new List<(string rep, int proId, double propuesto, double actualMismoPeriodo)>();
            using (var cmd = new SqlCommand($@"
SELECT RepCodigo, PreReferencia, SUM(PrePresupuestoPropuesto), SUM(ISNULL(PrePresupuestoActual,0))
FROM PresupuestoStaging_RM WHERE LoteId=@id{(filtrandoMes ? " AND PreAnio=@fa AND PreMes=@fm" : "")}
GROUP BY RepCodigo, PreReferencia", cn) { CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@id", loteId);
                if (filtrandoMes) { cmd.Parameters.AddWithValue("@fa", filtroAnio!.Value); cmd.Parameters.AddWithValue("@fm", filtroMes!.Value); }
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    if (!int.TryParse(rd.GetString(1), out var proId)) continue;
                    filas.Add((rd.GetString(0), proId, Convert.ToDouble(rd.GetValue(2)), Convert.ToDouble(rd.GetValue(3))));
                }
            }

            // Comparación contra un mes específico (opcional) — PrePresupuesto de
            // ESE mes en PRODUCCIÓN (ahí vive el histórico real ya cerrado).
            Dictionary<(string, int), double>? compEspecifico = null;
            if (compAnio is > 0 && compMes is >= 1 and <= 12 && filas.Count > 0)
            {
                compEspecifico = new();
                var reps = filas.Select(f => f.rep).Distinct().ToList();
                var parms = new Dictionary<string, object> { ["@tipo"] = preTipo, ["@anio"] = compAnio, ["@mes"] = compMes };
                var names = reps.Select((r, i) => { var n = $"@r{i}"; parms[n] = r; return n; }).ToList();
                using var cmd = new SqlCommand($@"
SELECT RepCodigo, PreReferencia, PrePresupuesto
FROM PresupuestosHistoricos WITH (NOLOCK)
WHERE PreTipo=@tipo AND PreAnio=@anio AND PreMes=@mes AND RepCodigo IN ({string.Join(",", names)})", cn) { CommandTimeout = 60 };
                foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    if (!int.TryParse(rd.GetString(1), out var proId)) continue;
                    compEspecifico[(rd.GetString(0), proId)] = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
                }
            }

            // "Ventas Promedio" (pedido explícito del usuario 2026-08-30, columna aparte
            // en el análisis): el promedio mensual del MISMO Rango Base con que se generó
            // el lote — recalculado aquí (no se guarda en Staging_RM) con el mismo motor
            // y universo de productos por rep que usó Generar, para que sea exactamente
            // la cifra que dio origen a la propuesta.
            //
            // MontoVentaPromedio (2026-08-31, corrección pedida explícitamente por el usuario
            // tras validar en vivo que el monto revalorizado a precio de Lista NO cuadraba con
            // la venta real — RD$4.09MM/mes de diferencia, concentrada en vendedores de canal
            // Mayorista/Especial que venden con descuento real sobre la lista): ya NO se
            // revaloriza `unidades × precio de Lista` — se usa DIRECTO el `MontoNeto` real
            // (VenCantidad×VenPrecio - Descuento) que `CalcularBaseHistorica` ya calculaba y
            // se descartaba. "Sugerido" (unidades futuras) SÍ sigue usando precio de Lista
            // porque ahí no existe un precio de venta real todavía.
            var ventaPromedio = new Dictionary<(string, int), double>();
            var ventaPromedioMonto = new Dictionary<(string, int), double>();
            if (filas.Count > 0)
            {
                var repsVenta = filas.Select(f => f.rep).Distinct().ToList();
                var universoVenta = PresupuestoBuilderCore.UniversoProductosPorRep(cn, repsVenta);
                var baseHistVenta = PresupuestoBuilderCore.CalcularBaseHistorica(cn, baseIni, baseFin.AddDays(1), universoVenta, repsVenta);
                foreach (var b in baseHistVenta)
                {
                    ventaPromedio[(b.RepCodigo, b.ProId)] = b.UnidadesNetas / mesesBase;
                    ventaPromedioMonto[(b.RepCodigo, b.ProId)] = b.MontoNeto / mesesBase;
                }
            }

            // Monto REAL (2026-08-30, pedido explícito: "el presupuesto promedio cuando sea
            // en Monto debes guardarlo del valor que está en la tabla de presupuesto en
            // monto... MNTDPROD") — para la columna de comparación, en vez de estimar,
            // se trae DIRECTO el monto ya publicado bajo PreTipo='MNTDPROD' (siempre en
            // producción, sea cual sea PresupuestosHistoricosDb — MNTDPROD no lo genera
            // este módulo). Se pide para TODOS los productos de los reps en alcance, no
            // solo los que el motor propuso (ver 'todasLasClaves' más abajo). Mismo
            // tratamiento "mes específico" (1 solo mes, no se divide) vs. "mismo período"
            // (suma de los N meses destino del lote, se promedia con 'divisorComp' más
            // abajo, igual que el lado en unidades) ya usado arriba.
            var montoReal = new Dictionary<(string, int), double>();
            if (filas.Count > 0)
            {
                var repsMonto = filas.Select(f => f.rep).Distinct().ToList();
                var parmsM = new Dictionary<string, object>();
                var repNamesM = repsMonto.Select((r, i) => { var n = $"@mr{i}"; parmsM[n] = r; return n; }).ToList();
                string filtroPeriodo;
                if (compAnio is > 0 && compMes is >= 1 and <= 12)
                {
                    filtroPeriodo = "h.PreAnio=@mAnio AND h.PreMes=@mMes";
                    parmsM["@mAnio"] = compAnio.Value; parmsM["@mMes"] = compMes.Value;
                }
                else
                {
                    var pares = destinoDetalleRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Split('-')).Where(a => a.Length == 2 && int.TryParse(a[0], out _) && int.TryParse(a[1], out _))
                        .Select(a => (anio: int.Parse(a[0]), mes: int.Parse(a[1]))).ToList();
                    filtroPeriodo = pares.Count > 0
                        ? $"EXISTS (SELECT 1 FROM (VALUES {string.Join(",", pares.Select(p => $"({p.anio},{p.mes})"))}) dm(a,m) WHERE dm.a=h.PreAnio AND dm.m=h.PreMes)"
                        : "1=0";
                }
                using var cmdM = new SqlCommand($@"
SELECT h.RepCodigo, h.PreReferencia, SUM(h.PrePresupuesto)
FROM PresupuestosHistoricos h WITH (NOLOCK)
WHERE h.PreTipo='MNTDPROD' AND {filtroPeriodo} AND h.RepCodigo IN ({string.Join(",", repNamesM)})
GROUP BY h.RepCodigo, h.PreReferencia", cn) { CommandTimeout = 60 };
                foreach (var kv in parmsM) cmdM.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rdM = cmdM.ExecuteReader();
                while (rdM.Read())
                {
                    if (!int.TryParse(rdM.GetString(1), out var proId)) continue;
                    montoReal[(rdM.GetString(0), proId)] = Convert.ToDouble(rdM.GetValue(2));
                }
            }

            // UNIÓN de pares Rep×Producto (2026-08-31, corrección de fondo pedida por el
            // usuario tras validar que "Presupuesto 2026-08" en pantalla NO cuadraba con el
            // real de la tabla de presupuestos: RD$84.18MM real vs RD$77.84MM en pantalla).
            // Causa encontrada y confirmada en vivo: antes solo se listaban los pares que el
            // MOTOR llegó a proponer (los de 'filas', que salen de la base histórica del
            // Rango Base) — cualquier producto con presupuesto REAL en el mes comparado (o
            // con venta en el Rango Base) que el motor no propuso quedaba invisible, aunque
            // sí exista. Ahora se arma la UNIÓN de las 4 fuentes (propuesto, comparación en
            // unidades, comparación en monto, ventas promedio) — así el total en pantalla
            // SIEMPRE cuadra con la suma real, con Propuesto=0 en los pares que el motor no
            // llegó a tocar.
            var todasLasClaves = new HashSet<(string rep, int proId)>(filas.Select(f => (f.rep, f.proId)));
            if (compEspecifico != null) foreach (var k in compEspecifico.Keys) todasLasClaves.Add(k);
            foreach (var k in montoReal.Keys) todasLasClaves.Add(k);
            foreach (var k in ventaPromedio.Keys) todasLasClaves.Add(k);

            var propuestoPorPar = filas.ToDictionary(f => (f.rep, f.proId), f => (f.propuesto, f.actualMismoPeriodo));

            // Productos: unidades-por-caja + dimensión Suplidor/Categoría (2026-08-30,
            // pedido explícito del usuario: el drill debe poder bajar también por
            // Suplidor/Categoría/Producto, no solo por Centro/Canal/Supervisor/Vendedor).
            var proIds = todasLasClaves.Select(k => k.proId).Distinct().ToList();
            var unidadesPorCaja = new Dictionary<int, double>();
            var prodDims = new Dictionary<int, (string nombre, int cat1Id, string cat1Nombre, int cat3Id, string cat3Nombre)>();
            if (proIds.Count > 0)
            {
                var parms1 = new Dictionary<string, object>();
                var names1 = proIds.Select((p, i) => { var n = $"@p{i}"; parms1[n] = p; return n; }).ToList();
                using var cmd = new SqlCommand($@"
SELECT pr.ProID, pr.ProUnidades, pr.ProDescripcion,
       ISNULL(pr.Cat1ID,0), ISNULL(c1.Cat1Descripcion,'(Sin categoría)'),
       ISNULL(pr.Cat3ID,0), ISNULL(c3.Cat3Descripcion,'(Sin suplidor)')
FROM Productos pr WITH (NOLOCK)
LEFT JOIN Categoria1 c1 WITH (NOLOCK) ON c1.Cat1ID=pr.Cat1ID
LEFT JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID=pr.Cat3ID
WHERE pr.ProID IN ({string.Join(",", names1)})", cn) { CommandTimeout = 30 };
                foreach (var kv in parms1) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    int proId = rd.GetInt32(0);
                    unidadesPorCaja[proId] = rd.IsDBNull(1) ? 1 : Convert.ToDouble(rd.GetValue(1));
                    prodDims[proId] = (rd.IsDBNull(2) ? "" : rd.GetString(2), rd.GetInt32(3), rd.GetString(4), rd.GetInt32(5), rd.GetString(6));
                }
            }
            // Nombres de canal (CanID es numérico, sin esto el drill mostraría solo el código).
            var canales = new Dictionary<int, string>();
            using (var cmd = new SqlCommand("SELECT CanID, CanDescripcion FROM CanalDistribucion WITH (NOLOCK) WHERE CanDescripcion IS NOT NULL", cn) { CommandTimeout = 30 })
            {
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) canales[rd.GetInt32(0)] = rd.GetString(1);
            }

            // Dimensiones por Rep (Centro/Canal/Supervisor/LipCodigo) — mismo patrón que Filtros().
            var dims = new Dictionary<string, (string nombre, string cedCod, string centro, string supCod, string supNom, int canId, string lipCodigo)>();
            using (var cmd = new SqlCommand(@"
SELECT r.RepCodigo, r.RepNombre, ISNULL(r.CedCodigo,''), ISNULL(cd.CedDescripcion,ISNULL(r.CedCodigo,'(Sin centro)')),
       ISNULL(r.RepSupervisor,''), ISNULL(sup.RepNombre,r.RepSupervisor), ISNULL(ra.CanID,0), ISNULL(ra.LipCodigo,'')
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo", cn) { CommandTimeout = 30 })
            {
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    dims[rd.GetString(0)] = (rd.IsDBNull(1) ? "" : rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.IsDBNull(5) ? "" : rd.GetString(5), rd.GetInt32(6), rd.GetString(7));
            }

            // Precio REAL por Rep×Producto (2026-08-30, corrección pedida explícitamente por
            // el usuario: el RD$ NO se estima con un precio promedio de ventas — se calcula
            // EXACTO igual que el proceso real que arma PreTipo='MNTDPROD': unidades × el
            // precio de la Lista de Precios que le corresponde al CANAL/lista de ESE
            // vendedor (RepresentantesAdicionales.LipCodigo → ListaPrecios.LipCodigo+ProID).
            // Verificado en vivo contra producción: unidades(UNDDPROD) × este precio
            // reproduce EXACTO (a centavos) el monto ya guardado en PreTipo='MNTDPROD' para
            // Agosto 2026. Dos vendedores con el mismo producto pero distinto canal
            // (ej. Minorista vs Mayorista) llevan precios distintos — por eso el precio se
            // resuelve por (Rep,Producto), no solo por Producto.
            var lipCodigosDistintos = todasLasClaves.Select(k => dims.GetValueOrDefault(k.rep).lipCodigo).Where(l => !string.IsNullOrEmpty(l)).Distinct().ToList();
            var listaPrecios = new Dictionary<(string lip, int proId), double>();
            if (lipCodigosDistintos.Count > 0 && proIds.Count > 0)
            {
                var parmsLp = new Dictionary<string, object>();
                var lipNames = lipCodigosDistintos.Select((l, i) => { var n = $"@lip{i}"; parmsLp[n] = l; return n; }).ToList();
                var proNames = proIds.Select((p, i) => { var n = $"@plp{i}"; parmsLp[n] = p; return n; }).ToList();
                using var cmdLp = new SqlCommand($@"
SELECT LipCodigo, ProID, LipPrecio FROM ListaPrecios WITH (NOLOCK)
WHERE LipCodigo IN ({string.Join(",", lipNames)}) AND ProID IN ({string.Join(",", proNames)})", cn) { CommandTimeout = 60 };
                foreach (var kv in parmsLp) cmdLp.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rdLp = cmdLp.ExecuteReader();
                while (rdLp.Read())
                    listaPrecios[(rdLp.GetString(0), rdLp.GetInt32(1))] = Convert.ToDouble(rdLp.GetValue(2));
            }
            double PrecioLista(string rep, int proId) => listaPrecios.GetValueOrDefault((dims.GetValueOrDefault(rep).lipCodigo, proId), 0);

            // Grano Rep×Producto (2026-08-30, pedido explícito del usuario: el drill debe
            // poder bajar hasta "el representante Y el producto", no quedarse a nivel
            // Rep). Antes esta lista se colapsaba a nivel Rep (porRep) — ahora cada fila
            // de 'filas' (ya agregada por SQL sobre los N meses destino) se emite tal
            // cual, con las dimensiones de Rep Y de Producto adjuntas; TODO el drill
            // (Centro/Canal/Supervisor/Vendedor/Suplidor/Categoría/Producto) se arma
            // 100% client-side sobre este único fetch — mismo patrón que
            // ApiPresupuestosController.DrillOrganizacion/DrillProducto.
            //
            // FIX "peras con peras" (pedido explícito del usuario 2026-08-30): el lado
            // Propuesto (siempre SUMA de los N meses destino) debe promediarse SIEMPRE
            // por nMesesDestino para ser comparable mes a mes — antes solo se promediaba
            // cuando había compAnio/compMes, dejando "Mismo período (ya publicado)" como
            // una SUMA de los N meses mientras Sugerido/Ventas Promedio ya estaban en
            // promedio mensual (variación % completamente inflada). El lado Comparación
            // tiene 2 casos distintos:
            //  - compEspecifico dado: el valor YA es el de UN solo mes (el mes elegido)
            //    — no se divide de nuevo.
            //  - sin compAnio/compMes: es SUM(PrePresupuestoActual) de los N meses destino
            //    (cada fila de Staging trae su propio snapshot por mes) — SÍ hay que
            //    promediarlo igual que Propuesto.
            // filtrandoMes (2026-09-01): 'filas' ya viene restringida a UN solo mes desde
            // el SQL de arriba (no es SUM de los N meses destino) — no hay nada que
            // promediar, así que ambos divisores pasan a 1 y ya no es "promedio mensual"
            // sino la cifra exacta de ese mes.
            bool esPromedioMensual = !filtrandoMes;
            double divisorProp = filtrandoMes ? 1 : nMesesDestino;
            double divisorComp = filtrandoMes ? 1 : (compEspecifico != null ? 1 : nMesesDestino);
            var resultado = todasLasClaves.Select(clave =>
            {
                var (rep, proId) = clave;
                var d = dims.GetValueOrDefault(rep);
                var pd = prodDims.GetValueOrDefault(proId);
                double caja = unidadesPorCaja.GetValueOrDefault(proId, 1); if (caja <= 0) caja = 1;
                double precioLista = PrecioLista(rep, proId);
                var (propuesto, actualMismoPeriodo) = propuestoPorPar.GetValueOrDefault(clave);
                double compUnidades = (compEspecifico != null ? compEspecifico.GetValueOrDefault(clave, 0) : actualMismoPeriodo) / divisorComp;
                double compMonto = montoReal.GetValueOrDefault(clave, 0) / divisorComp;
                double promUnidades = ventaPromedio.GetValueOrDefault(clave, 0);
                double unidadesProp = propuesto / divisorProp;
                // Monto: precio de Lista (unidades × Lista del canal del rep) SOLO para
                // Sugerido, porque son unidades futuras sin venta real todavía — pedido
                // explícito del usuario 2026-08-30, verificado en vivo que unidades × este
                // precio reproduce exacto lo guardado en MNTDPROD. Comparación usa el
                // MNTDPROD ya publicado (no estimado). Ventas Promedio usa el MontoNeto
                // REAL (2026-08-31, corrección — ver comentario arriba de 'ventaPromedioMonto').
                // 'unidadesProp'=0 en los pares que el motor no llegó a proponer (fuera de su
                // base histórica) pero que SÍ tienen presupuesto/venta real — antes esos pares
                // ni siquiera aparecían, ahora aparecen con Sugerido=0 (fix 2026-08-31).
                return new FilaComparativo(
                    rep, d.nombre, d.cedCod, d.centro,
                    d.supCod, d.supNom, d.canId, canales.GetValueOrDefault(d.canId, d.canId == 0 ? "(Sin canal)" : $"Canal {d.canId}"),
                    proId, pd.nombre, pd.cat1Id, pd.cat1Nombre, pd.cat3Id, pd.cat3Nombre,
                    unidadesProp, unidadesProp / caja, unidadesProp * precioLista,
                    compUnidades, compUnidades / caja, compMonto,
                    promUnidades, promUnidades / caja, ventaPromedioMonto.GetValueOrDefault(clave, 0));
            }).ToList();

            return (true, null, esPromedioMensual, filtrandoMes ? 1 : nMesesDestino, resultado);
    }

    // Confirma que el lote existe, está en 'Borrador' (no se edita un lote ya
    // publicado/descartado) y devuelve su LoteId real — usado por los 3
    // endpoints de ajuste manual de abajo.
    private (bool ok, string? error) ValidarLoteEditable(SqlConnection cn, Guid loteId)
    {
        using var cmd = new SqlCommand("SELECT Estado FROM PresupuestoLotes_RM WHERE LoteId=@id", cn);
        cmd.Parameters.AddWithValue("@id", loteId);
        var v = cmd.ExecuteScalar();
        if (v == null) return (false, "Lote no encontrado.");
        var estado = v.ToString();
        if (estado != "Borrador") return (false, $"El lote está en estado '{estado}' — solo se puede ajustar un lote en 'Borrador'.");
        return (true, null);
    }

    public sealed record ItemValor(int id, double valor);
    public sealed record ActualizarValoresReq(Guid loteId, List<ItemValor> items);

    // ── POST /apipresupuestogestion/staging/actualizarvalores ───────────────
    // Ajuste manual de 1 o muchas filas de un lote en borrador — usado tanto
    // por la edición en pantalla (1 item) como por la reimportación de Excel
    // (N items, mismo endpoint). No recalcula anomalías: es un ajuste
    // consciente del usuario sobre lo que el motor propuso.
    [HttpPost("staging/actualizarvalores")]
    public IActionResult ActualizarValores([FromBody] ActualizarValoresReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            var (ok, error) = ValidarLoteEditable(cn, req.loteId);
            if (!ok) return BadRequest(new { ok = false, error });

            using var cmd = new SqlCommand(@"
UPDATE PresupuestoStaging_RM SET PrePresupuestoPropuesto=@valor
WHERE Id=@itemId AND LoteId=@loteId", cn) { CommandTimeout = 60 };
            var pValor = cmd.Parameters.Add("@valor", System.Data.SqlDbType.Decimal); pValor.Precision = 18; pValor.Scale = 4;
            var pId = cmd.Parameters.Add("@itemId", System.Data.SqlDbType.Int);
            var pLote = cmd.Parameters.Add("@loteId", System.Data.SqlDbType.UniqueIdentifier); pLote.Value = req.loteId;
            int actualizados = 0;
            foreach (var item in req.items ?? new())
            {
                pValor.Value = Math.Round(item.valor, 4); pId.Value = item.id;
                actualizados += cmd.ExecuteNonQuery();
            }
            return Ok(new { ok = true, actualizados });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record IdReq(Guid loteId, int id);

    // ── POST /apipresupuestogestion/staging/eliminarfila ────────────────────
    [HttpPost("staging/eliminarfila")]
    public IActionResult EliminarFila([FromBody] IdReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            var (ok, error) = ValidarLoteEditable(cn, req.loteId);
            if (!ok) return BadRequest(new { ok = false, error });
            using var cmd = new SqlCommand("DELETE FROM PresupuestoStaging_RM WHERE Id=@id AND LoteId=@loteId", cn);
            cmd.Parameters.AddWithValue("@id", req.id);
            cmd.Parameters.AddWithValue("@loteId", req.loteId);
            int n = cmd.ExecuteNonQuery();
            return Ok(new { ok = true, eliminado = n > 0 });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record AgregarFilaReq(Guid loteId, string repCodigo, string preReferencia, int preAnio, int preMes, double valor);

    // ── POST /apipresupuestogestion/staging/agregarfila ─────────────────────
    // Agrega a mano una fila que el motor no generó (ej. producto sin
    // historial que se quiere presupuestar desde cero). PreTipo se toma del
    // lote (v1: siempre 'Unddprod'); PreAnio/PreMes deben ser uno de los
    // meses del "Rango Destino" con que se generó el lote.
    [HttpPost("staging/agregarfila")]
    public IActionResult AgregarFila([FromBody] AgregarFilaReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            var (ok, error) = ValidarLoteEditable(cn, req.loteId);
            if (!ok) return BadRequest(new { ok = false, error });

            string preTipo;
            using (var cmdT = new SqlCommand("SELECT PreTiposIncluidos, DestinoDetalle FROM PresupuestoLotes_RM WHERE LoteId=@id", cn))
            {
                cmdT.Parameters.AddWithValue("@id", req.loteId);
                using var rd = cmdT.ExecuteReader();
                if (!rd.Read()) return NotFound(new { ok = false, error = "Lote no encontrado." });
                preTipo = rd.GetString(0).Split(',')[0];
                var destinos = rd.GetString(1).Split(',');
                if (!destinos.Contains($"{req.preAnio}-{req.preMes:00}"))
                    return BadRequest(new { ok = false, error = $"{req.preAnio}-{req.preMes:00} no está en el Rango Destino de este lote." });
            }

            var actualDict = PresupuestoBuilderCore.ObtenerPresupuestoActual(cn, DestinoDb, preTipo, req.preAnio, req.preMes, new[] { req.repCodigo });
            actualDict.TryGetValue((req.repCodigo, req.preReferencia), out var actual);

            using var cmd = new SqlCommand(@"
INSERT INTO PresupuestoStaging_RM(LoteId,RepCodigo,PreTipo,PreAnio,PreMes,PreReferencia,PrePresupuestoPropuesto,PrePresupuestoActual,Anomalia,AnomaliaDetalle)
OUTPUT INSERTED.Id
VALUES(@lote,@rep,@tipo,@anio,@mes,@ref,@valor,@actual,0,'Agregado manualmente')", cn);
            cmd.Parameters.AddWithValue("@lote", req.loteId);
            cmd.Parameters.AddWithValue("@rep", req.repCodigo);
            cmd.Parameters.AddWithValue("@tipo", preTipo);
            cmd.Parameters.AddWithValue("@anio", req.preAnio);
            cmd.Parameters.AddWithValue("@mes", req.preMes);
            cmd.Parameters.AddWithValue("@ref", req.preReferencia);
            cmd.Parameters.AddWithValue("@valor", Math.Round(req.valor, 4));
            cmd.Parameters.AddWithValue("@actual", (object?)actual ?? DBNull.Value);
            var nuevoId = (int)cmd.ExecuteScalar();
            return Ok(new { ok = true, id = nuevoId });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apipresupuestogestion/lote/{id}/ajustar-nodo ──────────────────
    // Redistribución en CUALQUIER nodo del drill (pedido explícito del usuario
    // 2026-08-30): parado en Compañía/Centro/Canal/Supervisor/Vendedor/Suplidor/
    // Categoría/Producto, decir "esto debería ser X" (valor RD$ o unidades — el
    // front decide qué manda en 'monto') o "esto debería crecer/bajar a Y%" — la
    // diferencia se reparte PROPORCIONALMENTE (mismo peso relativo que ya tenía
    // cada fila) SOLO entre las filas de ese nodo, sin tocar el resto del lote.
    // 'ambito' vacío = toda la compañía (igual que 'montoTotalSugerido' en
    // Generar, pero aplicable DESPUÉS de generado, sin tener que rehacer el lote).
    // Institucional (CanID=7000) queda SIEMPRE fuera salvo que el ámbito pedido
    // sea explícitamente ese canal — mismo criterio que en Generar.
    public sealed record AmbitoNodo(string? cedCodigo, int? canId, string? supCod, string? repCodigo, int? cat3Id, int? cat1Id, int? proId);
    // anio/mes (2026-09-01, pedido explícito: "no quiero dejar a medias sin saber si
    // puedo ajustar otros meses por separado") — opcionales; si vienen, el ajuste se
    // aplica SOLO a ese mes destino del lote (no reparte entre todos los meses).
    public sealed record AjustarNodoReq(AmbitoNodo? ambito, string modo, double monto, int? anio = null, int? mes = null);

    [HttpPost("lote/{loteId:guid}/ajustar-nodo")]
    public IActionResult AjustarNodo(Guid loteId, [FromBody] AjustarNodoReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
        try
        {
            using var cn = NewConn(); cn.Open();
            var (ok, error) = ValidarLoteEditable(cn, loteId);
            if (!ok) return BadRequest(new { ok = false, error });

            var a = req.ambito ?? new AmbitoNodo(null, null, null, null, null, null, null);
            bool filtrandoMes = req.anio is > 0 && req.mes is >= 1 and <= 12;
            var parms = new Dictionary<string, object> { ["@lote"] = loteId };
            var filtros = new List<string>();
            if (a.canId is not 7000) filtros.Add("ISNULL(ra.CanID,0) <> 7000");
            if (a.cedCodigo != null) { filtros.Add("r.CedCodigo=@ced"); parms["@ced"] = a.cedCodigo; }
            if (a.canId != null) { filtros.Add("ISNULL(ra.CanID,0)=@can"); parms["@can"] = a.canId.Value; }
            if (a.supCod != null) { filtros.Add("r.RepSupervisor=@sup"); parms["@sup"] = a.supCod; }
            if (a.repCodigo != null) { filtros.Add("s.RepCodigo=@rep"); parms["@rep"] = a.repCodigo; }
            if (a.proId != null) { filtros.Add("TRY_CAST(s.PreReferencia AS INT)=@pro"); parms["@pro"] = a.proId.Value; }
            if (a.cat1Id != null) { filtros.Add("pr.Cat1ID=@cat1"); parms["@cat1"] = a.cat1Id.Value; }
            if (a.cat3Id != null) { filtros.Add("pr.Cat3ID=@cat3"); parms["@cat3"] = a.cat3Id.Value; }
            if (filtrandoMes) { filtros.Add("s.PreAnio=@fAnio"); filtros.Add("s.PreMes=@fMes"); parms["@fAnio"] = req.anio!.Value; parms["@fMes"] = req.mes!.Value; }
            string whereExtra = filtros.Count > 0 ? " AND " + string.Join(" AND ", filtros) : "";

            var valoresPrevios = new Dictionary<int, double>();
            using (var cmd = new SqlCommand($@"
SELECT s.Id, s.PrePresupuestoPropuesto
FROM PresupuestoStaging_RM s
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=s.RepCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=s.RepCodigo
LEFT JOIN Productos pr WITH (NOLOCK) ON pr.ProID=TRY_CAST(s.PreReferencia AS INT)
WHERE s.LoteId=@lote{whereExtra}", cn) { CommandTimeout = 60 })
            {
                foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) valoresPrevios[rd.GetInt32(0)] = Convert.ToDouble(rd.GetValue(1));
            }
            if (valoresPrevios.Count == 0) return BadRequest(new { ok = false, error = "Ningún renglón del lote cae dentro de ese ámbito." });

            double totalActual = valoresPrevios.Values.Sum();
            double objetivo = string.Equals(req.modo, "pct", StringComparison.OrdinalIgnoreCase) ? totalActual * (req.monto / 100.0) : req.monto;
            double escalaNodo = totalActual > 0 ? objetivo / totalActual : 0;

            var cacheUmbral = PresupuestoBuilderCore.CargarVariable(cn, "UMBRAL_ANOMALIA_PCT", DateTime.Now.Year);
            double umbral = PresupuestoBuilderCore.ResolverVariableEnMemoria(cacheUmbral, DateTime.Now.Month, null, null, null, 50.0);

            int actualizados = 0, anomaliasCount = 0;
            using (var cmdUpd = new SqlCommand(@"
UPDATE PresupuestoStaging_RM SET PrePresupuestoPropuesto=@nuevo, Anomalia=@anom, AnomaliaDetalle=@det
WHERE Id=@id AND LoteId=@lote", cn) { CommandTimeout = 60 })
            {
                var pNuevo = cmdUpd.Parameters.Add("@nuevo", System.Data.SqlDbType.Decimal); pNuevo.Precision = 18; pNuevo.Scale = 4;
                var pAnom = cmdUpd.Parameters.Add("@anom", System.Data.SqlDbType.Bit);
                var pDet = cmdUpd.Parameters.Add("@det", System.Data.SqlDbType.NVarChar, 400);
                var pId = cmdUpd.Parameters.Add("@id", System.Data.SqlDbType.Int);
                var pLote = cmdUpd.Parameters.Add("@lote", System.Data.SqlDbType.UniqueIdentifier); pLote.Value = loteId;
                foreach (var (id, previo) in valoresPrevios)
                {
                    double nuevo = Math.Round(previo * escalaNodo, 4);
                    var (anomalia, detalle) = PresupuestoBuilderCore.EvaluarAnomalia(previo, nuevo, umbral);
                    pNuevo.Value = nuevo; pAnom.Value = anomalia;
                    pDet.Value = (object?)(anomalia ? $"Ajuste manual por nodo: {detalle}" : null) ?? DBNull.Value;
                    pId.Value = id;
                    actualizados += cmdUpd.ExecuteNonQuery();
                    if (anomalia) anomaliasCount++;
                }
            }

            using (var cmdNota = new SqlCommand(@"
UPDATE PresupuestoLotes_RM SET Descripcion = CONCAT(ISNULL(Descripcion,''),
  CASE WHEN Descripcion IS NULL OR Descripcion='' THEN '' ELSE ' · ' END,
  @nota) WHERE LoteId=@lote", cn))
            {
                string ambitoTxt = string.Join(", ", new[] {
                    a.cedCodigo != null ? $"Centro={a.cedCodigo}" : null, a.canId != null ? $"Canal={a.canId}" : null,
                    a.supCod != null ? $"Supervisor={a.supCod}" : null, a.repCodigo != null ? $"Rep={a.repCodigo}" : null,
                    a.cat3Id != null ? $"Suplidor={a.cat3Id}" : null, a.cat1Id != null ? $"Categoría={a.cat1Id}" : null, a.proId != null ? $"Producto={a.proId}" : null,
                }.Where(s => s != null));
                string mesTxt = filtrandoMes ? $" [Mes={req.anio}-{req.mes:00}]" : "";
                cmdNota.Parameters.AddWithValue("@nota", $"Ajuste manual [{(string.IsNullOrEmpty(ambitoTxt) ? "Compañía" : ambitoTxt)}]{mesTxt} por {user}: {req.modo}={req.monto:0.##} (escala={escalaNodo:0.####})");
                cmdNota.Parameters.AddWithValue("@lote", loteId);
                cmdNota.ExecuteNonQuery();
            }

            return Ok(new { ok = true, filasActualizadas = actualizados, anomalias = anomaliasCount, escala = escalaNodo, totalAnterior = totalActual, totalNuevo = objetivo });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Vista de Activaciones en "3. Análisis comparativo" (2026-09-01, Fase C
    // del plan de rediseño). Reusa el mismo `AmbitoNodo` de `AjustarNodo` de
    // arriba — mismo shape de filtro (Centro/Canal/Supervisor/Vendedor/
    // Suplidor/Categoría/Producto), pero aplicado a `clientesDetalle`+
    // `VWVentas` en vez de `PresupuestoStaging_RM` (no existía ningún método
    // que agregara clientes distintos por Supervisor/Canal/Centro — solo por
    // RepCodigo individual, verificado antes de escribir esto).
    // ══════════════════════════════════════════════════════════════════════

    // Filtro organizacional (Centro/Canal/Supervisor/Vendedor) sobre
    // clientesDetalle/Representantes/RepresentantesAdicionales — MISMA regla
    // de Institucional que AjustarNodo (excluido salvo que se pida explícito).
    private static (string whereOrg, Dictionary<string, object> parms) FiltroOrganizacionalClientes(AmbitoNodo a)
    {
        var parms = new Dictionary<string, object>();
        var filtros = new List<string>();
        if (a.canId is not 7000) filtros.Add("ISNULL(ra.CanID,0) <> 7000");
        if (a.cedCodigo != null) { filtros.Add("r.CedCodigo=@ced"); parms["@ced"] = a.cedCodigo; }
        if (a.canId != null) { filtros.Add("ISNULL(ra.CanID,0)=@can"); parms["@can"] = a.canId.Value; }
        if (a.supCod != null) { filtros.Add("r.RepSupervisor=@sup"); parms["@sup"] = a.supCod; }
        if (a.repCodigo != null) { filtros.Add("cd.RepCodigo=@rep"); parms["@rep"] = a.repCodigo; }
        return (filtros.Count > 0 ? " AND " + string.Join(" AND ", filtros) : "", parms);
    }

    // Clientes Asignados (cartera) = clientes DISTINTOS en clientesDetalle de
    // reps PreVendedor/Vendedor en el ámbito organizacional — si el ámbito
    // trae Suplidor/Categoría/Producto, se restringe además a clientes cuyo
    // rep tenga ESE alcance en su universo (RepresentantesGrupoProductos).
    private int ContarClientesAsignados(SqlConnection cn, AmbitoNodo a)
    {
        var (whereOrg, parms) = FiltroOrganizacionalClientes(a);
        string whereProd = "";
        if (a.proId != null)
        {
            whereProd = @" AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp
                INNER JOIN GrupoProductosDetalle gpd ON gpd.GrpCodigo=rgp.GrpCodigo
                WHERE rgp.RepCodigo=cd.RepCodigo AND gpd.ProID=@pro)";
            parms["@pro"] = a.proId.Value;
        }
        else if (a.cat3Id != null || a.cat1Id != null)
        {
            var sub = new List<string>();
            if (a.cat3Id != null) { sub.Add("pr2.Cat3ID=@cat3"); parms["@cat3"] = a.cat3Id.Value; }
            if (a.cat1Id != null) { sub.Add("pr2.Cat1ID=@cat1"); parms["@cat1"] = a.cat1Id.Value; }
            whereProd = $@" AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp
                INNER JOIN GrupoProductosDetalle gpd ON gpd.GrpCodigo=rgp.GrpCodigo
                INNER JOIN Productos pr2 ON pr2.ProID=gpd.ProID
                WHERE rgp.RepCodigo=cd.RepCodigo AND {string.Join(" AND ", sub)})";
        }
        using var cmd = new SqlCommand($@"
SELECT COUNT(DISTINCT cd.CliID)
FROM clientesDetalle cd WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
WHERE 1=1{whereOrg}{whereProd}", cn) { CommandTimeout = 60 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        return (int)cmd.ExecuteScalar();
    }

    // Clientes Efectivos (activos) = clientes DISTINTOS que SÍ compraron algo
    // > 0 en el Rango Base del lote, dentro del mismo ámbito — reusa el mismo
    // WHERE que CalcularBaseHistorica (clientesDetalle + RepCargo + universo
    // vía EXISTS), pero contando CliID en vez de sumar monto/unidades.
    private int ContarClientesEfectivos(SqlConnection cn, AmbitoNodo a, DateTime desde, DateTime hastaExclusive)
    {
        var (whereOrg, parms) = FiltroOrganizacionalClientes(a);
        parms["@desde"] = desde; parms["@hasta"] = hastaExclusive;
        string whereProd = "";
        if (a.proId != null) { whereProd = " AND d.ProID=@pro"; parms["@pro"] = a.proId.Value; }
        else
        {
            var sub = new List<string>();
            if (a.cat3Id != null) { sub.Add("pr.Cat3ID=@cat3"); parms["@cat3"] = a.cat3Id.Value; }
            if (a.cat1Id != null) { sub.Add("pr.Cat1ID=@cat1"); parms["@cat1"] = a.cat1Id.Value; }
            if (sub.Count > 0) whereProd = " AND " + string.Join(" AND ", sub);
        }
        using var cmd = new SqlCommand($@"
SELECT COUNT(DISTINCT v.CliID)
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN clientesDetalle cd WITH (NOLOCK) ON cd.CliID = v.CliID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
LEFT JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
WHERE v.VenEstatus<>0 AND v.VenFecha>=@desde AND v.VenFecha<@hasta AND d.VenCantidad>0
  AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp WITH (NOLOCK) INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo=rgp.GrpCodigo WHERE rgp.RepCodigo=cd.RepCodigo AND gpd.ProID=d.ProID)
  {whereOrg}{whereProd}", cn) { CommandTimeout = 60 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        return (int)cmd.ExecuteScalar();
    }

    // ── Conteos BATCHED para el Excel de revisión (2026-09-02) ──────────────
    // Antes, las 4 hojas llamaban ContarClientesAsignados/ContarClientesEfectivos
    // UNA VEZ POR FILA (por cada Supervisor/Vendedor/Suplidor/Categoría) — con
    // decenas o cientos de grupos, son cientos de queries secuenciales contra
    // producción; bajo carga alguna terminó superando el CommandTimeout=60s y
    // tumbó la descarga entera ("Tiempo de espera de ejecución agotado").
    // Fix: 1 sola query GROUP BY por dimensión (8 queries fijas en total, sin
    // importar cuántos vendedores/suplidores haya), reusando exactamente el
    // mismo WHERE que ContarClientesAsignados/ContarClientesEfectivos.
    private Dictionary<string, int> ClientesAsignadosPorOrg(SqlConnection cn, string campoGroupBy)
    {
        var dict = new Dictionary<string, int>();
        using var cmd = new SqlCommand($@"
SELECT {campoGroupBy}, COUNT(DISTINCT cd.CliID)
FROM clientesDetalle cd WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
WHERE ISNULL(ra.CanID,0) <> 7000 AND {campoGroupBy} IS NOT NULL
GROUP BY {campoGroupBy}", cn) { CommandTimeout = 120 };
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) dict[rd.GetValue(0).ToString()!] = rd.GetInt32(1);
        return dict;
    }

    private Dictionary<string, int> ClientesEfectivosPorOrg(SqlConnection cn, string campoGroupBy, DateTime desde, DateTime hastaExclusive)
    {
        var dict = new Dictionary<string, int>();
        using var cmd = new SqlCommand($@"
SELECT {campoGroupBy}, COUNT(DISTINCT v.CliID)
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN clientesDetalle cd WITH (NOLOCK) ON cd.CliID = v.CliID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
WHERE v.VenEstatus<>0 AND v.VenFecha>=@desde AND v.VenFecha<@hasta AND d.VenCantidad>0
  AND ISNULL(ra.CanID,0) <> 7000 AND {campoGroupBy} IS NOT NULL
  AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp WITH (NOLOCK) INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo=rgp.GrpCodigo WHERE rgp.RepCodigo=cd.RepCodigo AND gpd.ProID=d.ProID)
GROUP BY {campoGroupBy}", cn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@desde", desde); cmd.Parameters.AddWithValue("@hasta", hastaExclusive);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) dict[rd.GetValue(0).ToString()!] = rd.GetInt32(1);
        return dict;
    }

    // Total de TODO el lote (hoja Resumen, sin nodo por fila) — bug real reportado por el usuario
    // 2026-09-06 al descargar el Excel: "Cada expresión GROUP BY debe contener al menos una
    // columna que no sea una referencia externa". Causa: ClientesEfectivosPorOrg/FacturasPorOrg se
    // llamaban con el literal "'TOTAL'" como si fuera una columna real — al sustituirlo, quedaba
    // `GROUP BY 'TOTAL'` (agrupar por una CONSTANTE, no una columna) dentro de una consulta que
    // además tiene un EXISTS correlacionado (mismo patrón exacto que ya rompió
    // TorrePersonalizados.Calcular en la 2ª parte del 2026-09-03: SQL Server rechaza agrupar por un
    // literal cuando se combina con ciertas subconsultas). Fix: método dedicado, SIN GROUP BY (una
    // consulta que solo tiene un COUNT(DISTINCT...) ya colapsa sola a 1 fila, agrupar por una
    // constante nunca aportaba nada).
    private int ClientesEfectivosTotal(SqlConnection cn, DateTime desde, DateTime hastaExclusive)
    {
        using var cmd = new SqlCommand($@"
SELECT COUNT(DISTINCT v.CliID)
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN clientesDetalle cd WITH (NOLOCK) ON cd.CliID = v.CliID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
WHERE v.VenEstatus<>0 AND v.VenFecha>=@desde AND v.VenFecha<@hasta AND d.VenCantidad>0
  AND ISNULL(ra.CanID,0) <> 7000
  AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp WITH (NOLOCK) INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo=rgp.GrpCodigo WHERE rgp.RepCodigo=cd.RepCodigo AND gpd.ProID=d.ProID)", cn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@desde", desde); cmd.Parameters.AddWithValue("@hasta", hastaExclusive);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private Dictionary<int, int> ClientesAsignadosPorProducto(SqlConnection cn, bool porCat1)
    {
        string campo = porCat1 ? "pr2.Cat1ID" : "pr2.Cat3ID";
        var dict = new Dictionary<int, int>();
        using var cmd = new SqlCommand($@"
SELECT {campo}, COUNT(DISTINCT cd.CliID)
FROM clientesDetalle cd WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
INNER JOIN RepresentantesGrupoProductos rgp WITH (NOLOCK) ON rgp.RepCodigo=cd.RepCodigo
INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo=rgp.GrpCodigo
INNER JOIN Productos pr2 WITH (NOLOCK) ON pr2.ProID=gpd.ProID
WHERE ISNULL(ra.CanID,0) <> 7000 AND {campo} IS NOT NULL
GROUP BY {campo}", cn) { CommandTimeout = 120 };
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) dict[rd.GetInt32(0)] = rd.GetInt32(1);
        return dict;
    }

    private Dictionary<int, int> ClientesEfectivosPorProducto(SqlConnection cn, bool porCat1, DateTime desde, DateTime hastaExclusive)
    {
        string campo = porCat1 ? "pr.Cat1ID" : "pr.Cat3ID";
        var dict = new Dictionary<int, int>();
        using var cmd = new SqlCommand($@"
SELECT {campo}, COUNT(DISTINCT v.CliID)
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN clientesDetalle cd WITH (NOLOCK) ON cd.CliID = v.CliID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
WHERE v.VenEstatus<>0 AND v.VenFecha>=@desde AND v.VenFecha<@hasta AND d.VenCantidad>0
  AND ISNULL(ra.CanID,0) <> 7000 AND {campo} IS NOT NULL
  AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp WITH (NOLOCK) INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo=rgp.GrpCodigo WHERE rgp.RepCodigo=cd.RepCodigo AND gpd.ProID=d.ProID)
GROUP BY {campo}", cn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@desde", desde); cmd.Parameters.AddWithValue("@hasta", hastaExclusive);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) dict[rd.GetInt32(0)] = rd.GetInt32(1);
        return dict;
    }

    // Facturas (PedSecuencia DISTINTAS) por Org/Producto — mismo criterio y mismo par de
    // funciones genéricas que ClientesEfectivosPorOrg/Producto (2026-09-06, pedido explícito del
    // usuario: "Facturas promedio actuales" en el Excel/pantalla de revisión, junto a
    // Activaciones) — mismo patrón COUNT(DISTINCT PedSecuencia) ya usado para Rappel/Factura en
    // PresupuestoBuilderCore.cs, pero agregado por Org/Producto (no por rep) para calzar con las
    // hojas de este endpoint.
    private Dictionary<string, int> FacturasPorOrg(SqlConnection cn, string campoGroupBy, DateTime desde, DateTime hastaExclusive)
    {
        var dict = new Dictionary<string, int>();
        using var cmd = new SqlCommand($@"
SELECT {campoGroupBy}, COUNT(DISTINCT d.PedSecuencia)
FROM VWPedidos p WITH (NOLOCK)
INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=p.PedSecuencia AND d.RepCodigo=p.RepCodigo
INNER JOIN clientesDetalle cd WITH (NOLOCK) ON cd.CliID = p.CliID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
WHERE p.PedFecha>=@desde AND p.PedFecha<@hasta
  AND ISNULL(ra.CanID,0) <> 7000 AND {campoGroupBy} IS NOT NULL
  AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp WITH (NOLOCK) INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo=rgp.GrpCodigo WHERE rgp.RepCodigo=cd.RepCodigo AND gpd.ProID=d.ProID)
GROUP BY {campoGroupBy}", cn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@desde", desde); cmd.Parameters.AddWithValue("@hasta", hastaExclusive);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) dict[rd.GetValue(0).ToString()!] = rd.GetInt32(1);
        return dict;
    }

    // Ver comentario de ClientesEfectivosTotal (mismo bug real, mismo fix: sin GROUP BY sobre
    // un literal cuando solo hace falta el total de TODO el lote).
    private int FacturasTotal(SqlConnection cn, DateTime desde, DateTime hastaExclusive)
    {
        using var cmd = new SqlCommand($@"
SELECT COUNT(DISTINCT d.PedSecuencia)
FROM VWPedidos p WITH (NOLOCK)
INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=p.PedSecuencia AND d.RepCodigo=p.RepCodigo
INNER JOIN clientesDetalle cd WITH (NOLOCK) ON cd.CliID = p.CliID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
WHERE p.PedFecha>=@desde AND p.PedFecha<@hasta
  AND ISNULL(ra.CanID,0) <> 7000
  AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp WITH (NOLOCK) INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo=rgp.GrpCodigo WHERE rgp.RepCodigo=cd.RepCodigo AND gpd.ProID=d.ProID)", cn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@desde", desde); cmd.Parameters.AddWithValue("@hasta", hastaExclusive);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private Dictionary<int, int> FacturasPorProducto(SqlConnection cn, bool porCat1, DateTime desde, DateTime hastaExclusive)
    {
        string campo = porCat1 ? "pr.Cat1ID" : "pr.Cat3ID";
        var dict = new Dictionary<int, int>();
        using var cmd = new SqlCommand($@"
SELECT {campo}, COUNT(DISTINCT d.PedSecuencia)
FROM VWPedidos p WITH (NOLOCK)
INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=p.PedSecuencia AND d.RepCodigo=p.RepCodigo
INNER JOIN clientesDetalle cd WITH (NOLOCK) ON cd.CliID = p.CliID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
WHERE p.PedFecha>=@desde AND p.PedFecha<@hasta
  AND ISNULL(ra.CanID,0) <> 7000 AND {campo} IS NOT NULL
  AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp WITH (NOLOCK) INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo=rgp.GrpCodigo WHERE rgp.RepCodigo=cd.RepCodigo AND gpd.ProID=d.ProID)
GROUP BY {campo}", cn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@desde", desde); cmd.Parameters.AddWithValue("@hasta", hastaExclusive);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) dict[rd.GetInt32(0)] = rd.GetInt32(1);
        return dict;
    }

    private (DateTime baseIni, DateTime baseFinExcl) ObtenerRangoBaseLote(SqlConnection cn, Guid loteId)
    {
        using var cmd = new SqlCommand("SELECT BaseFechaInicio, BaseFechaFin FROM PresupuestoLotes_RM WHERE LoteId=@id", cn);
        cmd.Parameters.AddWithValue("@id", loteId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) throw new InvalidOperationException("Lote no encontrado.");
        return (rd.GetDateTime(0), rd.GetDateTime(1).AddDays(1));
    }

    public sealed record DetalleActivacionReq(AmbitoNodo? ambito);

    // ── POST /apipresupuestogestion/lote/{id}/detalle-activacion ────────────
    [HttpPost("lote/{loteId:guid}/detalle-activacion")]
    public IActionResult DetalleActivacion(Guid loteId, [FromBody] DetalleActivacionReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            var (baseIni, baseFinExcl) = ObtenerRangoBaseLote(cn, loteId);
            var a = req.ambito ?? new AmbitoNodo(null, null, null, null, null, null, null);
            int asignados = ContarClientesAsignados(cn, a);
            int efectivos = ContarClientesEfectivos(cn, a, baseIni, baseFinExcl);
            double pct = asignados > 0 ? (double)efectivos / asignados : 0;
            return Ok(new { ok = true, clientesAsignados = asignados, clientesEfectivos = efectivos, pctActivacion = pct });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // anio/mes (2026-09-01): opcionales — si vienen, las unidades agregadas caen
    // SOLO en el mes destino indicado (mismo criterio que AjustarNodoReq).
    public sealed record RecalcularActivacionReq(AmbitoNodo? ambito, string modo, double valor, int? anio = null, int? mes = null);

    // ── POST /apipresupuestogestion/lote/{id}/recalcular-activacion ─────────
    // Decisión confirmada con el usuario (AskUserQuestion 2026-09-01, lo más
    // CONSERVADOR posible): el incremento de clientes activados se traduce a
    // EXACTAMENTE 1 unidad por cliente nuevo, y cae SOLO en el producto de
    // MAYOR venta real (nunca un promedio que pueda inflar el número) de cada
    // Suplidor×Categoría en alcance. Los clientes faltantes se reparten entre
    // esos pares proporcional a su propia activación actual (mismo criterio
    // de reparto proporcional que el resto del motor). Como solo se tocan las
    // filas de ESE producto específico (nunca se reescala nada más), el
    // impacto en RD$ sube hacia arriba solo — misma propiedad que ya logran
    // las columnas de Ajuste %/Monto de "3. Análisis comparativo".
    [HttpPost("lote/{loteId:guid}/recalcular-activacion")]
    public IActionResult RecalcularActivacion(Guid loteId, [FromBody] RecalcularActivacionReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
        try
        {
            using var cn = NewConn(); cn.Open();
            var (ok, error) = ValidarLoteEditable(cn, loteId);
            if (!ok) return BadRequest(new { ok = false, error });
            var (baseIni, baseFinExcl) = ObtenerRangoBaseLote(cn, loteId);
            var a = req.ambito ?? new AmbitoNodo(null, null, null, null, null, null, null);
            bool filtrandoMes = req.anio is > 0 && req.mes is >= 1 and <= 12;

            int actual = ContarClientesEfectivos(cn, a, baseIni, baseFinExcl);
            double objetivo = string.Equals(req.modo, "pct", StringComparison.OrdinalIgnoreCase) ? actual * (req.valor / 100.0) : req.valor;
            int clientesFaltantes = (int)Math.Ceiling(objetivo - actual);
            if (clientesFaltantes <= 0)
                return Ok(new { ok = true, sinCambios = true, clientesActuales = actual, mensaje = "Ya cumple o supera el objetivo — no se aplicó ningún cambio." });

            // Pares (Suplidor,Categoría) vendidos dentro del ámbito + su propia
            // activación actual (peso para repartir clientesFaltantes) — si el
            // ámbito YA trae ambos (cat3Id y cat1Id), es un solo par directo.
            var (whereOrg, parmsPares) = FiltroOrganizacionalClientes(a);
            parmsPares["@desde"] = baseIni; parmsPares["@hasta"] = baseFinExcl;
            string whereProdPares = "";
            if (a.cat3Id != null) { whereProdPares += " AND pr.Cat3ID=@cat3p"; parmsPares["@cat3p"] = a.cat3Id.Value; }
            if (a.cat1Id != null) { whereProdPares += " AND pr.Cat1ID=@cat1p"; parmsPares["@cat1p"] = a.cat1Id.Value; }
            var pares = new List<(int cat3, int cat1, int efectivos)>();
            using (var cmd = new SqlCommand($@"
SELECT pr.Cat3ID, pr.Cat1ID, COUNT(DISTINCT v.CliID) Efectivos
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN clientesDetalle cd WITH (NOLOCK) ON cd.CliID = v.CliID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID AND pr.Cat3ID IS NOT NULL AND pr.Cat1ID IS NOT NULL
WHERE v.VenEstatus<>0 AND v.VenFecha>=@desde AND v.VenFecha<@hasta AND d.VenCantidad>0
  AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp WITH (NOLOCK) INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo=rgp.GrpCodigo WHERE rgp.RepCodigo=cd.RepCodigo AND gpd.ProID=d.ProID)
  {whereOrg}{whereProdPares}
GROUP BY pr.Cat3ID, pr.Cat1ID", cn) { CommandTimeout = 60 })
            {
                foreach (var kv in parmsPares) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) pares.Add((rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2)));
            }
            if (pares.Count == 0) return BadRequest(new { ok = false, error = "No hay Suplidor×Categoría con venta real en el Rango Base dentro de este ámbito — no se puede repartir el incremento." });

            // Reps en el ámbito organizacional (para acotar universo/base histórica al alcance
            // exacto) — reusa 'whereOrg' ya construido arriba (mismo texto, `parmsPares` ya
            // trae los valores de ced/can/sup/rep además de los de producto/fecha; los que no
            // se referencian en este SQL simplemente se ignoran, sin error).
            var repsEnAmbito = new List<string>();
            using (var cmd = new SqlCommand($@"
SELECT DISTINCT cd.RepCodigo FROM clientesDetalle cd WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=cd.RepCodigo AND r.RepCargo IN ('PreVendedor','Vendedor')
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=cd.RepCodigo
WHERE 1=1{whereOrg}", cn) { CommandTimeout = 60 })
            {
                foreach (var kv in parmsPares) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) repsEnAmbito.Add(rd.GetString(0));
            }
            if (repsEnAmbito.Count == 0) return BadRequest(new { ok = false, error = "Ningún vendedor cae dentro de ese ámbito." });

            var universo = PresupuestoBuilderCore.UniversoProductosPorRep(cn, repsEnAmbito);
            var baseHist = PresupuestoBuilderCore.CalcularBaseHistorica(cn, baseIni, baseFinExcl, universo, repsEnAmbito);
            var proIds = baseHist.Select(b => b.ProId).Distinct().ToList();
            var dimsProd = new Dictionary<int, (int cat3, int cat1, bool combo)>();
            if (proIds.Count > 0)
            {
                var parmsP = new Dictionary<string, object>();
                var names = proIds.Select((p, i) => { var n = $"@pp{i}"; parmsP[n] = p; return n; }).ToList();
                using var cmd = new SqlCommand($"SELECT ProID, ISNULL(Cat3ID,0), ISNULL(Cat1ID,0), ProDescripcion FROM Productos WITH (NOLOCK) WHERE ProID IN ({string.Join(",", names)})", cn) { CommandTimeout = 60 };
                foreach (var kv in parmsP) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    dimsProd[rd.GetInt32(0)] = (rd.GetInt32(1), rd.GetInt32(2), (rd.IsDBNull(3) ? "" : rd.GetString(3)).IndexOf("COMBO", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            int totalEfectivosPares = pares.Sum(p => p.efectivos);
            var resultado = new List<object>();
            int totalAplicado = 0;
            using var cmdUpd = new SqlCommand(@"
UPDATE PresupuestoStaging_RM SET PrePresupuestoPropuesto=@nuevo WHERE Id=@id AND LoteId=@lote", cn) { CommandTimeout = 60 };
            var pNuevo = cmdUpd.Parameters.Add("@nuevo", System.Data.SqlDbType.Decimal); pNuevo.Precision = 18; pNuevo.Scale = 4;
            var pId = cmdUpd.Parameters.Add("@id", System.Data.SqlDbType.Int);
            var pLote = cmdUpd.Parameters.Add("@lote", System.Data.SqlDbType.UniqueIdentifier); pLote.Value = loteId;

            foreach (var par in pares)
            {
                double peso = totalEfectivosPares > 0 ? (double)par.efectivos / totalEfectivosPares : 1.0 / pares.Count;
                int clientesFaltantesPar = (int)Math.Ceiling(clientesFaltantes * peso);
                if (clientesFaltantesPar <= 0) continue;

                // Producto #1 (mayor venta real) de ESTE par, agregando entre TODOS los reps del ámbito, excluyendo combos.
                var montoPorProducto = baseHist
                    .Where(b => dimsProd.TryGetValue(b.ProId, out var d) && d.cat3 == par.cat3 && d.cat1 == par.cat1 && !d.combo && b.MontoNeto > 0)
                    .GroupBy(b => b.ProId)
                    .Select(g => (proId: g.Key, monto: g.Sum(x => x.MontoNeto)))
                    .OrderByDescending(x => x.monto)
                    .FirstOrDefault();
                if (montoPorProducto.proId == 0) continue; // sin producto real en este par, no se fuerza nada

                var filasProducto = new Dictionary<int, double>(); // Id staging -> valor actual
                using (var cmd = new SqlCommand($@"
SELECT s.Id, s.PrePresupuestoPropuesto
FROM PresupuestoStaging_RM s WITH (NOLOCK)
WHERE s.LoteId=@lote AND TRY_CAST(s.PreReferencia AS INT)=@pro AND s.RepCodigo IN ({string.Join(",", repsEnAmbito.Select((r, i) => $"@r{i}"))}){(filtrandoMes ? " AND s.PreAnio=@fAnio AND s.PreMes=@fMes" : "")}", cn) { CommandTimeout = 60 })
                {
                    cmd.Parameters.AddWithValue("@lote", loteId);
                    cmd.Parameters.AddWithValue("@pro", montoPorProducto.proId);
                    for (int i = 0; i < repsEnAmbito.Count; i++) cmd.Parameters.AddWithValue($"@r{i}", repsEnAmbito[i]);
                    if (filtrandoMes) { cmd.Parameters.AddWithValue("@fAnio", req.anio!.Value); cmd.Parameters.AddWithValue("@fMes", req.mes!.Value); }
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read()) filasProducto[rd.GetInt32(0)] = Convert.ToDouble(rd.GetValue(1));
                }
                double totalFilas = filasProducto.Values.Sum();
                if (totalFilas <= 0)
                {
                    resultado.Add(new { par.cat3, par.cat1, productoId = montoPorProducto.proId, clientesFaltantesPar, aplicado = false, motivo = "Sin fila de presupuesto base para este producto en el ámbito — no se pudo repartir." });
                    continue;
                }
                double unidadesAAgregar = clientesFaltantesPar * 1.0; // 1 unidad por cliente, pedido explícito (lo más conservador)
                foreach (var (idStaging, valorPrevio) in filasProducto)
                {
                    double peso2 = valorPrevio / totalFilas;
                    double nuevo = valorPrevio + unidadesAAgregar * peso2;
                    pNuevo.Value = Math.Round(nuevo, 4); pId.Value = idStaging;
                    cmdUpd.ExecuteNonQuery();
                }
                totalAplicado += clientesFaltantesPar;
                resultado.Add(new { par.cat3, par.cat1, productoId = montoPorProducto.proId, clientesFaltantesPar, unidadesAgregadas = unidadesAAgregar, aplicado = true });
            }

            using (var cmdNota = new SqlCommand(@"
UPDATE PresupuestoLotes_RM SET Descripcion = CONCAT(ISNULL(Descripcion,''),
  CASE WHEN Descripcion IS NULL OR Descripcion='' THEN '' ELSE ' · ' END, @nota) WHERE LoteId=@lote", cn))
            {
                string mesTxtAct = filtrandoMes ? $" [Mes={req.anio}-{req.mes:00}]" : "";
                cmdNota.Parameters.AddWithValue("@nota", $"Activación forzada{mesTxtAct} por {user}: +{clientesFaltantes} clientes objetivo (+{totalAplicado} aplicados, 1 u./cliente en el producto top de cada Suplidor×Categoría)");
                cmdNota.Parameters.AddWithValue("@lote", loteId);
                cmdNota.ExecuteNonQuery();
            }

            return Ok(new { ok = true, clientesActuales = actual, clientesObjetivo = objetivo, clientesFaltantes, clientesAplicados = totalAplicado, detalle = resultado });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Excel de revisión — Fase D (2026-09-01). Reporte de SOLO LECTURA (el
    // usuario decidió que la Fase E de reimportar ya no hace tanta falta —
    // los Ajuste %/Monto y Recalcular de "3. Análisis comparativo"/vista de
    // Activaciones ya cubren el caso en vivo). 5 hojas: Resumen + Supervisor +
    // Vendedor + Suplidor + Categoría, mismas columnas en las 4 de detalle.
    // Reusa `CalcularComparativoInterno` (mismos números que la pantalla) +
    // `ContarClientesAsignados`/`ContarClientesEfectivos` (mismos que la
    // vista de Activaciones) — no se recalcula nada por separado.
    //
    // Simplificación deliberada (comunicada al usuario): NO incluye "Clientes
    // Propuestos a Activar"/"Efectividad Propuesta" — esa proyección depende
    // de metas configuradas por nodo y ya la resuelve con precisión la
    // pantalla en vivo (recalcular-activacion / el hook de Generar);
    // replicarla aquí de forma agregada arriesgaba números que no cuadraran
    // exactamente con la pantalla. Si luego hace falta, se agrega aparte.
    // ══════════════════════════════════════════════════════════════════════

    static readonly string[] NombresMes = { "", "Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };

    [HttpGet("lote/{loteId:guid}/revision.xlsx")]
    public IActionResult ExportarRevisionExcel(Guid loteId, [FromQuery] int? compAnio, [FromQuery] int? compMes, [FromQuery] int? filtroAnio, [FromQuery] int? filtroMes)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();

            // Meses a incluir (2026-09-02, pedido explícito del usuario: "necesito
            // que tenga el presupuesto por mes en la primera página y los
            // comparativos deben estar hacia abajo mes a mes"): por defecto, TODOS
            // los meses Destino del lote, cada uno con sus propias cifras exactas
            // (no promediadas) — mismo mecanismo que el Selector de Mes de "3.
            // Análisis comparativo". Si el usuario ya venía con un mes específico
            // elegido en pantalla (filtroAnio/filtroMes), el Excel respeta ESE
            // único mes en vez de traer todos.
            List<(int anio, int mes)> mesesDestino;
            if (filtroAnio is > 0 && filtroMes is >= 1 and <= 12)
            {
                mesesDestino = new List<(int, int)> { (filtroAnio.Value, filtroMes.Value) };
            }
            else
            {
                string? destinoDetalleRaw;
                using (var cmdMeses = new SqlCommand("SELECT DestinoDetalle FROM PresupuestoLotes_RM WHERE LoteId=@id", cn))
                {
                    cmdMeses.Parameters.AddWithValue("@id", loteId);
                    destinoDetalleRaw = (string?)cmdMeses.ExecuteScalar();
                }
                if (destinoDetalleRaw == null) return NotFound(new { ok = false, error = "Lote no encontrado." });
                mesesDestino = destinoDetalleRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Split('-')).Select(p => (int.Parse(p[0]), int.Parse(p[1]))).ToList();
            }
            if (mesesDestino.Count == 0) return NotFound(new { ok = false, error = "El lote no tiene meses Destino configurados." });

            var porMes = new List<(int anio, int mes, List<FilaComparativo> filas)>();
            foreach (var (anio, mes) in mesesDestino)
            {
                var (okM, errorM, _, _, filasM) = CalcularComparativoInterno(cn, loteId, compAnio, compMes, anio, mes);
                if (!okM) return NotFound(new { ok = false, error = errorM });
                porMes.Add((anio, mes, filasM));
            }

            var (baseIni, baseFinExcl) = ObtenerRangoBaseLote(cn, loteId);
            // Meses del rango Base (mismo cálculo que Generar) — para expresar Activaciones/
            // Facturas como PROMEDIO MENSUAL, comparable con "Venta Promedio mensual".
            int mesesBaseExcel = ((baseFinExcl.AddDays(-1).Year - baseIni.Year) * 12 + baseFinExcl.AddDays(-1).Month - baseIni.Month) + 1;
            if (mesesBaseExcel < 1) mesesBaseExcel = 1;
            // Totales de TODO el lote (para la hoja Resumen, que no tiene un nodo por fila) — un
            // COUNT(DISTINCT...) sin GROUP BY ya colapsa solo a 1 fila con el total correcto
            // (no es una suma que pueda doble-contar). Antes esto llamaba a
            // ClientesEfectivosPorOrg/FacturasPorOrg con el literal "'TOTAL'" simulando una
            // columna — reventaba con "Each GROUP BY expression must contain at least one column
            // that is not an outer reference" (bug real reportado por el usuario 2026-09-06 al
            // descargar el Excel) — GROUP BY sobre una CONSTANTE, combinado con el EXISTS
            // correlacionado de esas funciones, es el mismo patrón que ya rompió
            // TorrePersonalizados.Calcular. Fix: métodos dedicados sin GROUP BY en absoluto.
            int efectivosTotalLote = ClientesEfectivosTotal(cn, baseIni, baseFinExcl);
            int facturasTotalLote = FacturasTotal(cn, baseIni, baseFinExcl);

            var todosLosReps = porMes.SelectMany(m => m.filas.Select(f => f.RepCodigo)).Distinct().ToList();
            var efectividadPorRep = PresupuestoBuilderCore.EfectividadPorRep(cn, baseIni, baseFinExcl, todosLosReps);

            using var wb = new XLWorkbook();

            // ── Objetivo seleccionado al Generar (2026-09-06, pedido explícito del
            // usuario) — persistido en PresupuestoLotes_RM desde el rediseño "Objetivo
            // por Mes + Total híbrido". Se muestra en TODAS las hojas (encabezado fijo,
            // no prorrateado por mes) para poder comparar contra la suma ACTUAL del
            // lote — que puede haberse desviado del objetivo original por ediciones a
            // mano en "2. Revisar" después de Generar.
            string? objetivoModoLote = null; double? objetivoTotalLote = null;
            using (var cmdObj = new SqlCommand("SELECT ObjetivoModo, ObjetivoTotalRD FROM PresupuestoLotes_RM WHERE LoteId=@id", cn))
            {
                cmdObj.Parameters.AddWithValue("@id", loteId);
                using var rdObj = cmdObj.ExecuteReader();
                if (rdObj.Read())
                {
                    objetivoModoLote = rdObj.IsDBNull(0) ? null : rdObj.GetString(0);
                    objetivoTotalLote = rdObj.IsDBNull(1) ? (double?)null : Convert.ToDouble(rdObj.GetValue(1));
                }
            }
            double sumaActualLote = porMes.Sum(m => m.filas.Sum(f => f.MontoPropuesto));
            int objetivoExtra = objetivoTotalLote is > 0 ? 3 : 0;
            void EscribirEncabezadoObjetivo(IXLWorksheet ws, int filaInicio)
            {
                if (objetivoTotalLote is not > 0) return;
                double diferencia = sumaActualLote - objetivoTotalLote.Value;
                ws.Cell(filaInicio, 1).Value = $"Objetivo seleccionado: {(objetivoModoLote == "valor" ? "RD$" : "")}{objetivoTotalLote:#,0.##}";
                ws.Cell(filaInicio + 1, 1).Value = $"Suma actual del lote: RD${sumaActualLote:#,0.##}";
                ws.Cell(filaInicio + 2, 1).Value = $"Diferencia: RD${diferencia:+#,0.##;-#,0.##;0}";
                ws.Range(filaInicio, 1, filaInicio + 2, 1).Style.Font.Italic = true;
            }

            // ── Resumen — presupuesto mes a mes ─────────────────────────────
            var wsR = wb.Worksheets.Add("Resumen");
            wsR.Cell(1, 1).Value = "Comparación de presupuesto — Lote " + loteId;
            wsR.Cell(1, 1).Style.Font.Bold = true; wsR.Cell(1, 1).Style.Font.FontSize = 14;
            wsR.Cell(2, 1).Value = $"Rango Base: {baseIni:yyyy-MM-dd} a {baseFinExcl.AddDays(-1):yyyy-MM-dd}";
            EscribirEncabezadoObjetivo(wsR, 3);
            int filaCabResumen = 4 + objetivoExtra;
            // Activaciones/Facturas (2026-09-06, pedido explícito del usuario): "Necesarias" es
            // el INCREMENTO sobre lo que ya se hace hoy — se asume que Activaciones/Facturas
            // escalan proporcional al mismo % de incremento que ya muestra RD$ (Presupuesto
            // Sugerido vs. Venta Promedio) — mismo supuesto de proporcionalidad que ya usa el
            // resto del motor (unidades/cliente activo, unidades/factura). Nunca negativo (si el
            // presupuesto es MENOR al promedio, no hacen falta MÁS activaciones/facturas).
            double activacionesPromActualResumen = (double)efectivosTotalLote / mesesBaseExcel;
            double facturasPromActualResumen = (double)facturasTotalLote / mesesBaseExcel;
            string[] cab = { "Mes", "Presupuestado actual", "Venta Promedio (Base)", "Presupuesto Sugerido", "Incremento % vs. Promedio",
                "Activaciones Prom. Actual (mensual)", "Activaciones Necesarias", "Facturas Prom. Actuales (mensual)", "Facturas Increm. Necesarias" };
            for (int c = 0; c < cab.Length; c++) { wsR.Cell(filaCabResumen, 1 + c).Value = cab[c]; wsR.Cell(filaCabResumen, 1 + c).Style.Font.Bold = true; wsR.Cell(filaCabResumen, 1 + c).Style.Fill.BackgroundColor = XLColor.FromHtml("#1C2F6E"); wsR.Cell(filaCabResumen, 1 + c).Style.Font.FontColor = XLColor.White; }
            int primeraFilaResumen = filaCabResumen + 1;
            int rowR = primeraFilaResumen;
            foreach (var m in porMes)
            {
                double propM = m.filas.Sum(f => f.MontoPropuesto), compM = m.filas.Sum(f => f.MontoComparacion), promM = m.filas.Sum(f => f.MontoVentaPromedio);
                double incrementoPctM = promM > 0 ? propM / promM - 1 : 0;
                wsR.Cell(rowR, 1).Value = $"{NombresMes[m.mes]} {m.anio}";
                wsR.Cell(rowR, 2).Value = compM; wsR.Cell(rowR, 3).Value = promM; wsR.Cell(rowR, 4).Value = propM;
                wsR.Cell(rowR, 5).Value = incrementoPctM;
                wsR.Cell(rowR, 6).Value = activacionesPromActualResumen;
                wsR.Cell(rowR, 7).Value = Math.Max(0, activacionesPromActualResumen * incrementoPctM);
                wsR.Cell(rowR, 8).Value = facturasPromActualResumen;
                wsR.Cell(rowR, 9).Value = Math.Max(0, facturasPromActualResumen * incrementoPctM);
                rowR++;
            }
            wsR.Cell(rowR, 1).Value = "TOTAL"; wsR.Cell(rowR, 1).Style.Font.Bold = true;
            for (int c = 2; c <= 4; c++) { wsR.Cell(rowR, c).FormulaA1 = $"=SUM({wsR.Cell(primeraFilaResumen, c).Address}:{wsR.Cell(rowR - 1, c).Address})"; wsR.Cell(rowR, c).Style.Font.Bold = true; }
            wsR.Range(primeraFilaResumen, 2, rowR, 4).Style.NumberFormat.Format = "#,##0";
            wsR.Range(primeraFilaResumen, 6, rowR - 1, 9).Style.NumberFormat.Format = "#,##0.0";
            wsR.Range(primeraFilaResumen, 5, rowR - 1, 5).Style.NumberFormat.Format = "0.0%";
            wsR.Column(1).Width = 22; wsR.Column(2).Width = 20; wsR.Column(3).Width = 20; wsR.Column(4).Width = 20;
            for (int c = 5; c <= cab.Length; c++) wsR.Column(c).Width = 20;
            wsR.SheetView.FreezeRows(filaCabResumen);

            // Calculado UNA VEZ para las 4 hojas (ver comentario en las funciones
            // ClientesAsignadosPorOrg/Producto arriba) — antes esto se recalculaba
            // por cada fila de cada hoja, con riesgo de timeout.
            var asignadosPorRep = ClientesAsignadosPorOrg(cn, "cd.RepCodigo");
            var efectivosPorRep = ClientesEfectivosPorOrg(cn, "cd.RepCodigo", baseIni, baseFinExcl);
            var asignadosPorSup = ClientesAsignadosPorOrg(cn, "r.RepSupervisor");
            var efectivosPorSup = ClientesEfectivosPorOrg(cn, "r.RepSupervisor", baseIni, baseFinExcl);
            var asignadosPorCat3 = ClientesAsignadosPorProducto(cn, false);
            var efectivosPorCat3 = ClientesEfectivosPorProducto(cn, false, baseIni, baseFinExcl);
            var asignadosPorCat1 = ClientesAsignadosPorProducto(cn, true);
            var efectivosPorCat1 = ClientesEfectivosPorProducto(cn, true, baseIni, baseFinExcl);
            // "Por Canal" (Bloque B, 2026-09-06, pedido explícito del usuario) — mismas
            // funciones genéricas ya existentes, agrupando por ra.CanID en vez de
            // RepCodigo/RepSupervisor, sin cambios en ClientesAsignadosPorOrg/Efectivos.
            var asignadosPorCanal = ClientesAsignadosPorOrg(cn, "ra.CanID");
            var efectivosPorCanal = ClientesEfectivosPorOrg(cn, "ra.CanID", baseIni, baseFinExcl);

            // Facturas (2026-09-06, pedido explícito del usuario: "Activaciones promedio actual,
            // activaciones necesarias... facturas promedio actuales y facturas promedio
            // incrementales necesarias... en todos los niveles") — mismo patrón que Clientes
            // Asignados/Efectivos de arriba, sin "asignado" (las facturas no se "asignan", son
            // pura actividad real en el Rango Base).
            var facturasPorRep = FacturasPorOrg(cn, "cd.RepCodigo", baseIni, baseFinExcl);
            var facturasPorSup = FacturasPorOrg(cn, "r.RepSupervisor", baseIni, baseFinExcl);
            var facturasPorCat3 = FacturasPorProducto(cn, false, baseIni, baseFinExcl);
            var facturasPorCat1 = FacturasPorProducto(cn, true, baseIni, baseFinExcl);
            var facturasPorCanal = FacturasPorOrg(cn, "ra.CanID", baseIni, baseFinExcl);

            // Cada hoja de detalle ahora apila UN BLOQUE COMPLETO por cada mes
            // Destino del lote (título de mes + encabezado + filas + TOTAL del
            // mes), en vez de un único bloque promediado — pedido explícito del
            // usuario: "los comparativos deben estar hacia abajo mes a mes".
            void AgregarHoja(string nombreHoja, Func<FilaComparativo, string> claveFn, Func<FilaComparativo, string> nombreFn,
                Func<string, (int asignados, int efectivos)> conteosFn, Func<string, int> facturasFn, bool incluirEfectividad)
            {
                var ws = wb.Worksheets.Add(nombreHoja);
                var headers = new List<string> { nombreHoja.Replace("Por ", ""), "Presupuestado actual", "Presupuesto Propuesto", "Venta Promedio mensual",
                    "Incremento vs. Venta Promedio", "Variación vs. Presupuestado", "Clientes Asignados", "Clientes Efectivos", "% Activación",
                    "Activaciones Prom. Actual (mensual)", "Activaciones Necesarias", "Facturas Prom. Actuales (mensual)", "Facturas Increm. Necesarias" };
                int colActivacionesPromActual = 10, colActivacionesNecesarias = 11, colFacturasPromActual = 12, colFacturasNecesarias = 13;
                if (incluirEfectividad) { headers.Add("Efectividad Actual (Cli. c/pedido ÷ Visitados)"); }
                int nCols = headers.Count;

                EscribirEncabezadoObjetivo(ws, 1);
                int row = 1 + objetivoExtra;
                foreach (var bloque in porMes)
                {
                    ws.Cell(row, 1).Value = $"{NombresMes[bloque.mes]} {bloque.anio}";
                    ws.Cell(row, 1).Style.Font.Bold = true; ws.Cell(row, 1).Style.Font.FontSize = 12.5;
                    ws.Range(row, 1, row, nCols).Style.Fill.BackgroundColor = XLColor.FromHtml("#CADCFC");
                    row++;

                    int headerRow = row;
                    for (int c = 0; c < headers.Count; c++)
                    {
                        ws.Cell(headerRow, 1 + c).Value = headers[c]; ws.Cell(headerRow, 1 + c).Style.Font.Bold = true;
                        ws.Cell(headerRow, 1 + c).Style.Fill.BackgroundColor = XLColor.FromHtml("#1C2F6E"); ws.Cell(headerRow, 1 + c).Style.Font.FontColor = XLColor.White;
                    }
                    row++;

                    var grupos = bloque.filas.GroupBy(claveFn).OrderByDescending(g => g.Sum(f => f.MontoPropuesto)).ToList();
                    int primeraFila = row;
                    foreach (var g in grupos)
                    {
                        double prop = g.Sum(f => f.MontoPropuesto), comp = g.Sum(f => f.MontoComparacion), prom = g.Sum(f => f.MontoVentaPromedio);
                        var (asignados, efectivos) = conteosFn(g.Key);
                        int facturas = facturasFn(g.Key);
                        double incrementoPct = prom > 0 ? prop / prom - 1 : 0;
                        double activacionesPromActual = (double)efectivos / mesesBaseExcel;
                        double facturasPromActual = (double)facturas / mesesBaseExcel;
                        ws.Cell(row, 1).Value = nombreFn(g.First());
                        ws.Cell(row, 2).Value = comp; ws.Cell(row, 3).Value = prop; ws.Cell(row, 4).Value = prom;
                        ws.Cell(row, 5).Value = incrementoPct; ws.Cell(row, 6).Value = comp > 0 ? prop / comp - 1 : 0;
                        ws.Cell(row, 7).Value = asignados; ws.Cell(row, 8).Value = efectivos;
                        ws.Cell(row, 9).Value = asignados > 0 ? (double)efectivos / asignados : 0;
                        // Activaciones/Facturas necesarias (2026-09-06, pedido explícito del
                        // usuario): incremento sobre lo que YA se hace hoy, asumiendo que escalan
                        // proporcional al mismo % de incremento que ya muestra el RD$ — nunca
                        // negativo (un presupuesto por debajo del promedio no exige MÁS).
                        ws.Cell(row, colActivacionesPromActual).Value = activacionesPromActual;
                        ws.Cell(row, colActivacionesNecesarias).Value = Math.Max(0, activacionesPromActual * incrementoPct);
                        ws.Cell(row, colFacturasPromActual).Value = facturasPromActual;
                        ws.Cell(row, colFacturasNecesarias).Value = Math.Max(0, facturasPromActual * incrementoPct);
                        ws.Range(row, 2, row, 4).Style.NumberFormat.Format = "#,##0";
                        ws.Range(row, 5, row, 6).Style.NumberFormat.Format = "0.0%";
                        ws.Cell(row, 9).Style.NumberFormat.Format = "0.0%";
                        ws.Range(row, colActivacionesPromActual, row, colFacturasNecesarias).Style.NumberFormat.Format = "#,##0.0";
                        if (incluirEfectividad)
                        {
                            var repsGrupo = g.Select(f => f.RepCodigo).Distinct().ToList();
                            int visitados = repsGrupo.Sum(r => efectividadPorRep.GetValueOrDefault(r)?.Visitados ?? 0);
                            int conPedido = repsGrupo.Sum(r => efectividadPorRep.GetValueOrDefault(r)?.ClientesConPedido ?? 0);
                            ws.Cell(row, 14).Value = visitados > 0 ? (double)conPedido / visitados : 0;
                            ws.Cell(row, 14).Style.NumberFormat.Format = "0.0%";
                        }
                        row++;
                    }
                    ws.Cell(row, 1).Value = $"TOTAL {NombresMes[bloque.mes]} {bloque.anio}"; ws.Cell(row, 1).Style.Font.Bold = true;
                    if (row > primeraFila)
                    {
                        for (int c = 2; c <= 4; c++) { ws.Cell(row, c).FormulaA1 = $"=SUM({ws.Cell(primeraFila, c).Address}:{ws.Cell(row - 1, c).Address})"; ws.Cell(row, c).Style.Font.Bold = true; ws.Cell(row, c).Style.NumberFormat.Format = "#,##0"; }
                        ws.Cell(row, 5).FormulaA1 = $"=IF({ws.Cell(row, 4).Address}=0,0,{ws.Cell(row, 3).Address}/{ws.Cell(row, 4).Address}-1)";
                        ws.Cell(row, 6).FormulaA1 = $"=IF({ws.Cell(row, 2).Address}=0,0,{ws.Cell(row, 3).Address}/{ws.Cell(row, 2).Address}-1)";
                        ws.Range(row, 5, row, 6).Style.NumberFormat.Format = "0.0%"; ws.Range(row, 5, row, 6).Style.Font.Bold = true;
                    }
                    row += 2; // fila en blanco antes del siguiente mes
                }

                ws.Column(1).Width = 34;
                for (int c = 2; c <= nCols; c++) ws.Column(c).Width = 16;
            }

            AgregarHoja("Por Supervisor", f => f.SupCod, f => string.IsNullOrEmpty(f.SupCod) ? "(Sin supervisor)" : $"{f.SupCod} - {f.SupNombre}",
                k => (asignadosPorSup.GetValueOrDefault(k ?? "", 0), efectivosPorSup.GetValueOrDefault(k ?? "", 0)),
                k => facturasPorSup.GetValueOrDefault(k ?? "", 0), true);
            AgregarHoja("Por Vendedor", f => f.RepCodigo, f => $"{f.RepCodigo} - {f.RepNombre}",
                k => (asignadosPorRep.GetValueOrDefault(k, 0), efectivosPorRep.GetValueOrDefault(k, 0)),
                k => facturasPorRep.GetValueOrDefault(k, 0), true);
            AgregarHoja("Por Suplidor", f => f.Cat3ID.ToString(), f => $"{f.Cat3ID} - {f.Cat3Nombre}",
                k => (asignadosPorCat3.GetValueOrDefault(int.Parse(k), 0), efectivosPorCat3.GetValueOrDefault(int.Parse(k), 0)),
                k => facturasPorCat3.GetValueOrDefault(int.Parse(k), 0), false);
            AgregarHoja("Por Categoria", f => f.Cat1ID.ToString(), f => $"{f.Cat1ID} - {f.Cat1Nombre}",
                k => (asignadosPorCat1.GetValueOrDefault(int.Parse(k), 0), efectivosPorCat1.GetValueOrDefault(int.Parse(k), 0)),
                k => facturasPorCat1.GetValueOrDefault(int.Parse(k), 0), false);
            AgregarHoja("Por Canal", f => f.CanID.ToString(), f => $"{f.CanID} - {f.CanNombre}",
                k => (asignadosPorCanal.GetValueOrDefault(k, 0), efectivosPorCanal.GetValueOrDefault(k, 0)),
                k => facturasPorCanal.GetValueOrDefault(k, 0), false);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var nombreArchivo = $"Revision_Presupuesto_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // 'forzar' (2026-08-30): override explícito de Gerencia para publicar un
    // lote marcado ⚠BLOQUEA_PUBLICAR (meta de activación obligatoria que no se
    // alcanza ni forzando el presupuesto) — sin esto, Publicar lo rechaza.
    public sealed record LoteIdReq(Guid loteId, bool? forzar = null);

    // ── POST /apipresupuestogestion/publicar ────────────────────────────────
    // Único endpoint que escribe a PresupuestosHistoricos: por cada Año/Mes del
    // lote, borra las filas del PreTipo+reps afectados y reinserta desde
    // staging. Único punto de todo el controller que usa Destino() — todo lo
    // demás (Lotes/Staging/Publicaciones) queda en la conexión de producción.
    [HttpPost("publicar")]
    public IActionResult Publicar([FromBody] LoteIdReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
        try
        {
            using var cn = NewConn(); cn.Open();
            using var tx = cn.BeginTransaction();
            try
            {
                string estado, preTipo; string? descripcionLote;
                using (var cmd = new SqlCommand("SELECT Estado, PreTiposIncluidos, Descripcion FROM PresupuestoLotes_RM WHERE LoteId=@id", cn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", req.loteId);
                    using var rd = cmd.ExecuteReader();
                    if (!rd.Read()) { tx.Rollback(); return NotFound(new { ok = false, error = "Lote no encontrado." }); }
                    estado = rd.GetString(0); preTipo = rd.GetString(1); descripcionLote = rd.IsDBNull(2) ? null : rd.GetString(2);
                }
                if (estado != "Borrador")
                {
                    tx.Rollback();
                    return BadRequest(new { ok = false, error = $"El lote ya está en estado '{estado}', no se puede publicar de nuevo." });
                }
                // Meta de activación OBLIGATORIA marcada BloqueaPublicar y que ni forzando
                // el presupuesto se alcanzó (marcador dejado por Generar) — requiere
                // override explícito de Gerencia (req.forzar=true) para publicar igual.
                if (descripcionLote?.Contains("⚠BLOQUEA_PUBLICAR") == true && req.forzar != true)
                {
                    tx.Rollback();
                    return BadRequest(new { ok = false, error = "Este lote tiene una meta de activación obligatoria que no se alcanza ni forzando el presupuesto. Revisa el detalle o confirma la publicación de todas formas.", requiereForzar = true });
                }

                int filasAfectadas = 0; double sumaTotal = 0;
                using (var cmdDel = new SqlCommand($@"
DELETE h FROM {Destino()}PresupuestosHistoricos h
INNER JOIN (SELECT DISTINCT RepCodigo, PreTipo, PreAnio, PreMes, PreReferencia FROM PresupuestoStaging_RM WHERE LoteId=@id) s
  ON h.RepCodigo=s.RepCodigo AND h.PreTipo=s.PreTipo AND h.PreAnio=s.PreAnio AND h.PreMes=s.PreMes AND h.PreReferencia=s.PreReferencia", cn, tx)
                { CommandTimeout = 120 })
                {
                    cmdDel.Parameters.AddWithValue("@id", req.loteId);
                    cmdDel.ExecuteNonQuery();
                }
                using (var cmdIns = new SqlCommand($@"
INSERT INTO {Destino()}PresupuestosHistoricos(RepCodigo,PreTipo,PreAnio,PreMes,PreReferencia,PrePresupuesto,PreEjecutado,UsuInicioSesion,PreFechaActualizacion,rowguid,mbversion,RepSupervisor)
SELECT RepCodigo,PreTipo,PreAnio,PreMes,PreReferencia,PrePresupuestoPropuesto,0,@u,GETDATE(),NEWID(),'PresupSmart',NULL
FROM PresupuestoStaging_RM WHERE LoteId=@id", cn, tx) { CommandTimeout = 120 })
                {
                    cmdIns.Parameters.AddWithValue("@u", user);
                    cmdIns.Parameters.AddWithValue("@id", req.loteId);
                    filasAfectadas = cmdIns.ExecuteNonQuery();
                }
                using (var cmdSum = new SqlCommand("SELECT SUM(PrePresupuestoPropuesto) FROM PresupuestoStaging_RM WHERE LoteId=@id", cn, tx))
                {
                    cmdSum.Parameters.AddWithValue("@id", req.loteId);
                    var v = cmdSum.ExecuteScalar();
                    sumaTotal = v == null || v == DBNull.Value ? 0 : Convert.ToDouble(v);
                }
                using (var cmdUpd = new SqlCommand("UPDATE PresupuestoLotes_RM SET Estado='Publicado', UsuPublico=@u, FechaPublicacion=GETDATE() WHERE LoteId=@id", cn, tx))
                {
                    cmdUpd.Parameters.AddWithValue("@u", user);
                    cmdUpd.Parameters.AddWithValue("@id", req.loteId);
                    cmdUpd.ExecuteNonQuery();
                }
                using (var cmdLog = new SqlCommand(@"
INSERT INTO PresupuestoPublicaciones_RM(LoteId,UsuInicioSesion,FilasAfectadas,SumaTotal,Detalle)
VALUES(@id,@u,@filas,@suma,@detalle)", cn, tx))
                {
                    cmdLog.Parameters.AddWithValue("@id", req.loteId);
                    cmdLog.Parameters.AddWithValue("@u", user);
                    cmdLog.Parameters.AddWithValue("@filas", filasAfectadas);
                    cmdLog.Parameters.AddWithValue("@suma", sumaTotal);
                    cmdLog.Parameters.AddWithValue("@detalle", $"Publicación de lote {preTipo}");
                    cmdLog.ExecuteNonQuery();
                }
                tx.Commit();
                return Ok(new { ok = true, filasAfectadas, sumaTotal });
            }
            catch { tx.Rollback(); throw; }
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apipresupuestogestion/descartar ───────────────────────────────
    [HttpPost("descartar")]
    public IActionResult Descartar([FromBody] LoteIdReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand("UPDATE PresupuestoLotes_RM SET Estado='Descartado' WHERE LoteId=@id AND Estado='Borrador'", cn);
            cmd.Parameters.AddWithValue("@id", req.loteId);
            int n = cmd.ExecuteNonQuery();
            return Ok(new { ok = true, actualizado = n > 0 });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // "Eliminar Presupuestos" (2026-09-06, pantalla nueva pedida explícitamente
    // por el usuario) — borra directo de PresupuestosHistoricos un bloque
    // filtrado por Año/Mes (obligatorios, un solo mes) + Canal/Supervisor/
    // Vendedor/Suplidor/Categoría/Producto (opcionales). Mismo patrón de
    // confirmación ya conocido de la Herramienta de Rebalanceo (texto literal
    // "CONFIRMAR", vista previa antes de aplicar). REGLA DURA (pedido explícito
    // del usuario): no se permite eliminar meses CERRADOS — solo el mes actual
    // en adelante — validado aquí en el servidor, no solo en el front.
    // ══════════════════════════════════════════════════════════════════════

    public sealed record EliminarReq(
        int anio, int mes, string? preTipo,
        List<string>? reps, List<int>? canales, List<string>? supervisores,
        List<string>? suplidores, List<string>? categorias, List<int>? productos,
        string? confirmacion);

    private static bool MesCerrado(int anio, int mes)
        => new DateTime(anio, mes, 1) < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    // Arma el WHERE + parámetros compartido entre preview/aplicar — mismo criterio
    // de resolución que los filtros de "Editar" en Generar, pero aquí se evalúa
    // directo en SQL contra PresupuestosHistoricos (no contra ventas/base histórica).
    private (string where, Dictionary<string, object> parms) ConstruirFiltroEliminar(EliminarReq req, string preTipoDefault)
    {
        var parms = new Dictionary<string, object> { ["@anio"] = req.anio, ["@mes"] = req.mes, ["@tipo"] = req.preTipo ?? preTipoDefault };
        var filtros = new List<string> { "h.PreTipo=@tipo", "h.PreAnio=@anio", "h.PreMes=@mes" };
        void AddIn(string campo, List<object>? valores, string prefijo)
        {
            if (valores is not { Count: > 0 }) return;
            var names = valores.Select((v, i) => { var n = $"@{prefijo}{i}"; parms[n] = v; return n; }).ToList();
            filtros.Add($"{campo} IN ({string.Join(",", names)})");
        }
        AddIn("h.RepCodigo", req.reps?.Cast<object>().ToList(), "rp");
        AddIn("ISNULL(ra.CanID,0)", req.canales?.Cast<object>().ToList(), "cn");
        AddIn("r.RepSupervisor", req.supervisores?.Cast<object>().ToList(), "sv");
        var suplidoresInt = req.suplidores?.Select(s => int.TryParse(s, out var v) ? v : (int?)null).Where(v => v.HasValue).Select(v => (object)v!.Value).ToList();
        var categoriasInt = req.categorias?.Select(s => int.TryParse(s, out var v) ? v : (int?)null).Where(v => v.HasValue).Select(v => (object)v!.Value).ToList();
        AddIn("p2.Cat3ID", suplidoresInt, "c3");
        AddIn("p2.Cat1ID", categoriasInt, "c1");
        AddIn("TRY_CAST(h.PreReferencia AS INT)", req.productos?.Cast<object>().ToList(), "pr");
        return (string.Join(" AND ", filtros), parms);
    }

    private const string JoinsEliminar = @"
FROM {0}PresupuestosHistoricos h WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=h.RepCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=h.RepCodigo
LEFT JOIN Productos p2 WITH (NOLOCK) ON p2.ProID=TRY_CAST(h.PreReferencia AS INT)";

    // ── POST /apipresupuestogestion/historicos/eliminar/preview ─────────────
    [HttpPost("historicos/eliminar/preview")]
    public IActionResult EliminarPreview([FromBody] EliminarReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        if (req.anio <= 0 || req.mes is < 1 or > 12) return BadRequest(new { ok = false, error = "Año/Mes inválido." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var (where, parms) = ConstruirFiltroEliminar(req, "Unddprod");
            using var cmd = new SqlCommand($"SELECT COUNT(*), SUM(h.PrePresupuesto) {string.Format(JoinsEliminar, Destino())} WHERE {where}", cn) { CommandTimeout = 60 };
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            int filas = 0; double suma = 0;
            using (var rd = cmd.ExecuteReader())
                if (rd.Read()) { filas = rd.IsDBNull(0) ? 0 : rd.GetInt32(0); suma = rd.IsDBNull(1) ? 0 : Convert.ToDouble(rd.GetValue(1)); }
            return Ok(new { ok = true, filas, sumaTotal = suma, mesCerrado = MesCerrado(req.anio, req.mes) });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apipresupuestogestion/historicos/eliminar/aplicar ─────────────
    [HttpPost("historicos/eliminar/aplicar")]
    public IActionResult EliminarAplicar([FromBody] EliminarReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        if (req.anio <= 0 || req.mes is < 1 or > 12) return BadRequest(new { ok = false, error = "Año/Mes inválido." });
        if (!string.Equals(req.confirmacion?.Trim(), "CONFIRMAR", StringComparison.Ordinal))
            return BadRequest(new { ok = false, error = "Debes escribir CONFIRMAR para aplicar la eliminación." });
        // Regla dura, pedido explícito del usuario: no eliminar meses CERRADOS —
        // solo el mes actual en adelante. Validado en servidor, no solo en el front.
        if (MesCerrado(req.anio, req.mes))
            return BadRequest(new { ok = false, error = $"{req.anio}-{req.mes:00} es un mes cerrado (anterior al mes actual) — no se puede eliminar, solo el mes actual o futuros." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
            var (where, parms) = ConstruirFiltroEliminar(req, "Unddprod");
            using var tx = cn.BeginTransaction();
            try
            {
                int filasAfectadas; double sumaTotal;
                using (var cmdSum = new SqlCommand($"SELECT COUNT(*), SUM(h.PrePresupuesto) {string.Format(JoinsEliminar, Destino())} WHERE {where}", cn, tx) { CommandTimeout = 60 })
                {
                    foreach (var kv in parms) cmdSum.Parameters.AddWithValue(kv.Key, kv.Value);
                    using var rd = cmdSum.ExecuteReader();
                    filasAfectadas = 0; sumaTotal = 0;
                    if (rd.Read()) { filasAfectadas = rd.IsDBNull(0) ? 0 : rd.GetInt32(0); sumaTotal = rd.IsDBNull(1) ? 0 : Convert.ToDouble(rd.GetValue(1)); }
                }
                if (filasAfectadas == 0) { tx.Commit(); return Ok(new { ok = true, filasAfectadas = 0, sumaTotal = 0.0 }); }

                using (var cmdDel = new SqlCommand($@"
DELETE h {string.Format(JoinsEliminar, Destino())} WHERE {where}", cn, tx) { CommandTimeout = 120 })
                {
                    foreach (var kv in parms) cmdDel.Parameters.AddWithValue(kv.Key, kv.Value);
                    cmdDel.ExecuteNonQuery();
                }

                var detalle = $"ELIMINACIÓN {req.anio}-{req.mes:00} (PreTipo={req.preTipo ?? "Unddprod"})"
                    + (req.reps is { Count: > 0 } ? $" · Vendedores={string.Join(",", req.reps)}" : "")
                    + (req.canales is { Count: > 0 } ? $" · Canales={string.Join(",", req.canales)}" : "")
                    + (req.supervisores is { Count: > 0 } ? $" · Supervisores={string.Join(",", req.supervisores)}" : "")
                    + (req.suplidores is { Count: > 0 } ? $" · Suplidores={string.Join(",", req.suplidores)}" : "")
                    + (req.categorias is { Count: > 0 } ? $" · Categorías={string.Join(",", req.categorias)}" : "")
                    + (req.productos is { Count: > 0 } ? $" · Productos={string.Join(",", req.productos)}" : "");
                using (var cmdAudit = new SqlCommand(@"
INSERT INTO PresupuestoPublicaciones_RM(LoteId,UsuInicioSesion,FilasAfectadas,SumaTotal,Detalle)
VALUES(NULL,@u,@filas,@suma,@det)", cn, tx) { CommandTimeout = 30 })
                {
                    cmdAudit.Parameters.AddWithValue("@u", user);
                    cmdAudit.Parameters.AddWithValue("@filas", filasAfectadas);
                    cmdAudit.Parameters.AddWithValue("@suma", sumaTotal);
                    cmdAudit.Parameters.AddWithValue("@det", detalle);
                    cmdAudit.ExecuteNonQuery();
                }

                tx.Commit();
                return Ok(new { ok = true, filasAfectadas, sumaTotal });
            }
            catch { tx.Rollback(); throw; }
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // ⚙ Configuración de variables/metodologías — CRUD contra la misma conexión
    // de producción (PresupuestoVariables_RM/PresupuestoVariablesValores_RM),
    // sin prefijo — ver nota de arquitectura al inicio del archivo.
    // ══════════════════════════════════════════════════════════════════════

    // ── GET /apipresupuestogestion/variables/definiciones ───────────────────
    [HttpGet("variables/definiciones")]
    public IActionResult ListarDefiniciones()
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand(@"
SELECT VarCodigo, VarNombre, VarDescripcion, VarTipoDato, VarAmbitoPermitido, Activo,
       (SELECT COUNT(*) FROM PresupuestoVariablesValores_RM v WHERE v.VarCodigo=d.VarCodigo) ValoresCargados
FROM PresupuestoVariables_RM d WITH (NOLOCK)
ORDER BY VarCodigo", cn) { CommandTimeout = 30 };
            var lista = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                lista.Add(new
                {
                    VarCodigo = rd.GetString(0), VarNombre = rd.GetString(1), VarDescripcion = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    VarTipoDato = rd.GetString(3), VarAmbitoPermitido = rd.GetString(4), Activo = rd.GetBoolean(5),
                    ValoresCargados = rd.GetInt32(6),
                });
            return Ok(new { ok = true, definiciones = lista });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record DefinicionReq(string varCodigo, string varNombre, string? varDescripcion, string varTipoDato, string varAmbitoPermitido, bool activo);

    // ── POST /apipresupuestogestion/variables/definiciones/guardar ──────────
    // Upsert: si VarCodigo ya existe, actualiza; si no, la crea — así Gerencia
    // puede tanto ajustar una variable existente como agregar una metodología
    // nueva sin que nadie tenga que tocar código ni tablas a mano.
    [HttpPost("variables/definiciones/guardar")]
    public IActionResult GuardarDefinicion([FromBody] DefinicionReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(req.varCodigo)) return BadRequest(new { ok = false, error = "VarCodigo es obligatorio." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
            using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM PresupuestoVariables_RM WHERE VarCodigo=@cod)
    UPDATE PresupuestoVariables_RM
    SET VarNombre=@nom, VarDescripcion=@desc, VarTipoDato=@tipo, VarAmbitoPermitido=@amb, Activo=@act, UsuInicioSesion=@u, FechaActualizacion=GETDATE()
    WHERE VarCodigo=@cod
ELSE
    INSERT INTO PresupuestoVariables_RM(VarCodigo,VarNombre,VarDescripcion,VarTipoDato,VarAmbitoPermitido,Activo,UsuInicioSesion)
    VALUES(@cod,@nom,@desc,@tipo,@amb,@act,@u)", cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@cod", req.varCodigo.Trim());
            cmd.Parameters.AddWithValue("@nom", req.varNombre);
            cmd.Parameters.AddWithValue("@desc", (object?)req.varDescripcion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tipo", req.varTipoDato);
            cmd.Parameters.AddWithValue("@amb", req.varAmbitoPermitido);
            cmd.Parameters.AddWithValue("@act", req.activo);
            cmd.Parameters.AddWithValue("@u", user);
            cmd.ExecuteNonQuery();
            return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record VarCodigoReq(string varCodigo);

    // ── POST /apipresupuestogestion/variables/definiciones/eliminar ─────────
    [HttpPost("variables/definiciones/eliminar")]
    public IActionResult EliminarDefinicion([FromBody] VarCodigoReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using (var cmdChk = new SqlCommand("SELECT COUNT(*) FROM PresupuestoVariablesValores_RM WHERE VarCodigo=@cod", cn))
            {
                cmdChk.Parameters.AddWithValue("@cod", req.varCodigo);
                if ((int)cmdChk.ExecuteScalar()! > 0)
                    return BadRequest(new { ok = false, error = "Esta variable tiene valores cargados — bórralos primero o desactívala en vez de eliminarla." });
            }
            using var cmd = new SqlCommand("DELETE FROM PresupuestoVariables_RM WHERE VarCodigo=@cod", cn);
            cmd.Parameters.AddWithValue("@cod", req.varCodigo);
            int n = cmd.ExecuteNonQuery();
            return Ok(new { ok = true, eliminado = n > 0 });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apipresupuestogestion/variables/valores?varCodigo=X ────────────
    [HttpGet("variables/valores")]
    public IActionResult ListarValores([FromQuery] string varCodigo)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand(@"
SELECT Id, AmbitoTipo, AmbitoValor, PeriodoTipo, PeriodoAnio, PeriodoTrimestre, PeriodoMes, Valor, UsuInicioSesion, FechaActualizacion
FROM PresupuestoVariablesValores_RM WITH (NOLOCK)
WHERE VarCodigo=@cod
ORDER BY PeriodoAnio DESC, AmbitoTipo, AmbitoValor", cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@cod", varCodigo);
            var lista = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                lista.Add(new
                {
                    Id = rd.GetInt32(0), AmbitoTipo = rd.GetString(1), AmbitoValor = rd.IsDBNull(2) ? null : rd.GetString(2),
                    PeriodoTipo = rd.GetString(3), PeriodoAnio = rd.GetInt16(4),
                    PeriodoTrimestre = rd.IsDBNull(5) ? (int?)null : Convert.ToInt32(rd.GetValue(5)),
                    PeriodoMes = rd.IsDBNull(6) ? (int?)null : Convert.ToInt32(rd.GetValue(6)),
                    Valor = Convert.ToDouble(rd.GetValue(7)), UsuInicioSesion = rd.GetString(8), FechaActualizacion = rd.GetDateTime(9),
                });
            return Ok(new { ok = true, valores = lista });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record ValorReq(int? id, string varCodigo, string ambitoTipo, string? ambitoValor, string periodoTipo, int periodoAnio, int? periodoTrimestre, int? periodoMes, double valor);

    // ── POST /apipresupuestogestion/variables/valores/guardar ───────────────
    // Upsert por Id: sin Id (o Id=0) inserta una fila nueva; con Id existente,
    // actualiza esa fila puntual.
    [HttpPost("variables/valores/guardar")]
    public IActionResult GuardarValor([FromBody] ValorReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
            if (req.id is > 0)
            {
                using var cmd = new SqlCommand(@"
UPDATE PresupuestoVariablesValores_RM
SET AmbitoTipo=@at, AmbitoValor=@av, PeriodoTipo=@pt, PeriodoAnio=@pa, PeriodoTrimestre=@ptr, PeriodoMes=@pm, Valor=@v, UsuInicioSesion=@u, FechaActualizacion=GETDATE()
WHERE Id=@id", cn) { CommandTimeout = 30 };
                cmd.Parameters.AddWithValue("@id", req.id);
                cmd.Parameters.AddWithValue("@at", req.ambitoTipo);
                cmd.Parameters.AddWithValue("@av", (object?)req.ambitoValor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pt", req.periodoTipo);
                cmd.Parameters.AddWithValue("@pa", req.periodoAnio);
                cmd.Parameters.AddWithValue("@ptr", (object?)req.periodoTrimestre ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pm", (object?)req.periodoMes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@v", req.valor);
                cmd.Parameters.AddWithValue("@u", user);
                cmd.ExecuteNonQuery();
                return Ok(new { ok = true, id = req.id });
            }
            else
            {
                using var cmd = new SqlCommand(@"
INSERT INTO PresupuestoVariablesValores_RM(VarCodigo,AmbitoTipo,AmbitoValor,PeriodoTipo,PeriodoAnio,PeriodoTrimestre,PeriodoMes,Valor,UsuInicioSesion)
OUTPUT INSERTED.Id
VALUES(@cod,@at,@av,@pt,@pa,@ptr,@pm,@v,@u)", cn) { CommandTimeout = 30 };
                cmd.Parameters.AddWithValue("@cod", req.varCodigo);
                cmd.Parameters.AddWithValue("@at", req.ambitoTipo);
                cmd.Parameters.AddWithValue("@av", (object?)req.ambitoValor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pt", req.periodoTipo);
                cmd.Parameters.AddWithValue("@pa", req.periodoAnio);
                cmd.Parameters.AddWithValue("@ptr", (object?)req.periodoTrimestre ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pm", (object?)req.periodoMes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@v", req.valor);
                cmd.Parameters.AddWithValue("@u", user);
                var nuevoId = (int)cmd.ExecuteScalar();
                return Ok(new { ok = true, id = nuevoId });
            }
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record IdSoloReq(int id);

    // ── POST /apipresupuestogestion/variables/valores/eliminar ──────────────
    [HttpPost("variables/valores/eliminar")]
    public IActionResult EliminarValor([FromBody] IdSoloReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand("DELETE FROM PresupuestoVariablesValores_RM WHERE Id=@id", cn);
            cmd.Parameters.AddWithValue("@id", req.id);
            int n = cmd.ExecuteNonQuery();
            return Ok(new { ok = true, eliminado = n > 0 });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Rebalancear presupuesto REAL (ya publicado) por Vendedor×Categoría
    // (2026-08-31) — pedido explícito del usuario tras detectar que el
    // presupuesto real de Agosto/Septiembre a menudo concentra TODO el monto
    // de una categoría en un solo producto (a veces uno que ni se vende).
    // Confirmado en vivo: de 8,437 pares Rep×Producto con presupuesto en
    // agosto, 6,562 (78%) no tuvieron NINGUNA venta en mayo-julio 2026 — no es
    // un caso aislado. EXCEPCIÓN DELIBERADA a la regla "solo Publicar toca
    // PresupuestosHistoricos": aquí se corrige presupuesto YA REAL en
    // producción, no una propuesta nuestra — por eso exige escribir
    // "CONFIRMAR" literal antes de aplicar, y siempre hay que pedir el
    // preview primero para ver EXACTAMENTE qué va a cambiar.
    // ══════════════════════════════════════════════════════════════════════

    public sealed record CambioProducto(int ProId, string ProDescripcion, bool EsCombo, double MontoAntes, double MontoDespues, double UnidadesAntes, double UnidadesDespues);
    public sealed record GrupoRebalance(string RepCodigo, string RepNombre, int Cat1Id, string Cat1Nombre, double MontoTotal, bool Anomalia, List<CambioProducto> Cambios);
    public sealed record RebalancearReq(int anio, int mes, List<int>? canales, List<string>? reps = null);
    public sealed record RebalancearAplicarReq(int anio, int mes, List<int>? canales, string confirmacion, List<string>? reps = null);

    // Rango de referencia FIJO para "qué vende realmente" cada vendedor,
    // decidido con el usuario 2026-08-31 (AskUserQuestion): mayo-julio 2026,
    // igual sin importar qué Año-Mes se esté corrigiendo (no es una ventana
    // móvil relativa al mes a corregir).
    private static readonly DateTime RebalanceRefDesde = new(2026, 5, 1);
    private static readonly DateTime RebalanceRefHasta = new(2026, 8, 1);

    private List<GrupoRebalance> CalcularRebalanceCategoria(SqlConnection cn, int anio, int mes, List<int>? canales, List<string>? repsFiltro = null)
    {
        var repsTodos = new List<string>();
        using (var cmd = new SqlCommand("SELECT DISTINCT RepCodigo FROM PresupuestosHistoricos WITH (NOLOCK) WHERE PreTipo='MNTDPROD' AND PreAnio=@anio AND PreMes=@mes", cn) { CommandTimeout = 60 })
        {
            cmd.Parameters.AddWithValue("@anio", anio);
            cmd.Parameters.AddWithValue("@mes", mes);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) repsTodos.Add(rd.GetString(0));
        }
        // Filtro de Vendedor (pedido explícito del usuario 2026-09-08, "incluir
        // representante") — intersección simple contra repsTodos antes del filtro
        // de canal existente.
        if (repsFiltro is { Count: > 0 })
        {
            var repsFiltroSet = repsFiltro.ToHashSet();
            repsTodos = repsTodos.Where(r => repsFiltroSet.Contains(r)).ToList();
        }
        var reps = PresupuestoBuilderCore.FiltrarRepsPorCanal(cn, repsTodos, canales);
        if (reps.Count == 0) return new List<GrupoRebalance>();

        var universo = PresupuestoBuilderCore.UniversoProductosPorRep(cn, reps);
        var pesos = PresupuestoBuilderCore.CalcularPesosReales(cn, RebalanceRefDesde, RebalanceRefHasta, universo, reps);

        // Presupuesto REAL actual (UNDDPROD + MNTDPROD) de esos reps para el Año-Mes a corregir.
        var montoActual = new Dictionary<(string rep, int proId), double>();
        var unidadesActual = new Dictionary<(string rep, int proId), double>();
        var parms = new Dictionary<string, object> { ["@anio"] = anio, ["@mes"] = mes };
        var repNames = reps.Select((r, i) => { var n = $"@r{i}"; parms[n] = r; return n; }).ToList();
        using (var cmd = new SqlCommand($@"
SELECT RepCodigo, PreTipo, PreReferencia, PrePresupuesto
FROM PresupuestosHistoricos WITH (NOLOCK)
WHERE PreTipo IN ('UNDDPROD','MNTDPROD') AND PreAnio=@anio AND PreMes=@mes AND RepCodigo IN ({string.Join(",", repNames)})", cn) { CommandTimeout = 60 })
        {
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                if (!int.TryParse(rd.GetString(2), out var proId)) continue;
                double valor = rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3));
                var key = (rd.GetString(0), proId);
                if (rd.GetString(1) == "MNTDPROD") montoActual[key] = valor; else unidadesActual[key] = valor;
            }
        }

        var proIdsActuales = montoActual.Keys.Select(k => k.proId).Distinct().ToList();
        var prodInfo = new Dictionary<int, (int cat1, string cat1Nombre, string proDesc, bool esCombo)>();
        if (proIdsActuales.Count > 0)
        {
            var parmsP = new Dictionary<string, object>();
            var names = proIdsActuales.Select((p, i) => { var n = $"@p{i}"; parmsP[n] = p; return n; }).ToList();
            using var cmd = new SqlCommand($@"
SELECT pr.ProID, ISNULL(pr.Cat1ID,0), ISNULL(c1.Cat1Descripcion,'(Sin categoría)'), ISNULL(pr.ProDescripcion,'')
FROM Productos pr WITH (NOLOCK)
LEFT JOIN Categoria1 c1 WITH (NOLOCK) ON c1.Cat1ID=pr.Cat1ID
WHERE pr.ProID IN ({string.Join(",", names)})", cn) { CommandTimeout = 30 };
            foreach (var kv in parmsP) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                int proId = rd.GetInt32(0);
                string desc = rd.GetString(3);
                prodInfo[proId] = (rd.GetInt32(1), rd.GetString(2), desc, desc.IndexOf("COMBO", StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        var nombreRep = new Dictionary<string, string>();
        using (var cmd = new SqlCommand($"SELECT RepCodigo, RepNombre FROM Representantes WITH (NOLOCK) WHERE RepCodigo IN ({string.Join(",", reps.Select((r, i) => $"@nr{i}"))})", cn) { CommandTimeout = 30 })
        {
            for (int i = 0; i < reps.Count; i++) cmd.Parameters.AddWithValue($"@nr{i}", reps[i]);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) nombreRep[rd.GetString(0)] = rd.IsDBNull(1) ? "" : rd.GetString(1);
        }

        // Agrupar el presupuesto REAL actual por Rep×Categoría.
        var grupos = new Dictionary<(string rep, int cat1), List<(int proId, double monto)>>();
        foreach (var kv in montoActual)
        {
            if (!prodInfo.TryGetValue(kv.Key.proId, out var pi)) continue;
            var gKey = (kv.Key.rep, pi.cat1);
            if (!grupos.TryGetValue(gKey, out var lista)) { lista = new(); grupos[gKey] = lista; }
            lista.Add((kv.Key.proId, kv.Value));
        }

        var proIdsParaPrecio = pesos.Values.SelectMany(l => l.Select(p => p.ProId)).Concat(proIdsActuales).Distinct().ToList();
        var precios = PresupuestoBuilderCore.PrecioListaPorRepYProducto(cn, reps, proIdsParaPrecio);
        double Precio(string rep, int proId) => precios.GetValueOrDefault((rep, proId), 0);
        double UnidadesDesdeMonto(string rep, int proId, double monto) { var p = Precio(rep, proId); return p > 0 ? monto / p : 0; }

        var resultado = new List<GrupoRebalance>();
        foreach (var (key, productosActuales) in grupos)
        {
            var (rep, cat1) = key;
            double montoTotal = productosActuales.Sum(p => p.monto);
            if (montoTotal <= 0) continue;
            string cat1Nombre = prodInfo.TryGetValue(productosActuales[0].proId, out var piN) ? piN.cat1Nombre : "";

            if (!pesos.TryGetValue(key, out var listaPesos) || listaPesos.Count == 0)
            {
                resultado.Add(new GrupoRebalance(rep, nombreRep.GetValueOrDefault(rep, ""), cat1, cat1Nombre, montoTotal, true, new List<CambioProducto>()));
                continue;
            }

            var actualPorProId = productosActuales.ToDictionary(p => p.proId, p => p.monto);
            double montoCombos = productosActuales.Where(p => prodInfo.GetValueOrDefault(p.proId).esCombo).Sum(p => p.monto);
            double montoRepartible = montoTotal - montoCombos;

            var cambios = new List<CambioProducto>();
            foreach (var p in productosActuales.Where(p => prodInfo.GetValueOrDefault(p.proId).esCombo))
            {
                var pi = prodInfo[p.proId];
                double uni = unidadesActual.GetValueOrDefault((rep, p.proId), 0);
                cambios.Add(new CambioProducto(p.proId, pi.proDesc, true, p.monto, p.monto, uni, uni)); // combos: sin cambio
            }
            var proIdsConPeso = listaPesos.Select(p => p.ProId).ToHashSet();
            foreach (var pp in listaPesos)
            {
                double montoAntes = actualPorProId.GetValueOrDefault(pp.ProId, 0);
                double montoDespues = montoRepartible * pp.PesoPct;
                string desc = prodInfo.TryGetValue(pp.ProId, out var pi2) ? pi2.proDesc : $"Producto {pp.ProId}";
                cambios.Add(new CambioProducto(pp.ProId, desc, false,
                    montoAntes, montoDespues,
                    unidadesActual.GetValueOrDefault((rep, pp.ProId), 0), UnidadesDesdeMonto(rep, pp.ProId, montoDespues)));
            }
            foreach (var p in productosActuales)
            {
                var pi = prodInfo.GetValueOrDefault(p.proId);
                if (pi.esCombo || proIdsConPeso.Contains(p.proId)) continue; // ya cubierto arriba
                cambios.Add(new CambioProducto(p.proId, pi.proDesc, false, p.monto, 0, unidadesActual.GetValueOrDefault((rep, p.proId), 0), 0));
            }

            resultado.Add(new GrupoRebalance(rep, nombreRep.GetValueOrDefault(rep, ""), cat1, cat1Nombre, montoTotal, false, cambios));
        }
        return resultado;
    }

    // ── POST /apipresupuestogestion/historicos/rebalancear-categoria/preview ─
    [HttpPost("historicos/rebalancear-categoria/preview")]
    public IActionResult RebalancearCategoriaPreview([FromBody] RebalancearReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            var grupos = CalcularRebalanceCategoria(cn, req.anio, req.mes, req.canales, req.reps);
            return Ok(new { ok = true, grupos, totalGrupos = grupos.Count, anomalias = grupos.Count(g => g.Anomalia) });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apipresupuestogestion/historicos/rebalancear-categoria/aplicar ─
    // Escribe DIRECTO a producción (PresupuestosHistoricos, sin Destino()) —
    // ver nota de arquitectura arriba. Exige 'confirmacion'="CONFIRMAR" literal.
    [HttpPost("historicos/rebalancear-categoria/aplicar")]
    public IActionResult RebalancearCategoriaAplicar([FromBody] RebalancearAplicarReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        if (req.confirmacion != "CONFIRMAR")
            return BadRequest(new { ok = false, error = "Debes escribir CONFIRMAR (así, en mayúsculas) para aplicar este cambio a producción." });
        var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
        try
        {
            using var cn = NewConn(); cn.Open();
            var grupos = CalcularRebalanceCategoria(cn, req.anio, req.mes, req.canales, req.reps);
            using var tx = cn.BeginTransaction();
            try
            {
                int filasActualizadas = 0;
                foreach (var g in grupos.Where(g => !g.Anomalia))
                {
                    foreach (var c in g.Cambios)
                    {
                        if (c.EsCombo) continue;
                        UpsertPresupuesto(cn, tx, g.RepCodigo, "UNDDPROD", req.anio, req.mes, c.ProId, c.UnidadesDespues, user);
                        UpsertPresupuesto(cn, tx, g.RepCodigo, "MNTDPROD", req.anio, req.mes, c.ProId, c.MontoDespues, user);
                        filasActualizadas += 2;
                    }
                }
                using (var cmdLog = new SqlCommand(@"
INSERT INTO PresupuestoPublicaciones_RM(LoteId,UsuInicioSesion,FilasAfectadas,SumaTotal,Detalle)
VALUES(NULL,@u,@filas,@suma,@detalle)", cn, tx))
                {
                    cmdLog.Parameters.AddWithValue("@u", user);
                    cmdLog.Parameters.AddWithValue("@filas", filasActualizadas);
                    cmdLog.Parameters.AddWithValue("@suma", grupos.Where(g => !g.Anomalia).Sum(g => g.MontoTotal));
                    cmdLog.Parameters.AddWithValue("@detalle", $"Rebalanceo MNTDPROD/UNDDPROD por Categoría real, {req.anio}-{req.mes:00}: {grupos.Count(g => !g.Anomalia)} grupos ajustados, {grupos.Count(g => g.Anomalia)} anomalías sin tocar.");
                    cmdLog.ExecuteNonQuery();
                }
                tx.Commit();
                return Ok(new { ok = true, filasActualizadas, gruposActualizados = grupos.Count(g => !g.Anomalia), anomalias = grupos.Count(g => g.Anomalia) });
            }
            catch { tx.Rollback(); throw; }
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    private static void UpsertPresupuesto(SqlConnection cn, SqlTransaction tx, string rep, string preTipo, int anio, int mes, int proId, double valor, string user)
    {
        using var cmdUpd = new SqlCommand(@"
UPDATE PresupuestosHistoricos SET PrePresupuesto=@v, UsuInicioSesion=@u, PreFechaActualizacion=GETDATE()
WHERE RepCodigo=@rep AND PreTipo=@tipo AND PreAnio=@anio AND PreMes=@mes AND PreReferencia=@ref", cn, tx);
        cmdUpd.Parameters.AddWithValue("@v", Math.Round(valor, 2));
        cmdUpd.Parameters.AddWithValue("@u", user);
        cmdUpd.Parameters.AddWithValue("@rep", rep);
        cmdUpd.Parameters.AddWithValue("@tipo", preTipo);
        cmdUpd.Parameters.AddWithValue("@anio", anio);
        cmdUpd.Parameters.AddWithValue("@mes", mes);
        cmdUpd.Parameters.AddWithValue("@ref", proId.ToString());
        int n = cmdUpd.ExecuteNonQuery();
        if (n == 0)
        {
            using var cmdIns = new SqlCommand(@"
INSERT INTO PresupuestosHistoricos(RepCodigo,PreTipo,PreAnio,PreMes,PreReferencia,PrePresupuesto,PreEjecutado,UsuInicioSesion,PreFechaActualizacion,rowguid,mbversion,RepSupervisor)
VALUES(@rep,@tipo,@anio,@mes,@ref,@v,0,@u,GETDATE(),NEWID(),'PresupSmartReb',NULL)", cn, tx);
            cmdIns.Parameters.AddWithValue("@rep", rep);
            cmdIns.Parameters.AddWithValue("@tipo", preTipo);
            cmdIns.Parameters.AddWithValue("@anio", anio);
            cmdIns.Parameters.AddWithValue("@mes", mes);
            cmdIns.Parameters.AddWithValue("@ref", proId.ToString());
            cmdIns.Parameters.AddWithValue("@v", Math.Round(valor, 2));
            cmdIns.Parameters.AddWithValue("@u", user);
            cmdIns.ExecuteNonQuery();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // ⚙ Metas de activación mínima por Suplidor×Categoría (2026-08-30) — mismo
    // patrón CRUD de arriba, tabla propia (PresupuestoActivacionMetas_RM)
    // porque su forma (Cat3ID+Cat1ID+MetaPct+Modo+BloqueaPublicar) no encaja en
    // el esquema genérico Ambito/Periodo de PresupuestoVariablesValores_RM.
    // ══════════════════════════════════════════════════════════════════════

    // ── GET /apipresupuestogestion/activacion/metas ─────────────────────────
    [HttpGet("activacion/metas")]
    public IActionResult ListarActivacionMetas()
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand(@"
SELECT m.Id, m.Cat3ID, ISNULL(c3.Cat3Descripcion,'(?)') Cat3Nombre, m.Cat1ID, ISNULL(c1.Cat1Descripcion,'(?)') Cat1Nombre,
       m.MetaPct, m.Modo, m.BloqueaPublicar, m.Activo, m.CanID, ISNULL(can.CanDescripcion,''), m.RepTipe, m.RepCodigo, ISNULL(rp.RepNombre,'')
FROM PresupuestoActivacionMetas_RM m WITH (NOLOCK)
LEFT JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID=m.Cat3ID
LEFT JOIN Categoria1 c1 WITH (NOLOCK) ON c1.Cat1ID=m.Cat1ID
LEFT JOIN CanalDistribucion can WITH (NOLOCK) ON can.CanID=m.CanID
LEFT JOIN Representantes rp WITH (NOLOCK) ON rp.RepCodigo=m.RepCodigo
ORDER BY Cat3Nombre, Cat1Nombre", cn) { CommandTimeout = 30 };
            var lista = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                lista.Add(new
                {
                    Id = rd.GetInt32(0), Cat3ID = rd.GetInt32(1), Cat3Nombre = rd.GetString(2),
                    Cat1ID = rd.GetInt32(3), Cat1Nombre = rd.GetString(4),
                    MetaPct = Convert.ToDouble(rd.GetValue(5)), Modo = rd.GetString(6),
                    BloqueaPublicar = rd.GetBoolean(7), Activo = rd.GetBoolean(8),
                    CanID = rd.IsDBNull(9) ? (int?)null : rd.GetInt32(9), CanNombre = rd.GetString(10),
                    RepTipe = rd.IsDBNull(11) ? null : rd.GetString(11),
                    RepCodigo = rd.IsDBNull(12) ? null : rd.GetString(12), RepNombre = rd.GetString(13),
                });
            return Ok(new { ok = true, metas = lista });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // canId/repTipe/repCodigo (2026-09-01): opcionales — a lo sumo UNO de los
    // tres debe venir con valor (son capas de override alternativas, no combinadas).
    public sealed record ActivacionMetaReq(int? id, int cat3Id, int cat1Id, double metaPct, string modo, bool bloqueaPublicar, bool activo, int? canId, string? repTipe, string? repCodigo);

    // ── POST /apipresupuestogestion/activacion/metas/guardar ────────────────
    [HttpPost("activacion/metas/guardar")]
    public IActionResult GuardarActivacionMeta([FromBody] ActivacionMetaReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        if (req.modo != "Ideal" && req.modo != "Obligatorio") return BadRequest(new { ok = false, error = "Modo debe ser 'Ideal' u 'Obligatorio'." });
        var repTipe = string.IsNullOrEmpty(req.repTipe) ? null : req.repTipe;
        var repCodigo = string.IsNullOrEmpty(req.repCodigo) ? null : req.repCodigo;
        var overridesLlenos = (req.canId != null ? 1 : 0) + (repTipe != null ? 1 : 0) + (repCodigo != null ? 1 : 0);
        if (overridesLlenos > 1) return BadRequest(new { ok = false, error = "Canal, División y Vendedor son overrides alternativos — llena solo uno de los tres, no varios." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
            if (req.id is > 0)
            {
                using var cmd = new SqlCommand(@"
UPDATE PresupuestoActivacionMetas_RM
SET Cat3ID=@c3, Cat1ID=@c1, MetaPct=@meta, Modo=@modo, BloqueaPublicar=@bloq, Activo=@act, UsuInicioSesion=@u, FechaActualizacion=GETDATE(), CanID=@can, RepTipe=@rt, RepCodigo=@rc
WHERE Id=@id", cn) { CommandTimeout = 30 };
                cmd.Parameters.AddWithValue("@id", req.id);
                cmd.Parameters.AddWithValue("@c3", req.cat3Id); cmd.Parameters.AddWithValue("@c1", req.cat1Id);
                cmd.Parameters.AddWithValue("@meta", req.metaPct); cmd.Parameters.AddWithValue("@modo", req.modo);
                cmd.Parameters.AddWithValue("@bloq", req.bloqueaPublicar); cmd.Parameters.AddWithValue("@act", req.activo);
                cmd.Parameters.AddWithValue("@u", user);
                cmd.Parameters.AddWithValue("@can", (object?)req.canId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rt", (object?)repTipe ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rc", (object?)repCodigo ?? DBNull.Value);
                cmd.ExecuteNonQuery();
                return Ok(new { ok = true, id = req.id });
            }
            else
            {
                using var cmd = new SqlCommand(@"
INSERT INTO PresupuestoActivacionMetas_RM(Cat3ID,Cat1ID,MetaPct,Modo,BloqueaPublicar,Activo,UsuInicioSesion,CanID,RepTipe,RepCodigo)
OUTPUT INSERTED.Id
VALUES(@c3,@c1,@meta,@modo,@bloq,@act,@u,@can,@rt,@rc)", cn) { CommandTimeout = 30 };
                cmd.Parameters.AddWithValue("@c3", req.cat3Id); cmd.Parameters.AddWithValue("@c1", req.cat1Id);
                cmd.Parameters.AddWithValue("@meta", req.metaPct); cmd.Parameters.AddWithValue("@modo", req.modo);
                cmd.Parameters.AddWithValue("@bloq", req.bloqueaPublicar); cmd.Parameters.AddWithValue("@act", req.activo);
                cmd.Parameters.AddWithValue("@u", user);
                cmd.Parameters.AddWithValue("@can", (object?)req.canId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rt", (object?)repTipe ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rc", (object?)repCodigo ?? DBNull.Value);
                var nuevoId = (int)cmd.ExecuteScalar();
                return Ok(new { ok = true, id = nuevoId });
            }
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apipresupuestogestion/activacion/metas/eliminar ───────────────
    [HttpPost("activacion/metas/eliminar")]
    public IActionResult EliminarActivacionMeta([FromBody] IdSoloReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand("DELETE FROM PresupuestoActivacionMetas_RM WHERE Id=@id", cn);
            cmd.Parameters.AddWithValue("@id", req.id);
            int n = cmd.ExecuteNonQuery();
            return Ok(new { ok = true, eliminado = n > 0 });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // ⚙ Modelo Rappel generalizado (2026-09-06, pedido explícito del usuario)
    // — Monto/Activaciones/Factura por Suplidor O Categoría, tabla propia
    // (PresupuestoRappelMetas_RM) — NO reemplaza META_RAPPEL (catálogo
    // genérico, sigue funcionando igual), es ADICIONAL. Mismo patrón CRUD que
    // Activación/Efectividad/Límites.
    // ══════════════════════════════════════════════════════════════════════

    // ── GET /apipresupuestogestion/rappel/metas ─────────────────────────────
    [HttpGet("rappel/metas")]
    public IActionResult ListarRappelMetas()
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand(@"
SELECT m.Id, m.TipoRequerimiento, m.AmbitoTipo, m.Cat3ID, ISNULL(c3.Cat3Descripcion,'(?)') Cat3Nombre,
       m.Cat1ID, ISNULL(c1.Cat1Descripcion,'(?)') Cat1Nombre, m.PeriodoAnio, m.PeriodoMes, m.MetaValor, m.Activo
FROM PresupuestoRappelMetas_RM m WITH (NOLOCK)
LEFT JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID=m.Cat3ID
LEFT JOIN Categoria1 c1 WITH (NOLOCK) ON c1.Cat1ID=m.Cat1ID
ORDER BY m.PeriodoAnio DESC, m.TipoRequerimiento, Cat3Nombre, Cat1Nombre", cn) { CommandTimeout = 30 };
            var lista = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                lista.Add(new
                {
                    Id = rd.GetInt32(0), TipoRequerimiento = rd.GetString(1), AmbitoTipo = rd.GetString(2),
                    Cat3ID = rd.IsDBNull(3) ? (int?)null : rd.GetInt32(3), Cat3Nombre = rd.GetString(4),
                    Cat1ID = rd.IsDBNull(5) ? (int?)null : rd.GetInt32(5), Cat1Nombre = rd.GetString(6),
                    PeriodoAnio = rd.GetInt16(7), PeriodoMes = rd.IsDBNull(8) ? (int?)null : Convert.ToInt32(rd.GetValue(8)),
                    MetaValor = Convert.ToDouble(rd.GetValue(9)), Activo = rd.GetBoolean(10),
                });
            return Ok(new { ok = true, metas = lista });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record RappelMetaReq(int? id, string tipoRequerimiento, string ambitoTipo, int? cat3Id, int? cat1Id, int periodoAnio, int? periodoMes, double metaValor, bool activo);

    // ── POST /apipresupuestogestion/rappel/metas/guardar ────────────────────
    [HttpPost("rappel/metas/guardar")]
    public IActionResult GuardarRappelMeta([FromBody] RappelMetaReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        if (req.tipoRequerimiento != "Monto" && req.tipoRequerimiento != "Activaciones" && req.tipoRequerimiento != "Factura")
            return BadRequest(new { ok = false, error = "TipoRequerimiento debe ser 'Monto', 'Activaciones' o 'Factura'." });
        if (req.ambitoTipo != "Suplidor" && req.ambitoTipo != "Categoria")
            return BadRequest(new { ok = false, error = "AmbitoTipo debe ser 'Suplidor' o 'Categoria'." });
        if (req.ambitoTipo == "Suplidor" && req.cat3Id is not > 0)
            return BadRequest(new { ok = false, error = "Falta Cat3ID (Suplidor)." });
        if (req.ambitoTipo == "Categoria" && req.cat1Id is not > 0)
            return BadRequest(new { ok = false, error = "Falta Cat1ID (Categoría)." });
        int? cat3 = req.ambitoTipo == "Suplidor" ? req.cat3Id : null;
        int? cat1 = req.ambitoTipo == "Categoria" ? req.cat1Id : null;
        try
        {
            using var cn = NewConn(); cn.Open();
            var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
            if (req.id is > 0)
            {
                using var cmd = new SqlCommand(@"
UPDATE PresupuestoRappelMetas_RM
SET TipoRequerimiento=@tipo, AmbitoTipo=@amb, Cat3ID=@c3, Cat1ID=@c1, PeriodoAnio=@anio, PeriodoMes=@mes, MetaValor=@val, Activo=@act, UsuInicioSesion=@u, FechaActualizacion=GETDATE()
WHERE Id=@id", cn) { CommandTimeout = 30 };
                cmd.Parameters.AddWithValue("@id", req.id);
                cmd.Parameters.AddWithValue("@tipo", req.tipoRequerimiento); cmd.Parameters.AddWithValue("@amb", req.ambitoTipo);
                cmd.Parameters.AddWithValue("@c3", (object?)cat3 ?? DBNull.Value); cmd.Parameters.AddWithValue("@c1", (object?)cat1 ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@anio", req.periodoAnio); cmd.Parameters.AddWithValue("@mes", (object?)req.periodoMes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@val", req.metaValor); cmd.Parameters.AddWithValue("@act", req.activo);
                cmd.Parameters.AddWithValue("@u", user);
                cmd.ExecuteNonQuery();
                return Ok(new { ok = true, id = req.id });
            }
            else
            {
                using var cmd = new SqlCommand(@"
INSERT INTO PresupuestoRappelMetas_RM(TipoRequerimiento,AmbitoTipo,Cat3ID,Cat1ID,PeriodoAnio,PeriodoMes,MetaValor,Activo,UsuInicioSesion)
OUTPUT INSERTED.Id
VALUES(@tipo,@amb,@c3,@c1,@anio,@mes,@val,@act,@u)", cn) { CommandTimeout = 30 };
                cmd.Parameters.AddWithValue("@tipo", req.tipoRequerimiento); cmd.Parameters.AddWithValue("@amb", req.ambitoTipo);
                cmd.Parameters.AddWithValue("@c3", (object?)cat3 ?? DBNull.Value); cmd.Parameters.AddWithValue("@c1", (object?)cat1 ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@anio", req.periodoAnio); cmd.Parameters.AddWithValue("@mes", (object?)req.periodoMes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@val", req.metaValor); cmd.Parameters.AddWithValue("@act", req.activo);
                cmd.Parameters.AddWithValue("@u", user);
                var nuevoId = (int)cmd.ExecuteScalar();
                return Ok(new { ok = true, id = nuevoId });
            }
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apipresupuestogestion/rappel/metas/eliminar ───────────────────
    [HttpPost("rappel/metas/eliminar")]
    public IActionResult EliminarRappelMeta([FromBody] IdSoloReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand("DELETE FROM PresupuestoRappelMetas_RM WHERE Id=@id", cn);
            cmd.Parameters.AddWithValue("@id", req.id);
            int n = cmd.ExecuteNonQuery();
            return Ok(new { ok = true, eliminado = n > 0 });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // ⚙ Metas de efectividad mínima por División/RepTipe (2026-09-01, Fase B)
    // — mismo patrón CRUD que Activación, tabla propia
    // (PresupuestoEfectividadMetas_RM), 1 sola dimensión (RepTipe).
    // ══════════════════════════════════════════════════════════════════════

    // ── GET /apipresupuestogestion/efectividad/metas ────────────────────────
    [HttpGet("efectividad/metas")]
    public IActionResult ListarEfectividadMetas()
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand("SELECT Id, RepTipe, MetaPct, Modo, BloqueaPublicar, Activo FROM PresupuestoEfectividadMetas_RM WITH (NOLOCK) ORDER BY RepTipe", cn) { CommandTimeout = 30 };
            var lista = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                lista.Add(new
                {
                    Id = rd.GetInt32(0), RepTipe = rd.GetString(1),
                    MetaPct = Convert.ToDouble(rd.GetValue(2)), Modo = rd.GetString(3),
                    BloqueaPublicar = rd.GetBoolean(4), Activo = rd.GetBoolean(5),
                });
            return Ok(new { ok = true, metas = lista });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record EfectividadMetaReq(int? id, string repTipe, double metaPct, string modo, bool bloqueaPublicar, bool activo);

    // ── POST /apipresupuestogestion/efectividad/metas/guardar ───────────────
    [HttpPost("efectividad/metas/guardar")]
    public IActionResult GuardarEfectividadMeta([FromBody] EfectividadMetaReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        if (req.modo != "Ideal" && req.modo != "Obligatorio") return BadRequest(new { ok = false, error = "Modo debe ser 'Ideal' u 'Obligatorio'." });
        var repTipe = req.repTipe ?? ""; // "" = fila default (todas las divisiones sin fila propia)
        try
        {
            using var cn = NewConn(); cn.Open();
            var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
            if (req.id is > 0)
            {
                using var cmd = new SqlCommand(@"
UPDATE PresupuestoEfectividadMetas_RM
SET RepTipe=@rt, MetaPct=@meta, Modo=@modo, BloqueaPublicar=@bloq, Activo=@act, UsuInicioSesion=@u, FechaActualizacion=GETDATE()
WHERE Id=@id", cn) { CommandTimeout = 30 };
                cmd.Parameters.AddWithValue("@id", req.id);
                cmd.Parameters.AddWithValue("@rt", repTipe);
                cmd.Parameters.AddWithValue("@meta", req.metaPct); cmd.Parameters.AddWithValue("@modo", req.modo);
                cmd.Parameters.AddWithValue("@bloq", req.bloqueaPublicar); cmd.Parameters.AddWithValue("@act", req.activo);
                cmd.Parameters.AddWithValue("@u", user);
                cmd.ExecuteNonQuery();
                return Ok(new { ok = true, id = req.id });
            }
            else
            {
                using var cmd = new SqlCommand(@"
INSERT INTO PresupuestoEfectividadMetas_RM(RepTipe,MetaPct,Modo,BloqueaPublicar,Activo,UsuInicioSesion)
OUTPUT INSERTED.Id
VALUES(@rt,@meta,@modo,@bloq,@act,@u)", cn) { CommandTimeout = 30 };
                cmd.Parameters.AddWithValue("@rt", repTipe);
                cmd.Parameters.AddWithValue("@meta", req.metaPct); cmd.Parameters.AddWithValue("@modo", req.modo);
                cmd.Parameters.AddWithValue("@bloq", req.bloqueaPublicar); cmd.Parameters.AddWithValue("@act", req.activo);
                cmd.Parameters.AddWithValue("@u", user);
                var nuevoId = (int)cmd.ExecuteScalar();
                return Ok(new { ok = true, id = nuevoId });
            }
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apipresupuestogestion/efectividad/metas/eliminar ──────────────
    [HttpPost("efectividad/metas/eliminar")]
    public IActionResult EliminarEfectividadMeta([FromBody] IdSoloReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand("DELETE FROM PresupuestoEfectividadMetas_RM WHERE Id=@id", cn);
            cmd.Parameters.AddWithValue("@id", req.id);
            int n = cmd.ExecuteNonQuery();
            return Ok(new { ok = true, eliminado = n > 0 });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // ⚙ Límites (piso/techo) por vendedor, % sobre su propia base (2026-09-01)
    // — PK es el propio RepCodigo (1 fila por vendedor, no hace falta Id),
    // así que es un simple upsert en vez del patrón alta/edición por Id de
    // arriba. Ver SQL/PresupuestoLimitesRep_RM.sql.
    // ══════════════════════════════════════════════════════════════════════

    // ── GET /apipresupuestogestion/limitesrep ────────────────────────────────
    [HttpGet("limitesrep")]
    public IActionResult ListarLimitesRep()
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand(@"
SELECT l.RepCodigo, ISNULL(r.RepNombre,'(?)'), l.PctMinimo, l.PctMaximo, l.Activo
FROM PresupuestoLimitesRep_RM l WITH (NOLOCK)
LEFT JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=l.RepCodigo
ORDER BY r.RepNombre", cn) { CommandTimeout = 30 };
            var lista = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                lista.Add(new
                {
                    RepCodigo = rd.GetString(0), RepNombre = rd.GetString(1),
                    PctMinimo = rd.IsDBNull(2) ? (double?)null : Convert.ToDouble(rd.GetValue(2)),
                    PctMaximo = rd.IsDBNull(3) ? (double?)null : Convert.ToDouble(rd.GetValue(3)),
                    Activo = rd.GetBoolean(4),
                });
            return Ok(new { ok = true, limites = lista });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record LimiteRepReq(string repCodigo, double? pctMinimo, double? pctMaximo, bool activo);

    // ── POST /apipresupuestogestion/limitesrep/guardar ──────────────────────
    [HttpPost("limitesrep/guardar")]
    public IActionResult GuardarLimiteRep([FromBody] LimiteRepReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(req.repCodigo)) return BadRequest(new { ok = false, error = "RepCodigo requerido." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var user = HttpContext.Session.GetString("user_login") ?? "desconocido";
            using var cmd = new SqlCommand(@"
MERGE PresupuestoLimitesRep_RM AS t
USING (SELECT @rep AS RepCodigo) AS s ON t.RepCodigo=s.RepCodigo
WHEN MATCHED THEN UPDATE SET PctMinimo=@min, PctMaximo=@max, Activo=@act, UsuInicioSesion=@u, FechaActualizacion=GETDATE()
WHEN NOT MATCHED THEN INSERT(RepCodigo,PctMinimo,PctMaximo,Activo,UsuInicioSesion) VALUES(@rep,@min,@max,@act,@u);", cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@rep", req.repCodigo);
            cmd.Parameters.AddWithValue("@min", (object?)req.pctMinimo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@max", (object?)req.pctMaximo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@act", req.activo);
            cmd.Parameters.AddWithValue("@u", user);
            cmd.ExecuteNonQuery();
            return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record RepCodigoSoloReq(string repCodigo);

    // ── POST /apipresupuestogestion/limitesrep/eliminar ─────────────────────
    [HttpPost("limitesrep/eliminar")]
    public IActionResult EliminarLimiteRep([FromBody] RepCodigoSoloReq req)
    {
        using var cn0 = NewConn(); cn0.Open();
        var deny = RequireGerencia(cn0); if (deny != null) return deny;
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand("DELETE FROM PresupuestoLimitesRep_RM WHERE RepCodigo=@rep", cn);
            cmd.Parameters.AddWithValue("@rep", req.repCodigo);
            int n = cmd.ExecuteNonQuery();
            return Ok(new { ok = true, eliminado = n > 0 });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }
}
