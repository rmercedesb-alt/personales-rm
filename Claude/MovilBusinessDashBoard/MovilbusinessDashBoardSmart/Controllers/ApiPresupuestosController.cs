using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

// ── API del módulo Presupuestos ─────────────────────────────────────────────
// Licencia (2026-09-30): gateado bajo "presupuestos", EXCEPTO Filtros() (ver
// [SinGateDeLicencia] en esa acción — Supervisión Dinámica y Presupuestos-Gestión
// también la usan solo para poblar listas de Canal/Suplidor/Categoría/Supervisor, no
// para ver datos de presupuesto en sí).
[ApiController]
[Route("apipresupuestos")]
[RequiereModulo("presupuestos")]
public class ApiPresupuestosController(IConfiguration config, IMemoryCache cache, ILogger<ApiPresupuestosController> logger) : ControllerBase
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));
    private bool IsAuth => HttpContext.Session.GetString("user_login") is not null;

    // Candado por clave de caché (compartido entre TODAS las requests concurrentes,
    // static = 1 solo diccionario para todo el proceso). Evita "cache stampede": el
    // usuario reportó 2026-08-29 que Dashboard Gráficos tardaba varios minutos —
    // verificado en vivo con Puppeteer que la causa real era que un solo clic de
    // "Consultar" dispara Resumen + Detalle (Por Organización) en paralelo, y cuando
    // ambos piden la MISMA curva de estacionalidad no cacheada (caso normal: "General",
    // sin Suplidor/Categoría exacta -> misma clave "curva:general:{anio}{mes}") con
    // IMemoryCache.GetOrCreate plano, los 2 requests ven la caché vacía A LA VEZ y
    // ambos ejecutan la consulta pesada (~17s contra VWVentas) EN PARALELO — duplicando
    // el costo y saturando la BD compartida, atrasando todo lo demás corriendo al mismo
    // tiempo (incluidas las consultas de Dashboard Gráficos, que en sí mismas ya son
    // rápidas). Con este candado, el 2º request que pide la misma clave ESPERA a que el
    // 1º termine de calcularla y reusa el resultado, en vez de recalcularlo.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new();
    private T ObtenerCacheado<T>(string clave, Func<T> calcular, TimeSpan expiracion)
    {
        if (cache.TryGetValue(clave, out T? valor)) return valor!;
        var sem = _cacheLocks.GetOrAdd(clave, _ => new SemaphoreSlim(1, 1));
        sem.Wait();
        try
        {
            if (cache.TryGetValue(clave, out valor)) return valor!; // otro request ya la calculó mientras esperábamos
            valor = calcular();
            cache.Set(clave, valor, expiracion);
            return valor!;
        }
        finally { sem.Release(); }
    }

    // Limita cuántos cálculos de curva PESADOS (CurvaPorFranjas contra VWVentas, ~17s cada
    // uno la 1ª vez) corren en PARALELO en TODO el proceso — a diferencia de _cacheLocks
    // (que solo evita recalcular la MISMA clave 2 veces), este candado es GLOBAL y frena
    // aunque las claves sean DISTINTAS. Motivo (pedido del usuario 2026-09-24,
    // "eficientizar" el tiempo de Presupuestos): "Del Mes" con 2+ meses (o "Del Año", hasta
    // 8 meses) dispara varios requests en paralelo que casi nunca comparten clave (cada
    // mes/Canal/Categoría es su propia clave "curva:tipo:clave:canN:aaaamm") — si TODAS
    // caen en cache miss a la vez, cada una ejecuta su propio escaneo pesado de VWVentas
    // AL MISMO TIEMPO, saturando la BD compartida (mismo patrón raíz ya diagnosticado
    // 2026-08-29 para "Dashboard Gráficos tardaba varios minutos" — ahí las claves SÍ
    // coincidían y ya se resolvió con _cacheLocks; acá el disparador es distinto —
    // multi-mes con claves distintas— pero la causa de fondo es la misma saturación).
    // Con este tope, como mucho 2 corren a la vez; el resto espera su turno en vez de
    // competir por el mismo I/O y CPU de SQL Server, evitando que CADA UNA tarde más de
    // lo que tardaría sola.
    private static readonly SemaphoreSlim _curvaThrottleGlobal = new(2, 2);

    // Como ObtenerCacheado, pero para curvas de estacionalidad específicamente — además
    // de memoria (24h), lee/escribe PresupuestosCurvas_RM (persistente, sobrevive
    // reciclados de IIS) — pedido explícito del usuario 2026-08-29. Orden de búsqueda:
    // memoria -> tabla -> calcular (VWVentas, caro) y guardar en ambas. "tipo"/"claveRef"
    // arman la misma clave que ya usaba ObtenerCurva (curva:{tipo}:{claveRef}:{anio}{mes}),
    // solo separada en columnas para poder guardarla en la tabla.
    // canId (default 0 = todos los canales, pedido 2026-09-05): eje adicional de la clave,
    // ver SQL/PresupuestosCurvas_RM_Canal.sql — con 0 el comportamiento es idéntico a antes.
    private double[]? ObtenerCurvaConPersistencia(SqlConnection cn, string tipo, string claveRef, int anio, int mes, Func<double[]?> calcular, int canId = 0)
    {
        var claveMem = $"curva:{tipo}:{claveRef}:can{canId}:{anio}{mes:00}";
        if (cache.TryGetValue(claveMem, out double[]? enMemoria)) return enMemoria;
        var sem = _cacheLocks.GetOrAdd(claveMem, _ => new SemaphoreSlim(1, 1));
        sem.Wait();
        try
        {
            if (cache.TryGetValue(claveMem, out enMemoria)) return enMemoria;
            var enTabla = PresupuestosCore.LeerCurvaDeTabla(cn, tipo, claveRef, anio, mes, canId, logger);
            if (enTabla != null) { cache.Set(claveMem, enTabla, TimeSpan.FromHours(24)); return enTabla; }
            // Cache miss en memoria Y en tabla -> toca calcular contra VWVentas (lo caro,
            // ~17s la 1ª vez, según el estimado histórico — nunca confirmado con logging
            // real hasta ahora). Pasa por el candado GLOBAL (_curvaThrottleGlobal) para no
            // competir con otras claves calculándose a la vez en requests paralelos (ver
            // comentario de _curvaThrottleGlobal más arriba). Se miden POR SEPARADO el
            // tiempo de ESPERA por el candado (cuántas otras curvas iban delante) del
            // tiempo de CÁLCULO real contra VWVentas — pedido del usuario 2026-09-25 tras
            // reportar que, incluso con el candado ya puesto, una consulta "sencilla"
            // seguía tardando minutos: sin este desglose no hay forma de saber si el
            // cuello de botella es "cola larga esperando su turno" (candado demasiado
            // estricto, subir el límite de 2) o "la consulta en sí es lenta" (algo que
            // revisar con TI en la BD — índices, bloqueos, volumen de datos).
            var swEspera = System.Diagnostics.Stopwatch.StartNew();
            _curvaThrottleGlobal.Wait();
            swEspera.Stop();
            var swCalc = System.Diagnostics.Stopwatch.StartNew();
            double[]? valor;
            try { valor = calcular(); }
            finally { _curvaThrottleGlobal.Release(); }
            swCalc.Stop();
            logger.LogInformation("Curva {Clave}: esperó {EsperaMs}ms por el candado global + {CalcMs}ms calculando contra VWVentas (cache/tabla en frío)", claveMem, swEspera.ElapsedMilliseconds, swCalc.ElapsedMilliseconds);
            if (valor != null)
            {
                PresupuestosCore.GuardarCurvaEnTabla(cn, tipo, claveRef, anio, mes, valor, canId, logger);
                cache.Set(claveMem, valor, TimeSpan.FromHours(24));
            }
            return valor;
        }
        finally { sem.Release(); }
    }

    // internal (no solo private): reusado tal cual por ApiPdfPresController (módulo "PDF
    // Presentaciones" en Supervisión Dinámica, 2026-08-30) para no reimplementar el
    // cruce Suplidor↔Categoría↔Producto ya depurado aquí — cero cambio de comportamiento,
    // solo visibilidad.
    internal static List<string> ParseList(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? new() :
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    internal static string BuildIn(string column, List<string> vals, string prefix, Dictionary<string, object> parms)
    {
        if (vals.Count == 0) return "";
        var names = vals.Select((v, i) => { var n = $"@{prefix}{i}"; parms[n] = v; return n; }).ToList();
        return $"AND {column} IN ({string.Join(",", names)})";
    }

    // Catálogo estático de familias — qué KPI tiene cada una y si tiene variante diaria
    // (deriva directo de PresupuestosCore.Mapa, no se hardcodea dos veces).
    private static readonly (string cod, string label)[] FamiliaLabels =
    {
        ("general", "General (monto/unidades del vendedor)"),
        ("categoria", "Categoría"),
        ("suplidor", "Suplidor"),
        ("producto", "Producto"),
        ("grupoproducto", "Grupo de Producto"),
    };

    // ── GET /apipresupuestos/filtros ────────────────────────────────────────
    // Cacheado en memoria (1h, con candado anti-stampede vía ObtenerCacheado) — pedido
    // explícito del usuario 2026-09-03: "dura mucho cargando los filtros" en Cierre
    // Diario/Ventas Proyectadas/Presupuestos, los 3 módulos que llaman este mismo
    // endpoint. Medido en vivo contra producción: ~740ms sumando las 6 consultas (nada
    // individualmente lento, pero es trabajo repetido en CADA carga de página sin
    // necesidad — el catálogo de Suplidores/Categorías/Representantes/Años casi nunca
    // cambia dentro del mismo día). 1h de vida: balance entre "instantáneo casi siempre"
    // y no quedar desactualizado por mucho tiempo si TI carga presupuesto nuevo.
    [HttpGet("filtros")]
    [SinGateDeLicencia]
    public IActionResult Filtros()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            var resultado = ObtenerCacheado("filtros:catalogo", () =>
            {
            using var cn = NewConn();
            cn.Open();

            var reps = new List<object>();
            using (var cmd = new SqlCommand(@"
SELECT r.RepCodigo, r.RepNombre, ISNULL(r.CedCodigo,'') CedCodigo, ISNULL(cd.CedDescripcion,ISNULL(r.CedCodigo,'(Sin centro)')) CentroNombre,
       ISNULL(r.RepSupervisor,'') SupCod, ISNULL(sup.RepNombre,r.RepSupervisor) SupNombre,
       ISNULL(ra.RepTipe,'') Division, ISNULL(ra.CanID,0) CanID
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor'
ORDER BY CentroNombre, SupNombre, r.RepNombre", cn) { CommandTimeout = 30 })
            {
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    reps.Add(new
                    {
                        RepCodigo = rd.GetString(0), RepNombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                        CedCodigo = rd.GetString(2), CentroNombre = rd.GetString(3),
                        SupervisorCod = rd.GetString(4), SupervisorNombre = rd.IsDBNull(5) ? "" : rd.GetString(5),
                        Division = rd.GetString(6), CanID = rd.GetInt32(7),
                    });
            }

            var canales = new List<object>();
            using (var cmd = new SqlCommand("SELECT CanID, CanDescripcion FROM CanalDistribucion WITH (NOLOCK) WHERE CanDescripcion IS NOT NULL ORDER BY CanDescripcion", cn) { CommandTimeout = 30 })
            {
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) canales.Add(new { v = rd.GetInt32(0).ToString(), t = rd.GetString(1) });
            }

            // Suplidor/Categoría/Producto: SOLO lo que tiene datos reales en PresupuestosHistoricos
            // (pedido explícito del usuario — no mostrar catálogo completo si no hay presupuesto/venta
            // cargado para ese ítem). Acotado a los últimos 2 años para performance (probado <250ms;
            // sin acotar, un DISTINCT sobre 86M+ filas es lento — mismo problema ya visto con PreAnio).
            int anioMinCatalogo = DateTime.Today.Year - 1;

            // 1) Productos con dato real + su Cat3/Cat1 padre (de una vez, vía Productos) —
            // esto también arma la cascada Suplidor→Categoría→Producto para el front.
            var productosCascada = new List<(int ProID, string Nombre, int Cat3ID, string Cat3Nombre, int Cat1ID, string Cat1Nombre)>();
            using (var cmd = new SqlCommand(@"
;WITH ProdValidos AS (
  SELECT DISTINCT TRY_CAST(CASE WHEN LEFT(PreReferencia,1)='V' THEN SUBSTRING(PreReferencia,2,20) ELSE PreReferencia END AS INT) AS ProdId
  FROM PresupuestosHistoricos WITH (NOLOCK)
  WHERE PreTipo IN ('MNTDPROD','Unddprod','UNDDACTPROD') AND PreAnio>=@anioMin
)
SELECT pr.ProID, pr.ProDescripcion, ISNULL(pr.Cat3ID,0) Cat3ID, ISNULL(c3.Cat3Descripcion,'') Cat3Nombre,
       ISNULL(pr.Cat1ID,0) Cat1ID, ISNULL(c1.Cat1Descripcion,'') Cat1Nombre
FROM ProdValidos pv
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=pv.ProdId
LEFT JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID=pr.Cat3ID
LEFT JOIN Categoria1 c1 WITH (NOLOCK) ON c1.Cat1ID=pr.Cat1ID
WHERE pr.ProDescripcion IS NOT NULL", cn) { CommandTimeout = 30 })
            {
                cmd.Parameters.AddWithValue("@anioMin", anioMinCatalogo);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    productosCascada.Add((
                        rd.GetInt32(0), rd.IsDBNull(1) ? "" : rd.GetString(1),
                        rd.GetInt32(2), rd.GetString(3),
                        rd.GetInt32(4), rd.GetString(5)
                    ));
            }
            var cat3IdsDeProductos = productosCascada.Select(p => p.Cat3ID).Where(id => id > 0).Distinct().ToHashSet();
            var cat1IdsDeProductos = productosCascada.Select(p => p.Cat1ID).Where(id => id > 0).Distinct().ToHashSet();

            // 2) Suplidores/Categorías que tienen dato DIRECTO en esa familia (además de los
            // que ya aparecen implícitos en productosCascada) — ej. un suplidor con presupuesto
            // fijado a nivel de MNTDCAT3VEN aunque ninguno de sus productos individuales tenga fila propia.
            var cat3IdsDirectos = new HashSet<int>();
            using (var cmd = new SqlCommand("SELECT DISTINCT TRY_CAST(PreReferencia AS INT) V FROM PresupuestosHistoricos WITH (NOLOCK) WHERE PreTipo IN ('MNTDCAT3VEN','UNDDCAT3VEN','UNDDACTCAT3') AND PreAnio>=@anioMin", cn) { CommandTimeout = 30 })
            {
                cmd.Parameters.AddWithValue("@anioMin", anioMinCatalogo);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) if (!rd.IsDBNull(0)) cat3IdsDirectos.Add(rd.GetInt32(0));
            }
            var cat1IdsDirectos = new HashSet<int>();
            using (var cmd = new SqlCommand("SELECT DISTINCT TRY_CAST(PreReferencia AS INT) V FROM PresupuestosHistoricos WITH (NOLOCK) WHERE PreTipo IN ('MNTDCAT','UNDDLIN','UNDDLINFACT','UNDDLINACT') AND PreAnio>=@anioMin", cn) { CommandTimeout = 30 })
            {
                cmd.Parameters.AddWithValue("@anioMin", anioMinCatalogo);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) if (!rd.IsDBNull(0)) cat1IdsDirectos.Add(rd.GetInt32(0));
            }
            cat3IdsDeProductos.UnionWith(cat3IdsDirectos);
            cat1IdsDeProductos.UnionWith(cat1IdsDirectos);

            var suplidores = new List<object>();
            if (cat3IdsDeProductos.Count > 0)
            {
                var parmsS = new Dictionary<string, object>();
                var inS = BuildIn("Cat3ID", cat3IdsDeProductos.Select(i => i.ToString()).ToList(), "s", parmsS);
                using var cmd = new SqlCommand($"SELECT Cat3ID, Cat3Descripcion FROM Categoria3 WITH (NOLOCK) WHERE Cat3Descripcion IS NOT NULL {inS} ORDER BY Cat3Descripcion", cn) { CommandTimeout = 30 };
                foreach (var kv in parmsS) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) suplidores.Add(new { v = rd.GetInt32(0).ToString(), t = rd.GetString(1) });
            }

            var categorias = new List<object>();
            if (cat1IdsDeProductos.Count > 0)
            {
                var parmsC = new Dictionary<string, object>();
                var inC = BuildIn("Cat1ID", cat1IdsDeProductos.Select(i => i.ToString()).ToList(), "c", parmsC);
                using var cmd = new SqlCommand($"SELECT Cat1ID, Cat1Descripcion FROM Categoria1 WITH (NOLOCK) WHERE Cat1Descripcion IS NOT NULL {inC} ORDER BY Cat1Descripcion", cn) { CommandTimeout = 30 };
                foreach (var kv in parmsC) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) categorias.Add(new { v = rd.GetInt32(0).ToString(), t = rd.GetString(1) });
            }

            var productos = productosCascada.Select(p => new { v = p.ProID.ToString(), t = p.Nombre }).ToList();
            var cascada = productosCascada.Select(p => new { p.ProID, p.Nombre, p.Cat3ID, p.Cat3Nombre, p.Cat1ID, p.Cat1Nombre }).ToList();

            // DISTINCT PreAnio sobre 86M+ filas es lento/inconsistente (2.5s-15s+ medido);
            // MAX(PreAnio) usa el índice y responde en ~60ms — generamos el rango en C#
            // en vez de escanear la tabla para una lista de años.
            int anioMax = DateTime.Today.Year;
            using (var cmd = new SqlCommand("SELECT MAX(PreAnio) FROM PresupuestosHistoricos WITH (NOLOCK)", cn) { CommandTimeout = 30 })
            {
                var v = cmd.ExecuteScalar();
                if (v != null && v != DBNull.Value) anioMax = Convert.ToInt32(v);
            }
            var anios = Enumerable.Range(anioMax - 3, 4).Reverse().ToList();

            var familias = FamiliaLabels.Select(f => new
            {
                cod = f.cod,
                label = f.label,
                kpis = PresupuestosCore.Mapa.Keys.Where(k => k.fam == f.cod).Select(k => k.kpi).ToArray(),
                // kpisDiario: subconjunto de "kpis" que sí tiene variante diaria nativa
                // (ej. categoria tiene dinero/facturas/activaciones con diario, pero NO unidades)
                // — la pestaña "Del Día" solo debe ofrecer estas combinaciones. Suplidor+Dinero
                // se agrega aparte: no tiene historial diario NATIVO, pero se puede INFERIR
                // desde Categoría+Dinero (cada categoría se asigna al suplidor con más
                // presupuesto entre sus productos, ver ObtenerMapaCat1ACat3) — pedido explícito
                // del usuario 2026-08-25.
                kpisDiario = PresupuestosCore.Mapa.Where(kv => kv.Key.fam == f.cod && kv.Value.TipoDiarioA != null).Select(kv => kv.Key.kpi)
                    .Concat(f.cod == "suplidor" ? new[] { "dinero" } : Array.Empty<string>())
                    .Distinct().ToArray(),
            });

            return new { representantes = reps, canales, suplidores, categorias, productos, productosCascada = cascada, anios, familias };
            }, TimeSpan.FromHours(1));
            return Ok(new { ok = true, resultado.representantes, resultado.canales, resultado.suplidores, resultado.categorias, resultado.productos, resultado.productosCascada, resultado.anios, resultado.familias });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Resuelve la lista de RepCodigo que cumplen los filtros de organización.
    // Lista vacía = "sin filtro de organización" (no se agrega AND RepCodigo IN en el caller).
    internal static List<string> ResolverReps(SqlConnection cn, string? centros, string? canales, string? divisiones, string? supervisores, string? vendedores)
    {
        var vCentros = ParseList(centros); var vCanales = ParseList(canales);
        var vDivisiones = ParseList(divisiones); var vSupervisores = ParseList(supervisores);
        var vVendedores = ParseList(vendedores);

        // Atajo: si solo hay filtro de Vendedor (sin Centro/Canal/División/Supervisor),
        // no hace falta consultar Representantes — el filtro YA es la lista de RepCodigo.
        if (vVendedores.Count > 0 && vCentros.Count == 0 && vCanales.Count == 0 && vDivisiones.Count == 0 && vSupervisores.Count == 0)
            return vVendedores;

        if (vCentros.Count == 0 && vCanales.Count == 0 && vDivisiones.Count == 0 && vSupervisores.Count == 0 && vVendedores.Count == 0)
            return new List<string>();

        var parms = new Dictionary<string, object>();
        var where = new List<string> { "r.RepEstatus=1", "ISNULL(r.RepCargo,'')<>'Supervisor'" };
        var f1 = BuildIn("r.CedCodigo", vCentros, "ce", parms); if (f1.Length > 0) where.Add(f1[4..]);
        var f2 = BuildIn("ra.CanID", vCanales, "ca", parms); if (f2.Length > 0) where.Add(f2[4..]);
        var f3 = BuildIn("ra.RepTipe", vDivisiones, "di", parms); if (f3.Length > 0) where.Add(f3[4..]);
        var f4 = BuildIn("r.RepSupervisor", vSupervisores, "su", parms); if (f4.Length > 0) where.Add(f4[4..]);
        var f5 = BuildIn("r.RepCodigo", vVendedores, "ve", parms); if (f5.Length > 0) where.Add(f5[4..]);

        var sql = $@"
SELECT r.RepCodigo
FROM Representantes r WITH (NOLOCK)
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE {string.Join(" AND ", where)}";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        var result = new List<string>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) result.Add(rd.GetString(0));
        return result;
    }

    // Filtro de PreReferencia según el "Ref" de la familia/KPI (ver PresupuestosCore.Mapa).
    // tablaAlias: prefijo de columna cuando PresupuestosHistoricos se alía en la query
    // (necesario al hacer JOIN con Representantes, que también tiene RepCodigo).
    internal static string BuildRefFiltro(string refTipo, List<string> suplidor, List<string> categoria, List<string> producto, Dictionary<string, object> parms, string tablaAlias = "")
    {
        string col = tablaAlias.Length > 0 ? $"{tablaAlias}.PreReferencia" : "PreReferencia";
        switch (refTipo)
        {
            case "none":
                return $"AND {col}='GENERAL'";
            case "cat1":
                return categoria.Count == 0 ? "" : BuildIn(col, categoria, "cat", parms);
            case "cat3":
                return suplidor.Count == 0 ? "" : BuildIn(col, suplidor, "sup", parms);
            case "prod":
                if (producto.Count == 0) return "";
                // ~99.5% de PreReferencia en las familias de producto trae un prefijo 'V' que no
                // cambia el ProID subyacente (ver hallazgo de investigación) — se normaliza antes de comparar.
                var names = producto.Select((v, i) => { var n = $"@prd{i}"; parms[n] = v; return n; }).ToList();
                return $"AND (CASE WHEN LEFT({col},1)='V' THEN SUBSTRING({col},2,20) ELSE {col} END) IN ({string.Join(",", names)})";
            default:
                return ""; // "grupo" pendiente (familia grupoproducto, fuera del alcance de esta fase)
        }
    }

    // ── Fuente real para "escalar hacia arriba" (pedido explícito del usuario 2026-08-28) ──
    // "Todos los montos de presupuesto nacen en MNTDPROD" y "el presupuesto de unidades
    // siempre comienza por Unddprod" — Categoría, Suplidor, Vendedor, Supervisor, Canal y
    // Centro deben escalar SIEMPRE desde estos 2 PreTipos (verificado en vivo: MNTDCAT3VEN/
    // MNTDCAT/MNTGVEN igualan EXACTO a MNTDPROD sumado por Cat3ID/Cat1ID/RepCodigo — no son
    // fuentes distintas, son la misma data pre-agregada). Además, el usuario revisa
    // "unidades" en CAJAS, no en unidades sueltas — Unddprod/UNDDCAT3VEN/UNDDLIN guardan
    // unidades sueltas; el factor de conversión es Productos.ProUnidades (unidades por
    // caja, confirmado en vivo contra UNDGVENCJ, que sí es nativo en Cajas).
    // Devuelve null (sin cambio, se sigue usando cfg.TipoMensual/PreReferencia tal cual)
    // para las excepciones deliberadas:
    //  - "general" (Ref="none", sin dimensión de producto que filtrar/reagrupar; MNTGVEN ya
    //    es exacto y UNDGVENCJ ya viene nativo en Cajas — reconstruirlo perdería precisión,
    //    no la ganaría).
    //  - "grupoproducto" (un producto puede pertenecer a VARIOS grupos a la vez —
    //    Productos.ProGrupoProductos es una lista JSON — atribución ambigua hacia arriba,
    //    sigue fuera de alcance, como ya lo estaba).
    //  - combinaciones sin equivalente a nivel de Producto (ej. Categoría+Facturas: no
    //    existe un PreTipo de facturas agregado por producto).
    //  - "activaciones", EXCEPTO cuando hace falta la granularidad de Producto para poder
    //    filtrar por un Suplidor o Producto específico. Verificado en vivo contra la BD
    //    (ago-2026, usuario reportó "el número no me cuadra"): a diferencia de Dinero/
    //    Cajas (si reconcilian exacto), UNDDACTPROD agregado por Categoría da 96,596
    //    mientras que UNDDLINACT (nativo de Categoría) da 58,271 para el mismo mes — NO
    //    son la misma métrica, escalar aquí infla el número ~66%. Además UNDDACTCAT3
    //    (nativo de Suplidor) tiene PreEjecutado=0 SIEMPRE en 2026 (presupuesto cargado,
    //    ejecución nunca poblada por TI) — no sirve como alternativa nativa. Por eso:
    //    sin filtro de Suplidor/Producto se prefiere el PreTipo nativo de la familia
    //    pedida (más preciso); solo se escala a Producto cuando el caller SÍ filtró por
    //    Suplidor o Producto (única fuente con Ejecutado real a esa granularidad —
    //    ver FragmentosProducto, que sí sabe filtrar por Cat3ID/ProID).
    internal sealed record FuenteProducto(string TipoReal, bool EsCajas);
    internal static FuenteProducto? ResolverFuenteProducto(string familia, string kpi, List<string>? vSuplidor = null, List<string>? vProducto = null)
    {
        if (familia == "general" || familia == "grupoproducto") return null;
        if (kpi == "activaciones" && (vSuplidor is null || vSuplidor.Count == 0) && (vProducto is null || vProducto.Count == 0)) return null;
        if (!PresupuestosCore.Mapa.TryGetValue(("producto", kpi), out var cfgProd)) return null;
        return new FuenteProducto(cfgProd.TipoMensual, kpi == "unidades");
    }

    // Arma los fragmentos SQL (join a Productos, filtro por dimensión, expresión de SUMA)
    // para una consulta que va a escalar desde Producto (ver ResolverFuenteProducto). Al
    // pasar por Productos se tienen Cat3ID Y Cat1ID en la MISMA fila — el filtro cruzado
    // Suplidor↔Categoría se vuelve exacto (ya no hace falta el mapa "dominante" aproximado
    // que se usaba antes para traducir entre las 2 dimensiones).
    internal static (string join, string filtro, string sumaPresu, string sumaEjec) FragmentosProducto(
        FuenteProducto fuente, List<string> vSuplidor, List<string> vCategoria, List<string> vProducto,
        Dictionary<string, object> parms, string aliasPh = "ph")
    {
        string join = $@"
CROSS APPLY (SELECT TRY_CAST(CASE WHEN LEFT({aliasPh}.PreReferencia,1)='V' THEN SUBSTRING({aliasPh}.PreReferencia,2,20) ELSE {aliasPh}.PreReferencia END AS INT) AS ProdId) fpx
INNER JOIN Productos fpr WITH (NOLOCK) ON fpr.ProID=fpx.ProdId";
        string filtro =
            (vSuplidor.Count == 0 ? "" : BuildIn("fpr.Cat3ID", vSuplidor, "fps", parms)) +
            (vCategoria.Count == 0 ? "" : BuildIn("fpr.Cat1ID", vCategoria, "fpc", parms)) +
            (vProducto.Count == 0 ? "" : BuildIn("fpr.ProID", vProducto, "fpp", parms));
        string sumaPresu = fuente.EsCajas ? $"SUM({aliasPh}.PrePresupuesto/NULLIF(fpr.ProUnidades,0))" : $"SUM({aliasPh}.PrePresupuesto)";
        string sumaEjec = fuente.EsCajas ? $"SUM({aliasPh}.PreEjecutado/NULLIF(fpr.ProUnidades,0))" : $"SUM({aliasPh}.PreEjecutado)";
        return (join, filtro, sumaPresu, sumaEjec);
    }

    // Curva de estacionalidad (Estimado%) según el "Ref" de la familia/KPI, cacheada 24h
    // (hasta 182 Cat1ID + 67 Cat3ID posibles — recalcular en vivo en cada request no es viable).
    // Solo aplica a "Del Mes": es una curva INTRA-MES (día 1..31), no tiene sentido aplicarla
    // contra un total anual (por eso "Del Año" se queda sin Estimado% por ahora, ver Resumen()).
    // Devuelve SIEMPRE una curva (nunca null) — pedido del usuario 2026-08-28: antes,
    // sin exactamente 1 Suplidor/Categoría/Producto elegido en el filtro, "Estimado"
    // salía en blanco en TODAS las consultas (Centro/Supervisor/Vendedor/Canal incluidos,
    // que agregan varias categorías a la vez). Ahora, cuando la selección es ambigua
    // (0 o 2+ elegidos), cae al agregado "general" (compañía completa) como mejor
    // aproximación disponible en vez de dejarlo "no aplica" — más útil aunque sea menos
    // preciso que una curva específica de esa categoría/suplidor.
    // vCanales (agregado 2026-09-05): igual patrón "exactamente 1 elegido, si no cae a
    // agregado" ya usado para Suplidor/Categoría — canId=0 (agregado/todos los canales) es
    // el comportamiento de siempre; con exactamente 1 canal elegido, la curva se vuelve
    // específica de ese canal (ataca el caso ya documentado de Mayorista, que se comporta
    // distinto a Minorista dentro del mes).
    private double[]? ObtenerCurva(SqlConnection cn, string refTipo, List<string> vCategoria, List<string> vSuplidor, List<string> vProducto, List<string> vCanales, int anio, int mes)
    {
        int canId = vCanales.Count == 1 && int.TryParse(vCanales[0], out var cid) ? cid : 0;
        string tipo, claveRef; string? cat1Id = null, cat3Id = null;
        const string tipoGeneral = "general", claveRefGeneral = "GENERAL";
        switch (refTipo)
        {
            case "cat1":
                if (vCategoria.Count == 1) { tipo = "cat1"; claveRef = cat1Id = vCategoria[0]; }
                else { tipo = tipoGeneral; claveRef = claveRefGeneral; }
                break;
            case "cat3":
                if (vSuplidor.Count == 1) { tipo = "cat3"; claveRef = cat3Id = vSuplidor[0]; }
                else { tipo = tipoGeneral; claveRef = claveRefGeneral; }
                break;
            case "prod":
                string? cat3Padre = vProducto.Count == 1 ? ObtenerCacheado($"prod:cat3:{vProducto[0]}", () =>
                {
                    using var cmdP = new SqlCommand("SELECT Cat3ID FROM Productos WITH (NOLOCK) WHERE ProID=@p", cn) { CommandTimeout = 15 };
                    cmdP.Parameters.AddWithValue("@p", int.Parse(vProducto[0]));
                    var v = cmdP.ExecuteScalar();
                    return v == null || v == DBNull.Value ? null : v.ToString();
                }, TimeSpan.FromHours(24)) : null;
                if (cat3Padre != null) { tipo = "cat3"; claveRef = cat3Id = cat3Padre; }
                else { tipo = tipoGeneral; claveRef = claveRefGeneral; }
                break;
            case "none":
            case "grupo":
                tipo = tipoGeneral; claveRef = claveRefGeneral;
                break;
            default:
                return null;
        }
        return ObtenerCurvaConPersistencia(cn, tipo, claveRef, anio, mes,
            () => PresupuestosCore.CurvaPorFranjas(cn, anio, mes, cat1Id, cat3Id, canId), canId);
    }

    // ── GET /apipresupuestos/patron-franjas ─────────────────────────────────
    // Muestra el patrón de 4 franjas descubierto (pedido explícito del usuario 2026-09-05:
    // "quiero ver el patrón, no solo el número final de Estimado") para la combinación de
    // Suplidor/Categoría/Canal vigente en el filtro — misma regla "exactamente 1 elegido, si
    // no cae al agregado" que usa ObtenerCurva, para que lo mostrado aquí sea siempre el mismo
    // patrón que alimenta "Estimado" en pantalla en ese momento. Los pesos no quedan
    // persistidos aparte de la curva ya materializada (ver PresupuestosCurvas_RM) — si el
    // usuario abre el patrón antes de que "Estimado" ya haya calentado esa combinación, esta
    // consulta paga el mismo costo (1 query contra VWVentas, ~segundos la 1ª vez) que ya paga
    // hoy cualquier curva nueva, nunca peor.
    [HttpGet("patron-franjas")]
    public IActionResult PatronFranjas(
        [FromQuery] int anio, [FromQuery] int mes,
        [FromQuery] string? suplidor, [FromQuery] string? categoria, [FromQuery] string? canales)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (anio < 2000 || mes < 1 || mes > 12) return BadRequest(new { ok = false, error = "anio/mes inválidos" });
        try
        {
            var vSuplidor = ParseList(suplidor); var vCategoria = ParseList(categoria); var vCanales = ParseList(canales);
            string? cat1Id = vCategoria.Count == 1 ? vCategoria[0] : null;
            string? cat3Id = vSuplidor.Count == 1 ? vSuplidor[0] : null;
            int canId = vCanales.Count == 1 && int.TryParse(vCanales[0], out var cid) ? cid : 0;

            using var cn = NewConn();
            cn.Open();
            var (pesos, mesesConDatos) = PresupuestosCore.PesosFranjas(cn, anio, mes, cat1Id, cat3Id, canId);
            var etiquetas = new[] { "1-7", "8-16", "17-22", "23-cierre" };
            var franjas = Enumerable.Range(0, 4).Select(i => new { rango = etiquetas[i], pct = Math.Round(pesos[i] * 100, 1) });
            return Ok(new { ok = true, franjas, mesesConDatos });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Mapa Cat1ID -> Cat3ID "dominante" (el suplidor con más presupuesto entre los
    // productos de esa categoría ese mes, vía MNTDPROD) — permite INFERIR Suplidor+
    // Dinero por día a partir de Categoría+Dinero (que sí tiene historial diario
    // nativo), ya que Suplidor+Dinero no lo tiene. Pedido explícito del usuario
    // 2026-08-25: "cada categoría pertenece a un suplidor... si está en 2 o más,
    // colócala en el que mayor presupuesto tenga". Cacheado 24h (no cambia en el día).
    private Dictionary<int, int> ObtenerMapaCat1ACat3(SqlConnection cn, int anio, int mes)
    {
        var clave = $"mapaCat1Cat3:{anio}{mes:00}";
        var resultado = ObtenerCacheado(clave, () =>
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
        }, TimeSpan.FromHours(24));
        return resultado!;
    }

    // Igual que ObtenerMapaCat1ACat3, pero agregando TODO el año (no un solo mes) — usado
    // por "Del Año" para traducir un filtro de Suplidor a sus Categorías equivalentes (o
    // viceversa) cuando se está viendo el pivote de la dimensión contraria. Bug reportado
    // por el usuario 2026-08-25: "el filtro no funciona, salen todas las categorías y
    // suplidores" — causa: MNTDCAT3VEN (pivote Suplidor) solo tiene Cat3ID, sin Cat1ID, y
    // MNTDCAT (pivote Categoría) solo tiene Cat1ID, sin Cat3ID — ninguno de los 2 puede
    // filtrarse directo por la dimensión contraria sin este mapeo.
    private Dictionary<int, int> ObtenerMapaCat1ACat3Anio(SqlConnection cn, int anio)
    {
        var clave = $"mapaCat1Cat3anio:{anio}";
        var resultado = ObtenerCacheado(clave, () =>
        {
            var mejor = new Dictionary<int, (int cat3, double presu)>();
            using var cmd = new SqlCommand(@"
SELECT pr.Cat1ID, pr.Cat3ID, SUM(ph.PrePresupuesto) Presu
FROM PresupuestosHistoricos ph WITH (NOLOCK)
CROSS APPLY (SELECT TRY_CAST(CASE WHEN LEFT(ph.PreReferencia,1)='V' THEN SUBSTRING(ph.PreReferencia,2,20) ELSE ph.PreReferencia END AS INT) AS ProdId) x
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=x.ProdId
WHERE ph.PreTipo='MNTDPROD' AND ph.PreAnio=@anio
  AND pr.Cat1ID IS NOT NULL AND pr.Cat3ID IS NOT NULL
GROUP BY pr.Cat1ID, pr.Cat3ID", cn) { CommandTimeout = 90 };
            cmd.Parameters.AddWithValue("@anio", anio);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                int cat1 = rd.GetInt32(0), cat3 = rd.GetInt32(1);
                double presu = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
                if (!mejor.TryGetValue(cat1, out var actual) || presu > actual.presu) mejor[cat1] = (cat3, presu);
            }
            return mejor.ToDictionary(kv => kv.Key, kv => kv.Value.cat3);
        }, TimeSpan.FromHours(24));
        return resultado!;
    }

    // ── GET /apipresupuestos/resumen ────────────────────────────────────────
    // Del Mes + Del Año para las familias/KPI pedidos, en 1 query por familia×KPI
    // (trae los 12 meses de un tirón agrupando por PreMes, evita N+1). El Estimado%
    // (curva de estacionalidad) aplica solo a Del Mes — ver ObtenerCurva().
    [HttpGet("resumen")]
    public IActionResult Resumen(
        [FromQuery] int anio, [FromQuery] int mes, [FromQuery] string? familias, [FromQuery(Name = "kpi")] string? kpiFiltro,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? suplidor, [FromQuery] string? categoria, [FromQuery] string? producto,
        [FromQuery] bool detalleMensual = false, [FromQuery] bool sinCurva = false, [FromQuery] bool asumirAyer = false)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (anio < 2000 || mes < 1 || mes > 12) return BadRequest(new { ok = false, error = "anio/mes inválidos" });
        try
        {
            using var cn = NewConn();
            cn.Open();

            var reps = ResolverReps(cn, centros, canales, divisiones, supervisores, vendedores);
            var huboFiltroOrg = new[] { centros, canales, divisiones, supervisores, vendedores }.Any(s => ParseList(s).Count > 0);
            if (huboFiltroOrg && reps.Count == 0)
                return Ok(new { ok = true, entornos = new { mes = new Dictionary<string, object>(), anio = new Dictionary<string, object>() } });

            var vSuplidor = ParseList(suplidor); var vCategoria = ParseList(categoria); var vProducto = ParseList(producto);
            var vCanales = ParseList(canales);
            var famList = ParseList(familias);
            // Sin "Tipo de Presupuesto" elegido, por defecto ya NO se muestra "General" —
            // pedido del usuario 2026-08-28 ("no agrega valor" antes del Detalle). Se
            // muestran Categoría+Suplidor+Producto en su lugar, para que el resumen nunca
            // quede vacío. "General" se sigue pudiendo ver si el usuario lo elige a propósito
            // en el filtro de Tipo de Presupuesto.
            if (famList.Count == 0) famList = new() { "categoria", "suplidor", "producto" };

            int diasDelMes = PresupuestosCore.DiasHabiles(anio, mes, DateTime.DaysInMonth(anio, mes));
            int diasDelAnio = PresupuestosCore.DiasHabilesAnioCompleto(anio);

            var entornoMes = new Dictionary<string, Dictionary<string, PresupuestosCore.KpiResultado?>>();
            var entornoAnio = new Dictionary<string, Dictionary<string, PresupuestosCore.KpiResultado?>>();
            // Detalle mes-a-mes para "Del Año" (pedido 2026-08-28: tabla que crece hacia abajo
            // por Mes, con las MISMAS columnas que "Por Mes" — antes Del Año solo tenía
            // Presupuesto/Ejecutado crudos, sin Alcance/Proyectado/Estimado). Opcional
            // (detalleMensual=true) para no pagar este costo extra en la carga normal de
            // "Del Mes", que no lo necesita — reusa la MISMA query ya traída (porMes), sin
            // SQL adicional, solo más cálculo en C# (Calcular() es aritmética en memoria).
            var entornoDetalle = new Dictionary<string, Dictionary<string, List<object>>>();

            foreach (var fam in famList)
            {
                // kpi (opcional): restringe a UN solo KPI dentro de la familia — pedido
                // explícito 2026-08-29 ("solo debe haber una sola consulta desplegada y es
                // la que se filtre"). Sin este parámetro (compatibilidad hacia atrás) se
                // siguen listando TODOS los KPI que soporta esa familia, como antes.
                var kpisDeFamilia = string.IsNullOrWhiteSpace(kpiFiltro)
                    ? PresupuestosCore.Mapa.Keys.Where(k => k.fam == fam).Select(k => k.kpi).Distinct().ToList()
                    : new List<string> { kpiFiltro };
                entornoMes[fam] = new Dictionary<string, PresupuestosCore.KpiResultado?>();
                entornoAnio[fam] = new Dictionary<string, PresupuestosCore.KpiResultado?>();
                if (detalleMensual) entornoDetalle[fam] = new Dictionary<string, List<object>>();

                foreach (var kpi in kpisDeFamilia)
                {
                    if (!PresupuestosCore.Mapa.TryGetValue((fam, kpi), out var cfg)) continue;

                    var parms = new Dictionary<string, object> { ["@anio"] = anio };
                    var fuente = ResolverFuenteProducto(fam, kpi, vSuplidor, vProducto);
                    var tipoNames = new List<string> { "@tp0" }; parms["@tp0"] = fuente?.TipoReal ?? cfg.TipoMensual;
                    var repFiltro = reps.Count == 0 ? "" : BuildIn("ph.RepCodigo", reps, "rp", parms);
                    string joinProd = "", refFiltro, sumaPresu = "SUM(ph.PrePresupuesto)", sumaEjec = "SUM(ph.PreEjecutado)";
                    if (fuente != null)
                    {
                        (joinProd, refFiltro, sumaPresu, sumaEjec) = FragmentosProducto(fuente, vSuplidor, vCategoria, vProducto, parms, "ph");
                    }
                    else refFiltro = BuildRefFiltro(cfg.Ref, vSuplidor, vCategoria, vProducto, parms, "ph");

                    // Se agrega por (mes, canal) — no por mes solamente — porque cada canal puede
                    // tener su propia MAX(PreFechaActualizacion) (Mayorista a veces se queda varios
                    // días atrás sin facturar, según el usuario) y mezclar todo bajo una sola fecha
                    // "global" sobreestima qué tan al día está el canal más lento. Cada franja de
                    // canal usa SU PROPIO diasT; Minorista además aplica el rezago pedido→factura.
                    var sql = $@"
SELECT ph.PreMes, ISNULL(ra.CanID,0) CanID,
       {sumaPresu} Presu, {sumaEjec} Ejec, MAX(ph.PreFechaActualizacion) Ultima
FROM PresupuestosHistoricos ph WITH (NOLOCK)
{joinProd}
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=ph.RepCodigo
WHERE ph.PreAnio=@anio AND ph.PreTipo IN ({string.Join(",", tipoNames)})
{repFiltro}
{refFiltro}
GROUP BY ph.PreMes, ISNULL(ra.CanID,0)";
                    using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
                    foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);

                    // Timing de diagnóstico (pedido del usuario 2026-09-24, "eficientizar" el
                    // tiempo de Presupuestos) — esta consulta ya se había medido en vivo en
                    // ~100-600ms (ver comentario más abajo sobre "Dashboard Gráficos"), pero
                    // sin logging quedaba esa única medición suelta; ahora queda constancia
                    // por cada fam/kpi realmente consultado, para comparar contra el costo de
                    // CurvaDeCanal (la parte cara) en logs reales de producción.
                    var swAgg = System.Diagnostics.Stopwatch.StartNew();
                    var porMes = new Dictionary<int, List<(int canId, double presu, double ejec, DateTime? ultima)>>();
                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            int m = rd.GetInt16(0);
                            int canId = rd.GetInt32(1);
                            double presu = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
                            double ejec = rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3));
                            DateTime? ultima = rd.IsDBNull(4) ? null : rd.GetDateTime(4);
                            // "asumirAyer" (pedido del usuario 2026-09-25): override manual para
                            // cuando el usuario DETECTA que el proceso nocturno de TI que puebla
                            // PreFechaActualizacion no corrió anoche y se corrió hoy en su lugar —
                            // bajo operación normal, 'ultima' SIEMPRE debería quedar "ayer" (el
                            // proceso corre de noche reflejando la facturación de un día antes);
                            // si corrió tarde/manual, 'ultima' queda "hoy" en vez de "ayer", e
                            // infla Días Transcurridos en 1 día hábil de más (ver
                            // DiasTranscurridosAjustado). Se corrige en ESTE único punto de
                            // ingesta (no en cada fórmula por separado) para que el ajuste se
                            // propague solo a TODO lo que depende de 'ultima' — diasT del mes,
                            // diasT por canal (Proyectado/Estimado), Del Año, Detalle mensual —
                            // sin duplicar la lógica de "-1 día" en varios lugares.
                            if (asumirAyer && ultima.HasValue) ultima = ultima.Value.AddDays(-1);
                            if (!porMes.TryGetValue(m, out var lista)) { lista = new(); porMes[m] = lista; }
                            lista.Add((canId, presu, ejec, ultima));
                        }
                    }
                    swAgg.Stop();
                    logger.LogInformation("Resumen: PresupuestosHistoricos {Fam}/{Kpi} {Anio}-{Mes:00} en {Ms}ms ({Filas} filas)", fam, kpi, anio, mes, swAgg.ElapsedMilliseconds, porMes.Sum(kv => kv.Value.Count));

                    // Curva por Canal (pedido explícito del usuario 2026-09-07: "el peso tiene
                    // que ser sí o sí por canal, para la compañía completa, filtremos o no") —
                    // una curva "general" mezclada no representa bien a NINGÚN canal (Mayorista
                    // concentra ~47% al cierre, Minorista ~33% — verificado en vivo). Se resuelve
                    // 1 vez por CanID distinto que aparezca en franjasMes/franjasM (cacheado en
                    // este dict local; ObtenerCurvaConPersistencia además cachea 24h/persiste en
                    // BD, así que ni siquiera esto pega contra VWVentas más que la 1ª vez por día).
                    var curvaPorCanalCache = new Dictionary<int, double[]?>();
                    double[]? CurvaDeCanal(int canId, int mesRef) => sinCurva ? null
                        : curvaPorCanalCache.TryGetValue(canId, out var cCache) ? cCache
                        : (curvaPorCanalCache[canId] = ObtenerCurva(cn, cfg.Ref, vCategoria, vSuplidor, vProducto, new List<string> { canId.ToString() }, anio, mesRef));

                    // Del Mes
                    if (porMes.TryGetValue(mes, out var franjasMes))
                    {
                        double presuTotal = franjasMes.Sum(f => f.presu), ejecTotal = franjasMes.Sum(f => f.ejec);
                        var ultimasMes = franjasMes.Where(f => f.ultima.HasValue).Select(f => f.ultima!.Value).ToList();
                        DateTime? ultimaMes = ultimasMes.Count > 0 ? ultimasMes.Max() : null;
                        int diasT = PresupuestosCore.DiasTranscurridos(ultimaMes, anio, mes);
                        var r = PresupuestosCore.Calcular(presuTotal, ejecTotal, diasT, diasDelMes, null, ultimaMes);
                        var franjasCalc = franjasMes.Select(f =>
                        {
                            int diasTCanal = PresupuestosCore.DiasTranscurridos(f.ultima, anio, mes);
                            diasTCanal = PresupuestosCore.DiasTranscurridosAjustado(diasTCanal, diasDelMes);
                            return (f.presu, f.ejec, diasTCanal);
                        });
                        PresupuestosCore.AjustarProyectadoPorCanal(r, franjasCalc, diasDelMes);
                        var franjasCalcEst = franjasMes.Select(f =>
                        {
                            int diasTCanal = PresupuestosCore.DiasTranscurridos(f.ultima, anio, mes);
                            return (f.presu, f.ejec, diasTCanal, CurvaDeCanal(f.canId, mes));
                        });
                        PresupuestosCore.AjustarEstimadoPorCanal(r, franjasCalcEst, diasDelMes);
                        entornoMes[fam][kpi] = r;
                    }
                    else entornoMes[fam][kpi] = null;

                    // Del Año
                    if (porMes.Count > 0)
                    {
                        double presuAnio = porMes.Values.Sum(lst => lst.Sum(f => f.presu));
                        double ejecAnio = porMes.Values.Sum(lst => lst.Sum(f => f.ejec));
                        var ultimas = porMes.Values.SelectMany(lst => lst).Where(f => f.ultima.HasValue).Select(f => f.ultima!.Value).ToList();
                        DateTime? ultimaAnio = ultimas.Count > 0 ? ultimas.Max() : null;
                        int diasT = PresupuestosCore.DiasTranscurridosAnio(ultimaAnio, anio);
                        entornoAnio[fam][kpi] = PresupuestosCore.Calcular(presuAnio, ejecAnio, diasT, diasDelAnio, null, ultimaAnio);
                    }
                    else entornoAnio[fam][kpi] = null;

                    if (detalleMensual)
                    {
                        var detalleLista = new List<object>();
                        foreach (var mKey in porMes.Keys.OrderBy(m => m))
                        {
                            var franjasM = porMes[mKey];
                            double presuM = franjasM.Sum(f => f.presu), ejecM = franjasM.Sum(f => f.ejec);
                            var ultimasM = franjasM.Where(f => f.ultima.HasValue).Select(f => f.ultima!.Value).ToList();
                            DateTime? ultimaM = ultimasM.Count > 0 ? ultimasM.Max() : null;
                            int diasDelMesM = PresupuestosCore.DiasHabiles(anio, mKey, DateTime.DaysInMonth(anio, mKey));
                            int diasTM = PresupuestosCore.DiasTranscurridos(ultimaM, anio, mKey);
                            var rM = PresupuestosCore.Calcular(presuM, ejecM, diasTM, diasDelMesM, null, ultimaM);
                            var franjasCalcM = franjasM.Select(f =>
                            {
                                int diasTCanalM = PresupuestosCore.DiasTranscurridos(f.ultima, anio, mKey);
                                diasTCanalM = PresupuestosCore.DiasTranscurridosAjustado(diasTCanalM, diasDelMesM);
                                return (f.presu, f.ejec, diasTCanalM);
                            });
                            PresupuestosCore.AjustarProyectadoPorCanal(rM, franjasCalcM, diasDelMesM);
                            var franjasCalcEstM = franjasM.Select(f =>
                            {
                                int diasTCanalM = PresupuestosCore.DiasTranscurridos(f.ultima, anio, mKey);
                                return (f.presu, f.ejec, diasTCanalM, CurvaDeCanal(f.canId, mKey));
                            });
                            PresupuestosCore.AjustarEstimadoPorCanal(rM, franjasCalcEstM, diasDelMesM);
                            detalleLista.Add(new
                            {
                                Mes = mKey,
                                rM.Presupuesto, rM.PresupuestoAlDia, rM.Ejecutado, rM.AlcancePct,
                                rM.VentasProyectadas, rM.ProyectadoPct, rM.VentasEstimadas, rM.EstimadoPct,
                            });
                        }
                        entornoDetalle[fam][kpi] = detalleLista;
                    }
                }
            }

            return Ok(new { ok = true, entornos = new { mes = entornoMes, anio = entornoAnio, detalle = detalleMensual ? entornoDetalle : null } });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apipresupuestos/drill/organizacion ─────────────────────────────
    // 1 fetch a grano RepCodigo para Del Mes de una sola familia/kpi — el drill
    // Centro→Supervisor→Vendedor (breadcrumb) se arma 100% client-side a partir de
    // estas filas, igual patrón que Views/Historicos/Index.cshtml (agg/render/setNivel).
    [HttpGet("drill/organizacion")]
    public IActionResult DrillOrganizacion(
        [FromQuery] int anio, [FromQuery] int mes, [FromQuery] string familia, [FromQuery] string kpi,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? suplidor, [FromQuery] string? categoria, [FromQuery] string? producto,
        [FromQuery] bool sinCurva = false)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (!PresupuestosCore.Mapa.TryGetValue((familia, kpi), out var cfg))
            return BadRequest(new { ok = false, error = $"Combinación familia/kpi inválida: {familia}/{kpi}" });
        try
        {
            using var cn = NewConn();
            cn.Open();

            var reps = ResolverReps(cn, centros, canales, divisiones, supervisores, vendedores);
            var huboFiltroOrg = new[] { centros, canales, divisiones, supervisores, vendedores }.Any(s => ParseList(s).Count > 0);
            if (huboFiltroOrg && reps.Count == 0) return Ok(new { ok = true, diasDelMes = 0, filas = Array.Empty<object>() });

            var vSuplidor = ParseList(suplidor); var vCategoria = ParseList(categoria); var vProducto = ParseList(producto);
            var vCanales = ParseList(canales);
            int diasDelMes = PresupuestosCore.DiasHabiles(anio, mes, DateTime.DaysInMonth(anio, mes));
            // Curva por Canal (pedido explícito del usuario 2026-09-07: "el peso tiene que ser sí
            // o sí por canal, para la compañía completa, filtremos o no") — cada fila de este
            // endpoint YA es un vendedor con un único CanID (viene en la propia fila, ver SQL más
            // abajo), así que cada uno usa la curva de SU canal, sin importar qué haya elegido el
            // usuario en el filtro de Canal. sinCurva=true (Dashboard Gráficos, que no muestra
            // Estimado/Est.%) se salta ObtenerCurva por completo — evita 1 consulta contra
            // VWVentas (CurvaPorFranjas, pesada) por cada canal distinto cuando el dato ni
            // siquiera se va a usar. Cacheado por CanID (normalmente 2-6 canales distintos, no 1
            // por vendedor) — ObtenerCurvaConPersistencia además cachea 24h/persiste en BD.
            var curvaPorCanalCache = new Dictionary<int, double[]?>();
            double[]? CurvaDeCanal(int canId) => sinCurva ? null
                : curvaPorCanalCache.TryGetValue(canId, out var cCache) ? cCache
                : (curvaPorCanalCache[canId] = ObtenerCurva(cn, cfg.Ref, vCategoria, vSuplidor, vProducto, new List<string> { canId.ToString() }, anio, mes));

            var fuente = ResolverFuenteProducto(familia, kpi, vSuplidor, vProducto);
            var parms = new Dictionary<string, object> { ["@anio"] = anio, ["@mes"] = mes, ["@tp0"] = fuente?.TipoReal ?? cfg.TipoMensual };
            var repFiltro = reps.Count == 0 ? "" : BuildIn("ph.RepCodigo", reps, "rp", parms);
            string joinProd = "", refFiltro, sumaPresu = "SUM(ph.PrePresupuesto)", sumaEjec = "SUM(ph.PreEjecutado)";
            if (fuente != null) (joinProd, refFiltro, sumaPresu, sumaEjec) = FragmentosProducto(fuente, vSuplidor, vCategoria, vProducto, parms, "ph");
            else refFiltro = BuildRefFiltro(cfg.Ref, vSuplidor, vCategoria, vProducto, parms, "ph");

            var sql = $@"
SELECT ph.RepCodigo, r.RepNombre, ISNULL(r.CedCodigo,'') CedCodigo, ISNULL(cd.CedDescripcion,ISNULL(r.CedCodigo,'(Sin centro)')) CentroNombre,
       ISNULL(r.RepSupervisor,'') SupCod, ISNULL(sup.RepNombre,r.RepSupervisor) SupNombre,
       {sumaPresu} Presu, {sumaEjec} Ejec, MAX(ph.PreFechaActualizacion) Ultima, ISNULL(ra.CanID,0) CanID
FROM PresupuestosHistoricos ph WITH (NOLOCK)
{joinProd}
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=ph.RepCodigo
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=ph.RepCodigo
WHERE ph.PreAnio=@anio AND ph.PreMes=@mes AND ph.PreTipo=@tp0
{repFiltro}
{refFiltro}
GROUP BY ph.RepCodigo, r.RepNombre, r.CedCodigo, cd.CedDescripcion, r.RepSupervisor, sup.RepNombre, ra.CanID";

            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);

            var filas = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                double presu = rd.IsDBNull(6) ? 0 : Convert.ToDouble(rd.GetValue(6));
                double ejec = rd.IsDBNull(7) ? 0 : Convert.ToDouble(rd.GetValue(7));
                DateTime? ultima = rd.IsDBNull(8) ? null : rd.GetDateTime(8);
                int canIdRow = rd.GetInt32(9);
                int diasT = PresupuestosCore.DiasTranscurridos(ultima, anio, mes);
                var r = PresupuestosCore.Calcular(presu, ejec, diasT, diasDelMes, CurvaDeCanal(canIdRow), ultima);
                int diasTAjustado = PresupuestosCore.DiasTranscurridosAjustado(diasT, diasDelMes);
                PresupuestosCore.AjustarProyectadoPorCanal(r, new[] { (presu, ejec, diasTAjustado) }, diasDelMes);
                filas.Add(new
                {
                    RepCodigo = rd.GetString(0), RepNombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    CedCodigo = rd.GetString(2), CentroNombre = rd.GetString(3),
                    SupCod = rd.GetString(4), SupNombre = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    r.Presupuesto, r.PresupuestoAlDia, r.Ejecutado, r.AlcancePct,
                    r.VentasProyectadas, r.ProyectadoPct, r.VentasEstimadas, r.EstimadoPct,
                    r.DiasDelPeriodo, r.DiasTranscurridosAjustado, r.DiasRestantes, r.NecesidadDiaria,
                });
            }
            return Ok(new { ok = true, diasDelMes, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apipresupuestos/drill/producto ─────────────────────────────────
    // Profundidad variable, honesta con lo que soporta cada familia:
    //  - "producto", y "suplidor"/"categoria" cuando el KPI tiene equivalente a nivel de
    //    Producto (dinero/unidades/activaciones): 3 niveles reales (Suplidor→Categoría→
    //    Producto), 1 fila por ProID, con Presupuesto+Ejecutado reales en los 3 niveles.
    //  - "categoria"+facturas (sin equivalente por producto) y "grupoproducto": 1 solo
    //    nivel — 1 fila por Cat1ID/Grupo, ya agregado.
    // Estimado por fila vía curva cacheada por Cat3ID/Cat1ID (ver CurvaCat3Local/CurvaCat1Local).
    [HttpGet("drill/producto")]
    public IActionResult DrillProducto(
        [FromQuery] int anio, [FromQuery] int mes, [FromQuery] string familia, [FromQuery] string kpi,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? suplidor, [FromQuery] string? categoria, [FromQuery] string? producto,
        [FromQuery] bool sinCurva = false, [FromQuery] bool porCanal = false)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (!PresupuestosCore.Mapa.TryGetValue((familia, kpi), out var cfg))
            return BadRequest(new { ok = false, error = $"Combinación familia/kpi inválida: {familia}/{kpi}" });
        if (familia != "producto" && familia != "suplidor" && familia != "categoria" && familia != "grupoproducto")
            return BadRequest(new { ok = false, error = "Esta vista aplica a familias de producto (Suplidor/Categoría/Producto/Grupo de Producto)." });
        try
        {
            using var cn = NewConn();
            cn.Open();

            var reps = ResolverReps(cn, centros, canales, divisiones, supervisores, vendedores);
            var huboFiltroOrg = new[] { centros, canales, divisiones, supervisores, vendedores }.Any(s => ParseList(s).Count > 0);
            if (huboFiltroOrg && reps.Count == 0) return Ok(new { ok = true, niveles = 1, diasDelMes = 0, filas = Array.Empty<object>() });

            // porCanal=true (pedido 2026-08-29 — "desglosar por Canal" en Detalle: Por
            // Producto): agrega CanID a la GROUP BY vía RepresentantesAdicionales, igual
            // patrón que ya usa DrillOrganizacion — el mismo Suplidor/Categoría/Producto
            // aparece 1 vez por cada Canal donde tuvo actividad, cada uno con su propio
            // Presupuesto/Ejecutado (no se combinan) y se muestra así en pantalla.
            //
            // FIX 2026-09-07 (pedido explícito del usuario — "el peso tiene que ser sí o sí
            // por canal... filtremos o no"): el JOIN/GROUP BY por canal ahora es SIEMPRE
            // incondicional (antes solo se activaba con porCanal=true; sin el flag, CanID
            // salía fijo en -1 y la fila combinaba TODOS los canales bajo una sola curva
            // general — el mismo hueco que ya se cerró en Resumen()/DrillOrganizacion()).
            // Con porCanal=false las filas por canal se siguen leyendo de la BD (para poder
            // ponderar el Estimado con la curva propia de cada canal), pero se COLAPSAN de
            // vuelta a 1 fila por Suplidor/Categoría/Producto más abajo en C# (ver
            // ColapsarCanales) — el contrato de salida hacia el frontend no cambia.
            string joinCanal = "LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=ph.RepCodigo";
            string selectCanal = "ISNULL(ra.CanID,0)";
            string groupByCanal = ", ISNULL(ra.CanID,0)";

            // Bug reportado por el usuario 2026-08-25: este endpoint no recibía ni aplicaba
            // los filtros de Suplidor/Categoría/Producto — el front ya los enviaba (qsFiltros
            // los incluye siempre), pero el backend los ignoraba por completo.
            var vSuplidor = ParseList(suplidor); var vCategoria = ParseList(categoria); var vProducto = ParseList(producto);
            int diasDelMes = PresupuestosCore.DiasHabiles(anio, mes, DateTime.DaysInMonth(anio, mes));
            var parms = new Dictionary<string, object> { ["@anio"] = anio, ["@mes"] = mes, ["@tp0"] = cfg.TipoMensual };
            var repFiltro = reps.Count == 0 ? "" : BuildIn("ph.RepCodigo", reps, "rp", parms);

            // Estimado por fila (pedido del usuario 2026-08-28: antes esta vista SIEMPRE
            // dejaba Estimado en null — "una curva por fila requeriría un cálculo por cada
            // una", pero con caché por Cat3ID/Cat1ID (24h, vía IMemoryCache) el costo real es
            // 1 query por Suplidor/Categoría distinto, no por fila — barato, ya que hay pocas
            // decenas de suplidores/categorías aunque haya cientos de productos).
            // OJO — "barato" asume pocos suplidores distintos en el resultado. Cuando NO hay
            // filtro de Suplidor (p.ej. "Dashboard Gráficos", familia=producto, catálogo
            // completo), el resultado puede tener DECENAS/CIENTOS de Cat3ID distintos, y cada
            // uno que no esté aún en caché ese día dispara 1 consulta pesada contra VWVentas
            // (CurvaPorFranjas, join con VWVentasDetalle+Productos, CommandTimeout=90) —
            // secuenciales, sobre la MISMA conexión — eso fue lo que reportó el usuario
            // 2026-08-29 como "el tiempo de espera... es demasiado". Por eso `sinCurva=true`
            // (Dashboard Gráficos, que no muestra Estimado/Est.%) evita esto de raíz.
            // canId (default 0, pedido 2026-09-05): cuando porCanal=true cada fila ya trae su
            // propio CanID (ver joinCanal/selectCanal arriba) — se pasa aquí para que Mayorista/
            // Minorista tengan su propio patrón de 4 franjas en vez de compartir uno agregado.
            double[]? CurvaCat3Local(int cat3Id, int canId = 0) => sinCurva ? null : ObtenerCurvaConPersistencia(cn, "cat3", cat3Id.ToString(), anio, mes,
                () => PresupuestosCore.CurvaPorFranjas(cn, anio, mes, null, cat3Id.ToString(), canId), canId);
            double[] CurvaCat1Local(int cat1Id, int canId = 0) => ObtenerCurvaConPersistencia(cn, "cat1", cat1Id.ToString(), anio, mes,
                () => PresupuestosCore.CurvaPorFranjas(cn, anio, mes, cat1Id.ToString(), null, canId), canId)!;
            double[] CurvaGeneralLocal(int canId = 0) => ObtenerCurvaConPersistencia(cn, "general", "GENERAL", anio, mes,
                () => PresupuestosCore.CurvaPorFranjas(cn, anio, mes, null, null, canId), canId)!;

            var filas = new List<object>();
            int niveles;

            // Suplidor/Categoría también bajan a 3 niveles (Suplidor→Categoría→Producto),
            // pedido explícito del usuario 2026-08-28: "el presupuesto esta construido por
            // Productos igual el ejecutado, entonces si bajo de Suplidor a Categoria, debería
            // poder determinar cuál es el presupuesto y el ejecutado". Verificado en vivo:
            // MNTDCAT3VEN/MNTDCAT (y sus KPIs de unidades) son EXACTAMENTE la suma de
            // MNTDPROD/Unddprod agrupada por Cat3ID/Cat1ID — misma data, no hay que fabricar
            // nada, solo reusar el PreTipo de "producto" para el mismo KPI. Excepción:
            // Categoría+Facturas no tiene equivalente a nivel de Producto (UNDDLINFACT no
            // existe agregado por producto) — ahí se mantiene el nivel único de siempre.
            bool hayEquivalenteProducto = familia != "grupoproducto"
                && PresupuestosCore.Mapa.ContainsKey(("producto", kpi));

            if (familia == "producto" || hayEquivalenteProducto)
            {
                niveles = 3;
                parms["@tp0"] = familia == "producto" ? cfg.TipoMensual : PresupuestosCore.Mapa[("producto", kpi)].TipoMensual;
                // BuildRefFiltro(Ref="prod") solo filtra por Producto — Suplidor/Categoría se
                // filtran aparte aquí mismo, aprovechando que ya se tiene pr.Cat3ID/pr.Cat1ID.
                var prodFiltro = vProducto.Count == 0 ? "" : BuildIn("pr.ProID", vProducto, "pp", parms);
                var cat3Filtro = vSuplidor.Count == 0 ? "" : BuildIn("pr.Cat3ID", vSuplidor, "ps", parms);
                var cat1Filtro = vCategoria.Count == 0 ? "" : BuildIn("pr.Cat1ID", vCategoria, "pc", parms);
                // "unidades" nace en Unddprod (unidades sueltas) pero el usuario lo revisa en
                // Cajas — se convierte con Productos.ProUnidades (unidades por caja) fila por
                // fila (cada fila ya es 1 solo ProID, así que dividir antes o después de sumar
                // da igual aquí — no hay mezcla de productos con distinto factor por fila).
                bool esCajas = kpi == "unidades";
                string sumaPresu = esCajas ? "SUM(ph.PrePresupuesto/NULLIF(pr.ProUnidades,0))" : "SUM(ph.PrePresupuesto)";
                string sumaEjec = esCajas ? "SUM(ph.PreEjecutado/NULLIF(pr.ProUnidades,0))" : "SUM(ph.PreEjecutado)";
                var sql = $@"
SELECT pr.ProID, pr.ProDescripcion, ISNULL(pr.Cat3ID,0) Cat3ID, ISNULL(c3.Cat3Descripcion,'(Sin suplidor)') Cat3Nombre,
       ISNULL(pr.Cat1ID,0) Cat1ID, ISNULL(c1.Cat1Descripcion,'(Sin categoría)') Cat1Nombre,
       {selectCanal} CanID,
       {sumaPresu} Presu, {sumaEjec} Ejec, MAX(ph.PreFechaActualizacion) Ultima
FROM PresupuestosHistoricos ph WITH (NOLOCK)
CROSS APPLY (SELECT TRY_CAST(CASE WHEN LEFT(ph.PreReferencia,1)='V' THEN SUBSTRING(ph.PreReferencia,2,20) ELSE ph.PreReferencia END AS INT) AS ProdId) x
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=x.ProdId
LEFT JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID=pr.Cat3ID
LEFT JOIN Categoria1 c1 WITH (NOLOCK) ON c1.Cat1ID=pr.Cat1ID
{joinCanal}
WHERE ph.PreAnio=@anio AND ph.PreMes=@mes AND ph.PreTipo=@tp0
{repFiltro}
{prodFiltro}
{cat3Filtro}
{cat1Filtro}
GROUP BY pr.ProID, pr.ProDescripcion, pr.Cat3ID, c3.Cat3Descripcion, pr.Cat1ID, c1.Cat1Descripcion{groupByCanal}";
                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
                foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                var crudo = new List<(int ProID, string Nombre, int Cat3ID, string Cat3Nombre, int Cat1ID, string Cat1Nombre, int CanID, double Presu, double Ejec, DateTime? Ultima)>();
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        crudo.Add((
                            rd.GetInt32(0), rd.IsDBNull(1) ? "" : rd.GetString(1),
                            rd.GetInt32(2), rd.GetString(3),
                            rd.GetInt32(4), rd.GetString(5),
                            rd.GetInt32(6),
                            rd.IsDBNull(7) ? 0 : Convert.ToDouble(rd.GetValue(7)),
                            rd.IsDBNull(8) ? 0 : Convert.ToDouble(rd.GetValue(8)),
                            rd.IsDBNull(9) ? (DateTime?)null : rd.GetDateTime(9)));
                    }
                }
                // FIX 2026-09-07: el JOIN/GROUP BY por canal ahora es incondicional (ver
                // comentario junto a joinCanal más arriba) — con porCanal=true cada fila por
                // canal se muestra tal cual (igual que antes); con porCanal=false (default)
                // se colapsan de vuelta a 1 fila por ProID, pero el Estimado se pondera con
                // la curva propia de CADA canal (AjustarEstimadoPorCanal) en vez de una sola
                // curva general compartida.
                if (porCanal)
                {
                    foreach (var f in crudo)
                    {
                        int diasT = PresupuestosCore.DiasTranscurridos(f.Ultima, anio, mes);
                        int canIdCurva = f.CanID > 0 ? f.CanID : 0;
                        var r = PresupuestosCore.Calcular(f.Presu, f.Ejec, diasT, diasDelMes, CurvaCat3Local(f.Cat3ID, canIdCurva), f.Ultima);
                        filas.Add(new
                        {
                            ProID = f.ProID, Nombre = f.Nombre,
                            Cat3ID = f.Cat3ID, Cat3Nombre = f.Cat3Nombre,
                            Cat1ID = f.Cat1ID, Cat1Nombre = f.Cat1Nombre,
                            CanID = f.CanID,
                            r.Presupuesto, r.PresupuestoAlDia, r.Ejecutado, r.AlcancePct, r.VentasProyectadas, r.ProyectadoPct,
                            r.VentasEstimadas, r.EstimadoPct,
                            r.DiasDelPeriodo, r.DiasTranscurridosAjustado, r.DiasRestantes, r.NecesidadDiaria,
                        });
                    }
                }
                else
                {
                    foreach (var grp in crudo.GroupBy(f => f.ProID))
                    {
                        var first = grp.First();
                        double presuTotal = grp.Sum(f => f.Presu), ejecTotal = grp.Sum(f => f.Ejec);
                        DateTime? ultimaBlend = grp.Any(f => f.Ultima.HasValue) ? grp.Where(f => f.Ultima.HasValue).Max(f => f.Ultima!.Value) : (DateTime?)null;
                        int diasT = PresupuestosCore.DiasTranscurridos(ultimaBlend, anio, mes);
                        var r = PresupuestosCore.Calcular(presuTotal, ejecTotal, diasT, diasDelMes, null, ultimaBlend);
                        var franjas = grp.Select(f => (f.Presu, f.Ejec, PresupuestosCore.DiasTranscurridos(f.Ultima, anio, mes),
                            sinCurva ? null : CurvaCat3Local(f.Cat3ID, f.CanID > 0 ? f.CanID : 0)));
                        PresupuestosCore.AjustarEstimadoPorCanal(r, franjas, diasDelMes);
                        filas.Add(new
                        {
                            ProID = first.ProID, Nombre = first.Nombre,
                            Cat3ID = first.Cat3ID, Cat3Nombre = first.Cat3Nombre,
                            Cat1ID = first.Cat1ID, Cat1Nombre = first.Cat1Nombre,
                            CanID = -1,
                            r.Presupuesto, r.PresupuestoAlDia, r.Ejecutado, r.AlcancePct, r.VentasProyectadas, r.ProyectadoPct,
                            r.VentasEstimadas, r.EstimadoPct,
                            r.DiasDelPeriodo, r.DiasTranscurridosAjustado, r.DiasRestantes, r.NecesidadDiaria,
                        });
                    }
                }
            }
            else
            {
                niveles = 1;
                string tabla, colId, colNombre;
                switch (familia)
                {
                    case "suplidor": tabla = "Categoria3"; colId = "Cat3ID"; colNombre = "Cat3Descripcion"; break;
                    case "categoria": tabla = "Categoria1"; colId = "Cat1ID"; colNombre = "Cat1Descripcion"; break;
                    default: tabla = ""; colId = ""; colNombre = ""; break; // grupoproducto: sin catálogo amigable todavía
                }
                string sql;
                if (tabla.Length > 0)
                {
                    // Filtro por la misma dimensión que se está pivotando (Suplidor filtra el
                    // pivote de Suplidor, Categoría filtra el pivote de Categoría) MÁS el
                    // filtro CRUZADO (Categoría filtra también el pivote de Suplidor y
                    // viceversa, vía el mapa Cat1<->Cat3 dominante — bug reportado por el
                    // usuario 2026-08-25, mismo mecanismo que ya se aplicó en Del Año).
                    var dimFiltro = familia == "suplidor" ? (vSuplidor.Count == 0 ? "" : BuildIn($"d.{colId}", vSuplidor, "ds", parms))
                                  : familia == "categoria" ? (vCategoria.Count == 0 ? "" : BuildIn($"d.{colId}", vCategoria, "dc", parms))
                                  : "";
                    var cruzadoFiltro = "";
                    if (familia == "categoria" && vSuplidor.Count > 0)
                    {
                        var mapaMes = ObtenerMapaCat1ACat3(cn, anio, mes);
                        var suplidorSet = vSuplidor.Select(s => int.TryParse(s, out var n) ? n : -1).ToHashSet();
                        var cat1Equiv = mapaMes.Where(kv => suplidorSet.Contains(kv.Value)).Select(kv => kv.Key.ToString()).ToList();
                        cruzadoFiltro = cat1Equiv.Count > 0 ? BuildIn("d.Cat1ID", cat1Equiv, "cx", parms) : "AND 1=0";
                    }
                    else if (familia == "suplidor" && vCategoria.Count > 0)
                    {
                        var mapaMes = ObtenerMapaCat1ACat3(cn, anio, mes);
                        var categoriaSet = vCategoria.Select(s => int.TryParse(s, out var n) ? n : -1).ToHashSet();
                        var cat3Equiv = mapaMes.Where(kv => categoriaSet.Contains(kv.Key)).Select(kv => kv.Value.ToString()).Distinct().ToList();
                        cruzadoFiltro = cat3Equiv.Count > 0 ? BuildIn("d.Cat3ID", cat3Equiv, "cx", parms) : "AND 1=0";
                    }
                    sql = $@"
SELECT d.{colId} Id, ISNULL(d.{colNombre},CAST(d.{colId} AS VARCHAR(20))) Nombre,
       {selectCanal} CanID,
       SUM(ph.PrePresupuesto) Presu, SUM(ph.PreEjecutado) Ejec, MAX(ph.PreFechaActualizacion) Ultima
FROM PresupuestosHistoricos ph WITH (NOLOCK)
INNER JOIN {tabla} d WITH (NOLOCK) ON CAST(d.{colId} AS VARCHAR(20))=ph.PreReferencia
{joinCanal}
WHERE ph.PreAnio=@anio AND ph.PreMes=@mes AND ph.PreTipo=@tp0
{repFiltro}
{dimFiltro}
{cruzadoFiltro}
GROUP BY d.{colId}, d.{colNombre}{groupByCanal}";
                }
                else
                {
                    // grupoproducto: sin join amigable todavía, se agrupa por el código crudo
                    sql = $@"
SELECT ph.PreReferencia Id, ph.PreReferencia Nombre,
       {selectCanal} CanID,
       SUM(ph.PrePresupuesto) Presu, SUM(ph.PreEjecutado) Ejec, MAX(ph.PreFechaActualizacion) Ultima
FROM PresupuestosHistoricos ph WITH (NOLOCK)
{joinCanal}
WHERE ph.PreAnio=@anio AND ph.PreMes=@mes AND ph.PreTipo=@tp0
{repFiltro}
GROUP BY ph.PreReferencia{groupByCanal}";
                }
                // grupoproducto no tiene Id numérico amigable (PreReferencia crudo) -> curva
                // general; suplidor/categoria sí, cada fila ES un Cat3ID/Cat1ID distinto — a
                // diferencia del bucle de arriba (niveles==3), aquí cada FILA visible ya es
                // un suplidor/categoría distinto, así que sin filtro esto es 1 query pesada
                // por fila. sinCurva evita el cálculo cuando no se va a mostrar (mismo caso
                // que el bucle de niveles==3, ver nota de CurvaCat3Local más arriba).
                double[]? CurvaFilaNivel1(object id, int canId) => sinCurva ? null : familia switch
                {
                    "suplidor" => CurvaCat3Local(Convert.ToInt32(id), canId),
                    "categoria" => CurvaCat1Local(Convert.ToInt32(id), canId),
                    _ => CurvaGeneralLocal(canId),
                };
                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
                foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                var crudo1 = new List<(object Id, string Nombre, int CanID, double Presu, double Ejec, DateTime? Ultima)>();
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        crudo1.Add((
                            rd.GetValue(0), rd.GetValue(1)?.ToString() ?? "",
                            rd.GetInt32(2),
                            rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3)),
                            rd.IsDBNull(4) ? 0 : Convert.ToDouble(rd.GetValue(4)),
                            rd.IsDBNull(5) ? (DateTime?)null : rd.GetDateTime(5)));
                    }
                }
                // FIX 2026-09-07: mismo tratamiento "canal siempre" que en el bucle de arriba
                // (niveles==3) — JOIN/GROUP BY por canal incondicional, colapsado de vuelta a
                // 1 fila por Id cuando porCanal=false, ponderando el Estimado con la curva
                // propia de cada canal en vez de la curva general compartida.
                if (porCanal)
                {
                    foreach (var f in crudo1)
                    {
                        int diasT = PresupuestosCore.DiasTranscurridos(f.Ultima, anio, mes);
                        int canIdCurva = f.CanID > 0 ? f.CanID : 0;
                        var r = PresupuestosCore.Calcular(f.Presu, f.Ejec, diasT, diasDelMes, CurvaFilaNivel1(f.Id, canIdCurva), f.Ultima);
                        filas.Add(new
                        {
                            Nombre = f.Nombre,
                            CanID = f.CanID,
                            r.Presupuesto, r.PresupuestoAlDia, r.Ejecutado, r.AlcancePct, r.VentasProyectadas, r.ProyectadoPct,
                            r.VentasEstimadas, r.EstimadoPct,
                            r.DiasDelPeriodo, r.DiasTranscurridosAjustado, r.DiasRestantes, r.NecesidadDiaria,
                        });
                    }
                }
                else
                {
                    foreach (var grp in crudo1.GroupBy(f => f.Id))
                    {
                        var first = grp.First();
                        double presuTotal = grp.Sum(f => f.Presu), ejecTotal = grp.Sum(f => f.Ejec);
                        DateTime? ultimaBlend = grp.Any(f => f.Ultima.HasValue) ? grp.Where(f => f.Ultima.HasValue).Max(f => f.Ultima!.Value) : (DateTime?)null;
                        int diasT = PresupuestosCore.DiasTranscurridos(ultimaBlend, anio, mes);
                        var r = PresupuestosCore.Calcular(presuTotal, ejecTotal, diasT, diasDelMes, null, ultimaBlend);
                        var franjas = grp.Select(f => (f.Presu, f.Ejec, PresupuestosCore.DiasTranscurridos(f.Ultima, anio, mes),
                            sinCurva ? null : CurvaFilaNivel1(f.Id, f.CanID > 0 ? f.CanID : 0)));
                        PresupuestosCore.AjustarEstimadoPorCanal(r, franjas, diasDelMes);
                        filas.Add(new
                        {
                            Nombre = first.Nombre,
                            CanID = -1,
                            r.Presupuesto, r.PresupuestoAlDia, r.Ejecutado, r.AlcancePct, r.VentasProyectadas, r.ProyectadoPct,
                            r.VentasEstimadas, r.EstimadoPct,
                            r.DiasDelPeriodo, r.DiasTranscurridosAjustado, r.DiasRestantes, r.NecesidadDiaria,
                        });
                    }
                }
            }

            return Ok(new { ok = true, niveles, diasDelMes, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Filtro de PreReferencia para las tablas DIARIAS ("DD-Cat1ID"/"DD-Cat3ID") — extrae
    // la parte de referencia (después del guion) y compara. Solo aplica a cat1/cat3
    // porque son las únicas dimensiones con variante diaria nativa.
    // internal (no solo private): reusado tal cual por ApiPdfPresController (informe
    // "Cierre Diario", 2026-09-03) — cero cambio de comportamiento, solo visibilidad,
    // mismo patrón ya usado con ParseList/BuildIn/ResolverReps/BuildRefFiltro.
    internal static string BuildRefFiltroDiario(string refTipo, List<string> categoria, List<string> suplidor, Dictionary<string, object> parms)
    {
        const string extraido = "RIGHT(PreReferencia, LEN(PreReferencia)-CHARINDEX('-',PreReferencia))";
        if (refTipo == "cat1") return categoria.Count == 0 ? "" : BuildIn(extraido, categoria, "dcat", parms);
        if (refTipo == "cat3") return suplidor.Count == 0 ? "" : BuildIn(extraido, suplidor, "dsup", parms);
        return "";
    }

    // Resuelve la FamCfg real a usar para los endpoints de "Del Día". Caso especial:
    // Suplidor+Dinero no tiene historial diario nativo, así que se DERIVA desde
    // Categoría+Dinero (que sí lo tiene) — el filtro de Suplidor (Cat3ID) se traduce a
    // las Categorías (Cat1ID) que le mapean como dominante (ObtenerMapaCat1ACat3).
    // Pedido explícito del usuario 2026-08-25.
    private (PresupuestosCore.FamCfg? cfg, List<string> vCategoria, bool derivado) ResolverCfgDiario(
        SqlConnection cn, string familia, string kpi, List<string> vCategoria, List<string> vSuplidor, int anio, int mes)
    {
        if (familia == "suplidor" && kpi == "dinero")
        {
            var cfgCat = PresupuestosCore.Mapa[("categoria", "dinero")];
            if (vSuplidor.Count == 0) return (cfgCat, vCategoria, true);
            var mapa = ObtenerMapaCat1ACat3(cn, anio, mes);
            var suplidorSet = vSuplidor.Select(s => int.TryParse(s, out var n) ? n : -1).ToHashSet();
            var cat1Equivalentes = mapa.Where(kv => suplidorSet.Contains(kv.Value)).Select(kv => kv.Key.ToString()).ToList();
            // Si el suplidor filtrado no mapea a ninguna categoría (sin presupuesto de
            // producto ese mes), forzar una lista con un Cat1ID inexistente (-1) para que
            // el filtro dé CERO resultados — una lista vacía se interpretaría como "sin
            // filtro" (mostraría todo), que sería el bug contrario.
            if (cat1Equivalentes.Count == 0) cat1Equivalentes.Add("-1");
            return (cfgCat, cat1Equivalentes, true);
        }
        var cfgNormal = PresupuestosCore.Mapa.TryGetValue((familia, kpi), out var c) ? c : null;
        return (cfgNormal, vCategoria, false);
    }

    // ── GET /apipresupuestos/deldia ──────────────────────────────────────────
    // Serie 1..díasDelMes en 1 sola query — solo para familia/kpi con variante diaria
    // nativa (Categoría: dinero/facturas/activaciones; Suplidor: solo activaciones).
    // Cuando existen TipoDiarioA+B (categoria/dinero) se devuelven ambas metas en
    // paralelo (lineal vs. ajustada por déficit, confirmado con el usuario que las 2
    // son válidas) — el resto de combinaciones solo tiene una serie diaria.
    [HttpGet("deldia")]
    public IActionResult DelDia(
        [FromQuery] int anio, [FromQuery] int mes, [FromQuery] string familia, [FromQuery] string kpi,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? suplidor, [FromQuery] string? categoria)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn();
            cn.Open();

            var (cfg, vCategoria, derivado) = ResolverCfgDiario(cn, familia, kpi, ParseList(categoria), ParseList(suplidor), anio, mes);
            if (cfg is null) return BadRequest(new { ok = false, error = $"Combinación familia/kpi inválida: {familia}/{kpi}" });
            if (cfg.TipoDiarioA is null) return BadRequest(new { ok = false, error = "Esta familia/KPI no tiene historial diario nativo." });

            var reps = ResolverReps(cn, centros, canales, divisiones, supervisores, vendedores);
            var huboFiltroOrg = new[] { centros, canales, divisiones, supervisores, vendedores }.Any(s => ParseList(s).Count > 0);
            if (huboFiltroOrg && reps.Count == 0) return Ok(new { ok = true, diasDelMes = 0, dias = Array.Empty<object>() });

            int diasDelMes = DateTime.DaysInMonth(anio, mes);

            var tipos = new List<string> { cfg.TipoDiarioA! };
            if (cfg.TipoDiarioB != null) tipos.Add(cfg.TipoDiarioB);
            var parms = new Dictionary<string, object> { ["@anio"] = anio, ["@mes"] = mes };
            var tipoNames = tipos.Select((t, i) => { var n = $"@tp{i}"; parms[n] = t; return n; }).ToList();
            var repFiltro = reps.Count == 0 ? "" : BuildIn("RepCodigo", reps, "rp", parms);
            // Cuando se deriva Suplidor+Dinero desde Categoría+Dinero, el filtro de Suplidor
            // ya se tradujo a las Categorías equivalentes (vCategoria) — no filtrar de nuevo
            // por Cat3ID (los datos subyacentes están indexados por Cat1ID, no Cat3ID).
            var refFiltro = BuildRefFiltroDiario(derivado ? "cat1" : cfg.Ref, vCategoria, derivado ? new List<string>() : ParseList(suplidor), parms);

            var sql = $@"
SELECT PreTipo, TRY_CAST(LEFT(PreReferencia, CHARINDEX('-',PreReferencia)-1) AS INT) Dia,
       SUM(PrePresupuesto) Presu, SUM(PreEjecutado) Ejec
FROM PresupuestosHistoricos WITH (NOLOCK)
WHERE PreAnio=@anio AND PreMes=@mes AND PreTipo IN ({string.Join(",", tipoNames)})
  AND CHARINDEX('-',PreReferencia) > 0
{repFiltro}
{refFiltro}
GROUP BY PreTipo, TRY_CAST(LEFT(PreReferencia, CHARINDEX('-',PreReferencia)-1) AS INT)";
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);

            var porTipo = new Dictionary<string, double[]>(); // tipo -> presu[1..31]
            var ejecPorDia = new double[32];
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    if (rd.IsDBNull(1)) continue;
                    string tipo = rd.GetString(0);
                    int dia = rd.GetInt32(1);
                    if (dia < 1 || dia > 31) continue;
                    double presu = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
                    double ejec = rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3));
                    if (!porTipo.TryGetValue(tipo, out var arr)) { arr = new double[32]; porTipo[tipo] = arr; }
                    arr[dia] = presu;
                    // el Ejecutado es casi idéntico entre 1DIA/2DIA (misma venta real) —
                    // se toma del primer tipo que aparezca, no se suma dos veces.
                    if (ejecPorDia[dia] == 0) ejecPorDia[dia] = ejec;
                }
            }

            var dias = new List<object>();
            porTipo.TryGetValue(cfg.TipoDiarioA!, out var arrA);
            double[]? arrB = null; if (cfg.TipoDiarioB != null) porTipo.TryGetValue(cfg.TipoDiarioB, out arrB);
            for (int d = 1; d <= diasDelMes; d++)
            {
                var fecha = new DateTime(anio, mes, d);
                dias.Add(new
                {
                    Dia = d,
                    DiaSemana = fecha.DayOfWeek.ToString(),
                    EsDomingo = fecha.DayOfWeek == DayOfWeek.Sunday,
                    PresupuestoA = arrA != null ? arrA[d] : (double?)null,
                    PresupuestoB = arrB != null ? arrB[d] : (double?)null,
                    Ejecutado = ejecPorDia[d],
                });
            }
            return Ok(new
            {
                ok = true,
                diasDelMes,
                etiquetaA = cfg.TipoDiarioB != null ? "Meta ajustada por déficit" : "Presupuesto diario",
                etiquetaB = cfg.TipoDiarioB != null ? "Meta lineal" : null,
                dias,
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apipresupuestos/deldia/drill ───────────────────────────────────
    // Drill-down Centro→Supervisor→Vendedor para UNO O VARIOS DÍAS específicos (no todo
    // el mes) — mismas 3 capas que /drill/organizacion, mismo patrón client-side, pero la
    // fuente es el historial diario nativo (solo Categoría/Suplidor lo tienen). Cada fila
    // trae su propio "Dia" — el front decide si comparar día a día (pivote) o sumarlos
    // (agregado), pedido explícito del usuario 2026-08-25. Sin Proyectado/Estimado aquí:
    // son conceptos de PROYECCIÓN DE MES, no aplican a un día — se muestra Presupuesto
    // lineal/ajustado + Ejecutado + Alcance% de cada día, por vendedor.
    [HttpGet("deldia/drill")]
    public IActionResult DelDiaDrill(
        [FromQuery] int anio, [FromQuery] int mes, [FromQuery] string dias, [FromQuery] string familia, [FromQuery] string kpi,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? suplidor, [FromQuery] string? categoria)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn();
            cn.Open();

            var (cfg, vCategoria, derivado) = ResolverCfgDiario(cn, familia, kpi, ParseList(categoria), ParseList(suplidor), anio, mes);
            if (cfg is null) return BadRequest(new { ok = false, error = $"Combinación familia/kpi inválida: {familia}/{kpi}" });
            if (cfg.TipoDiarioA is null) return BadRequest(new { ok = false, error = "Esta familia/KPI no tiene historial diario nativo." });

            var reps = ResolverReps(cn, centros, canales, divisiones, supervisores, vendedores);
            var huboFiltroOrg = new[] { centros, canales, divisiones, supervisores, vendedores }.Any(s => ParseList(s).Count > 0);
            if (huboFiltroOrg && reps.Count == 0) return Ok(new { ok = true, filas = Array.Empty<object>() });

            var vDias = ParseList(dias).Select(s => int.TryParse(s, out var n) ? n : -1).Where(n => n >= 1 && n <= 31).ToList();
            if (vDias.Count == 0) return Ok(new { ok = true, filas = Array.Empty<object>() });

            var parms = new Dictionary<string, object> { ["@anio"] = anio, ["@mes"] = mes, ["@tpA"] = cfg.TipoDiarioA! };
            var tipoIn = new List<string> { "@tpA" };
            if (cfg.TipoDiarioB != null) { parms["@tpB"] = cfg.TipoDiarioB; tipoIn.Add("@tpB"); }
            var repFiltro = reps.Count == 0 ? "" : BuildIn("ph.RepCodigo", reps, "rp", parms);
            var refFiltro = BuildRefFiltroDiario(derivado ? "cat1" : cfg.Ref, vCategoria, derivado ? new List<string>() : ParseList(suplidor), parms).Replace("RIGHT(PreReferencia,", "RIGHT(ph.PreReferencia,");
            var diaFiltro = BuildIn("TRY_CAST(LEFT(ph.PreReferencia, CHARINDEX('-',ph.PreReferencia)-1) AS INT)", vDias.Select(d => d.ToString()).ToList(), "di", parms);

            var presuBExpr = cfg.TipoDiarioB != null ? "SUM(CASE WHEN ph.PreTipo=@tpB THEN ph.PrePresupuesto ELSE 0 END)" : "CAST(NULL AS DECIMAL(18,2))";
            // Ejecutado: SUM (no MAX) de UN SOLO tipo diario (B si existe, si no A) filtrado por
            // PreTipo — MAX(ph.PreEjecutado) estaba mal: sin filtrar por una Categoría/Suplidor
            // específico, un mismo vendedor tiene 1 fila POR CADA Cat1ID ese día, y MAX() se
            // quedaba solo con la categoría más grande, descartando las demás (bug real,
            // detectado 2026-08-25: mostraba ~1.27M cuando el ejecutado real era ~2.8M).
            var ejecExpr = cfg.TipoDiarioB != null ? "SUM(CASE WHEN ph.PreTipo=@tpB THEN ph.PreEjecutado ELSE 0 END)" : "SUM(CASE WHEN ph.PreTipo=@tpA THEN ph.PreEjecutado ELSE 0 END)";
            var sql = $@"
SELECT ph.RepCodigo, r.RepNombre, ISNULL(r.CedCodigo,'') CedCodigo, ISNULL(cd.CedDescripcion,ISNULL(r.CedCodigo,'(Sin centro)')) CentroNombre,
       ISNULL(r.RepSupervisor,'') SupCod, ISNULL(sup.RepNombre,r.RepSupervisor) SupNombre,
       TRY_CAST(LEFT(ph.PreReferencia, CHARINDEX('-',ph.PreReferencia)-1) AS INT) Dia,
       SUM(CASE WHEN ph.PreTipo=@tpA THEN ph.PrePresupuesto ELSE 0 END) PresuA,
       {presuBExpr} PresuB,
       {ejecExpr} Ejec
FROM PresupuestosHistoricos ph WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=ph.RepCodigo
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
WHERE ph.PreAnio=@anio AND ph.PreMes=@mes AND ph.PreTipo IN ({string.Join(",", tipoIn)})
  AND CHARINDEX('-',ph.PreReferencia) > 0
{diaFiltro}
{repFiltro}
{refFiltro}
GROUP BY ph.RepCodigo, r.RepNombre, r.CedCodigo, cd.CedDescripcion, r.RepSupervisor, sup.RepNombre,
         TRY_CAST(LEFT(ph.PreReferencia, CHARINDEX('-',ph.PreReferencia)-1) AS INT)";

            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);

            var filas = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                if (rd.IsDBNull(6)) continue;
                double presuA = rd.IsDBNull(7) ? 0 : Convert.ToDouble(rd.GetValue(7));
                double? presuB = rd.IsDBNull(8) ? null : Convert.ToDouble(rd.GetValue(8));
                double ejec = rd.IsDBNull(9) ? 0 : Convert.ToDouble(rd.GetValue(9));
                filas.Add(new
                {
                    RepCodigo = rd.GetString(0), RepNombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    CedCodigo = rd.GetString(2), CentroNombre = rd.GetString(3),
                    SupCod = rd.GetString(4), SupNombre = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    Dia = rd.GetInt32(6),
                    PresupuestoA = presuA, PresupuestoB = presuB, Ejecutado = ejec,
                    AlcancePct = presuA != 0 ? 100.0 * ejec / presuA : (double?)null,
                    AlcanceLinealPct = presuB.HasValue && presuB.Value != 0 ? 100.0 * ejec / presuB.Value : (double?)null,
                });
            }
            return Ok(new { ok = true, dias = vDias, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apipresupuestos/deldia/drillproducto ───────────────────────────
    // Desglose por Categoría (familia=categoria) o Suplidor (familia=suplidor) para UNO
    // O VARIOS días — igual patrón que DelDiaDrill pero agrupando por el Ref de la
    // familia (Cat1ID/Cat3ID) en vez de por RepCodigo. Cada fila trae su "Dia" (el front
    // decide comparación vs. suma). Solo aplica a combinaciones con historial diario
    // nativo (Ref cat1/cat3) — Producto/Grupo no tienen esa granularidad.
    [HttpGet("deldia/drillproducto")]
    public IActionResult DelDiaDrillProducto(
        [FromQuery] int anio, [FromQuery] int mes, [FromQuery] string dias, [FromQuery] string familia, [FromQuery] string kpi,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? suplidor, [FromQuery] string? categoria)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn();
            cn.Open();

            // Suplidor+Dinero se DERIVA de Categoría+Dinero: se agrupa por Cat1ID (fuente
            // real) y se re-mapea a Cat3ID después, vía ObtenerMapaCat1ACat3. Pedido
            // explícito del usuario 2026-08-25.
            bool suplidorDineroDerivado = familia == "suplidor" && kpi == "dinero";
            PresupuestosCore.FamCfg? cfg;
            if (suplidorDineroDerivado) cfg = PresupuestosCore.Mapa[("categoria", "dinero")];
            else PresupuestosCore.Mapa.TryGetValue((familia, kpi), out cfg);
            if (cfg is null) return BadRequest(new { ok = false, error = $"Combinación familia/kpi inválida: {familia}/{kpi}" });
            if (cfg.TipoDiarioA is null) return BadRequest(new { ok = false, error = "Esta familia/KPI no tiene historial diario nativo." });
            if (!suplidorDineroDerivado && cfg.Ref != "cat1" && cfg.Ref != "cat3")
                return BadRequest(new { ok = false, error = "Esta vista por Categoría/Suplidor solo aplica a esas 2 familias." });

            var reps = ResolverReps(cn, centros, canales, divisiones, supervisores, vendedores);
            var huboFiltroOrg = new[] { centros, canales, divisiones, supervisores, vendedores }.Any(s => ParseList(s).Count > 0);
            if (huboFiltroOrg && reps.Count == 0) return Ok(new { ok = true, filas = Array.Empty<object>() });

            var vDias = ParseList(dias).Select(s => int.TryParse(s, out var n) ? n : -1).Where(n => n >= 1 && n <= 31).ToList();
            if (vDias.Count == 0) return Ok(new { ok = true, filas = Array.Empty<object>() });

            var parms = new Dictionary<string, object> { ["@anio"] = anio, ["@mes"] = mes, ["@tpA"] = cfg.TipoDiarioA! };
            var tipoIn = new List<string> { "@tpA" };
            if (cfg.TipoDiarioB != null) { parms["@tpB"] = cfg.TipoDiarioB; tipoIn.Add("@tpB"); }
            var repFiltro = reps.Count == 0 ? "" : BuildIn("ph.RepCodigo", reps, "rp", parms);
            var presuBExpr = cfg.TipoDiarioB != null ? "SUM(CASE WHEN ph.PreTipo=@tpB THEN ph.PrePresupuesto ELSE 0 END)" : "CAST(NULL AS DECIMAL(18,2))";
            var ejecExpr = cfg.TipoDiarioB != null ? "SUM(CASE WHEN ph.PreTipo=@tpB THEN ph.PreEjecutado ELSE 0 END)" : "SUM(CASE WHEN ph.PreTipo=@tpA THEN ph.PreEjecutado ELSE 0 END)";
            const string idExtraido = "TRY_CAST(RIGHT(ph.PreReferencia, LEN(ph.PreReferencia)-CHARINDEX('-',ph.PreReferencia)) AS INT)";
            const string diaExtraido = "TRY_CAST(LEFT(ph.PreReferencia, CHARINDEX('-',ph.PreReferencia)-1) AS INT)";
            var diaFiltro = BuildIn(diaExtraido, vDias.Select(d => d.ToString()).ToList(), "di", parms);

            string refReal = suplidorDineroDerivado ? "cat1" : cfg.Ref;
            string joinCatalogo, nombreExpr;
            if (refReal == "cat1")
            {
                joinCatalogo = $"LEFT JOIN Categoria1 c1 WITH (NOLOCK) ON c1.Cat1ID = {idExtraido}";
                nombreExpr = "ISNULL(c1.Cat1Descripcion,'(Sin categoría)')";
            }
            else
            {
                joinCatalogo = $"LEFT JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = {idExtraido}";
                nombreExpr = "ISNULL(c3.Cat3Descripcion,'(Sin suplidor)')";
            }
            // Filtro por la misma dimensión mostrada (Categoría filtra el pivote de
            // Categoría, Suplidor el de Suplidor). Caso derivado (Suplidor+Dinero): los
            // datos crudos están indexados por Cat1ID, no Cat3ID — se traduce el filtro de
            // Suplidor a sus Categorías equivalentes vía ObtenerMapaCat1ACat3 (mismo mapeo
            // que ya se usa para construir el pivote). Bug reportado por el usuario
            // 2026-08-25: al cambiar Del Día de "Por Organización" a "Por Suplidor", el
            // filtro de Suplidor dejaba de tener efecto — exactamente este caso.
            var idFiltro = "";
            if (suplidorDineroDerivado)
            {
                var vSuplidorFiltro = ParseList(suplidor);
                if (vSuplidorFiltro.Count > 0)
                {
                    var mapaFiltro = ObtenerMapaCat1ACat3(cn, anio, mes);
                    var suplidorSet = vSuplidorFiltro.Select(s => int.TryParse(s, out var n) ? n : -1).ToHashSet();
                    var cat1Equivalentes = mapaFiltro.Where(kv => suplidorSet.Contains(kv.Value)).Select(kv => kv.Key.ToString()).ToList();
                    idFiltro = cat1Equivalentes.Count > 0 ? BuildIn(idExtraido, cat1Equivalentes, "fd", parms) : "AND 1=0";
                }
            }
            else
            {
                var vDim = refReal == "cat1" ? ParseList(categoria) : ParseList(suplidor);
                if (vDim.Count > 0) idFiltro = BuildIn(idExtraido, vDim, "fd", parms);
            }

            var sql = $@"
SELECT {idExtraido} Id, {nombreExpr} Nombre, {diaExtraido} Dia,
       SUM(CASE WHEN ph.PreTipo=@tpA THEN ph.PrePresupuesto ELSE 0 END) PresuA,
       {presuBExpr} PresuB,
       {ejecExpr} Ejec
FROM PresupuestosHistoricos ph WITH (NOLOCK)
{joinCatalogo}
WHERE ph.PreAnio=@anio AND ph.PreMes=@mes AND ph.PreTipo IN ({string.Join(",", tipoIn)})
  AND CHARINDEX('-',ph.PreReferencia) > 0
{diaFiltro}
{idFiltro}
{repFiltro}
GROUP BY {idExtraido}, {nombreExpr}, {diaExtraido}";

            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);

            var crudo = new List<(int id, string nombre, int dia, double presuA, double? presuB, double ejec)>();
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    if (rd.IsDBNull(0) || rd.IsDBNull(2)) continue;
                    double presuA = rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3));
                    double? presuB = rd.IsDBNull(4) ? null : Convert.ToDouble(rd.GetValue(4));
                    double ejec = rd.IsDBNull(5) ? 0 : Convert.ToDouble(rd.GetValue(5));
                    crudo.Add((rd.GetInt32(0), rd.GetString(1), rd.GetInt32(2), presuA, presuB, ejec));
                }
            }

            List<(int id, string nombre, int dia, double presuA, double? presuB, double ejec, int? padreId, string? padreNombre)> final;
            string dimension; int niveles;
            if (suplidorDineroDerivado)
            {
                // Grano fino = Categoría (Cat1ID, ya viene así en 'crudo' porque refReal="cat1"
                // para este caso), con su Suplidor (Cat3ID) dominante adjunto como "padre" — el
                // front arma un drill Suplidor→Categoría con esto, igual patrón que
                // Centro→Supervisor. Pedido explícito del usuario 2026-08-25.
                var mapa = ObtenerMapaCat1ACat3(cn, anio, mes);
                var nombresCat3 = new Dictionary<int, string>();
                using (var cmdN = new SqlCommand("SELECT Cat3ID, Cat3Descripcion FROM Categoria3 WITH (NOLOCK)", cn))
                using (var rdN = cmdN.ExecuteReader())
                    while (rdN.Read()) nombresCat3[rdN.GetInt32(0)] = rdN.IsDBNull(1) ? "" : rdN.GetString(1);

                final = crudo.Where(f => mapa.ContainsKey(f.id)) // categoría sin suplidor dominante (sin presupuesto de producto) -> se omite
                    .Select(f =>
                    {
                        int cat3 = mapa[f.id];
                        string cat3Nombre = nombresCat3.TryGetValue(cat3, out var n) ? n : "(Sin suplidor)";
                        return (f.id, f.nombre, f.dia, f.presuA, f.presuB, f.ejec, (int?)cat3, (string?)cat3Nombre);
                    }).ToList();
                dimension = "suplidor";
                niveles = 2;
            }
            else
            {
                final = crudo.Select(f => (f.id, f.nombre, f.dia, f.presuA, f.presuB, f.ejec, (int?)null, (string?)null)).ToList();
                dimension = refReal == "cat1" ? "categoria" : "suplidor";
                niveles = 1;
            }

            var filas = final.Select(f => new
            {
                Id = f.id, Nombre = f.nombre, Dia = f.dia,
                PresupuestoA = f.presuA, PresupuestoB = f.presuB, Ejecutado = f.ejec,
                AlcancePct = f.presuA != 0 ? 100.0 * f.ejec / f.presuA : (double?)null,
                AlcanceLinealPct = f.presuB.HasValue && f.presuB.Value != 0 ? 100.0 * f.ejec / f.presuB.Value : (double?)null,
                PadreId = f.padreId, PadreNombre = f.padreNombre,
            });
            return Ok(new { ok = true, dimension, niveles, dias = vDias, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apipresupuestos/delanio ────────────────────────────────────────
    // 1 fetch a grano (entidad × mes) para el año completo — el front arma el chart
    // (totales por mes, sumados aquí mismo en C# a partir de "filas", sin 2da query)
    // y el pivote (filas=entidad, columnas=Ene..Dic), mismo patrón sin refetch por nivel.
    [HttpGet("delanio")]
    public IActionResult DelAnio(
        [FromQuery] int anio, [FromQuery] string dim, [FromQuery] string familia, [FromQuery] string kpi,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? suplidor, [FromQuery] string? categoria, [FromQuery] string? producto)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (!PresupuestosCore.Mapa.TryGetValue((familia, kpi), out var cfg))
            return BadRequest(new { ok = false, error = $"Combinación familia/kpi inválida: {familia}/{kpi}" });
        try
        {
            using var cn = NewConn();
            cn.Open();

            var reps = ResolverReps(cn, centros, canales, divisiones, supervisores, vendedores);
            var huboFiltroOrg = new[] { centros, canales, divisiones, supervisores, vendedores }.Any(s => ParseList(s).Count > 0);
            if (huboFiltroOrg && reps.Count == 0) return Ok(new { ok = true, meses = Array.Empty<object>(), filas = Array.Empty<object>() });

            var vSuplidor = ParseList(suplidor); var vCategoria = ParseList(categoria); var vProducto = ParseList(producto);
            var fuente = ResolverFuenteProducto(familia, kpi, vSuplidor, vProducto);
            var parms = new Dictionary<string, object> { ["@anio"] = anio, ["@tp0"] = fuente?.TipoReal ?? cfg.TipoMensual };
            var repFiltro = reps.Count == 0 ? "" : BuildIn("ph.RepCodigo", reps, "rp", parms);
            string joinProd = "", refFiltro, sumaPresu = "SUM(ph.PrePresupuesto)", sumaEjec = "SUM(ph.PreEjecutado)";
            if (fuente != null) (joinProd, refFiltro, sumaPresu, sumaEjec) = FragmentosProducto(fuente, vSuplidor, vCategoria, vProducto, parms, "ph");
            else refFiltro = BuildRefFiltro(cfg.Ref, vSuplidor, vCategoria, vProducto, parms, "ph");

            // filaKey -> (nombre, [extra cols para el pivote], mes -> (presu,ejec))
            var entidades = new Dictionary<string, (string nombre, string[] extra, Dictionary<int, (double presu, double ejec)> porMes)>();

            if (dim == "organizacion")
            {
                var sql = $@"
SELECT ph.RepCodigo, r.RepNombre, ISNULL(cd.CedDescripcion,ISNULL(r.CedCodigo,'(Sin centro)')) CentroNombre, ISNULL(sup.RepNombre,r.RepSupervisor) SupNombre,
       ph.PreMes, {sumaPresu} Presu, {sumaEjec} Ejec
FROM PresupuestosHistoricos ph WITH (NOLOCK)
{joinProd}
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=ph.RepCodigo
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
WHERE ph.PreAnio=@anio AND ph.PreTipo=@tp0
{repFiltro}
{refFiltro}
GROUP BY ph.RepCodigo, r.RepNombre, cd.CedDescripcion, r.CedCodigo, sup.RepNombre, r.RepSupervisor, ph.PreMes";
                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 90 };
                foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    string key = rd.GetString(0);
                    if (!entidades.TryGetValue(key, out var e))
                    {
                        e = ($"{(rd.IsDBNull(1) ? "" : rd.GetString(1))} ({key})", new[] { rd.GetString(2), rd.IsDBNull(3) ? "" : rd.GetString(3) }, new Dictionary<int, (double, double)>());
                        entidades[key] = e;
                    }
                    int m = rd.GetInt16(4);
                    double presu = rd.IsDBNull(5) ? 0 : Convert.ToDouble(rd.GetValue(5));
                    double ejec = rd.IsDBNull(6) ? 0 : Convert.ToDouble(rd.GetValue(6));
                    e.porMes[m] = (presu, ejec);
                }
            }
            else // producto: pivote plano al nivel hoja (Producto, o Suplidor/Categoría/Grupo si la familia no tiene más profundidad)
            {
                string sql;
                if (fuente != null)
                {
                    // Suplidor/Categoría/Producto escalan SIEMPRE desde MNTDPROD/Unddprod (ver
                    // ResolverFuenteProducto) — al pasar por Productos se tiene Cat3ID Y Cat1ID en
                    // la MISMA fila, así que el filtro cruzado Suplidor↔Categoría ya no necesita
                    // el mapa "dominante" aproximado (ObtenerMapaCat1ACat3Anio): es exacto.
                    string selectCols, groupCols, joinExtra;
                    if (familia == "producto")
                    {
                        selectCols = "CAST(fpr.ProID AS VARCHAR(20)) K, fpr.ProDescripcion Nombre, ISNULL(c3.Cat3Descripcion,'') Extra1";
                        groupCols = "fpr.ProID, fpr.ProDescripcion, c3.Cat3Descripcion";
                        joinExtra = "LEFT JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID=fpr.Cat3ID";
                    }
                    else if (familia == "suplidor")
                    {
                        selectCols = "CAST(fpr.Cat3ID AS VARCHAR(20)) K, ISNULL(c3.Cat3Descripcion,CAST(fpr.Cat3ID AS VARCHAR(20))) Nombre, '' Extra1";
                        groupCols = "fpr.Cat3ID, c3.Cat3Descripcion";
                        joinExtra = "LEFT JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID=fpr.Cat3ID";
                    }
                    else // categoria con equivalente por producto (dinero/unidades/activaciones)
                    {
                        selectCols = "CAST(fpr.Cat1ID AS VARCHAR(20)) K, ISNULL(c1.Cat1Descripcion,CAST(fpr.Cat1ID AS VARCHAR(20))) Nombre, '' Extra1";
                        groupCols = "fpr.Cat1ID, c1.Cat1Descripcion";
                        joinExtra = "LEFT JOIN Categoria1 c1 WITH (NOLOCK) ON c1.Cat1ID=fpr.Cat1ID";
                    }
                    sql = $@"
SELECT {selectCols},
       ph.PreMes, {sumaPresu} Presu, {sumaEjec} Ejec
FROM PresupuestosHistoricos ph WITH (NOLOCK)
{joinProd}
{joinExtra}
WHERE ph.PreAnio=@anio AND ph.PreTipo=@tp0
{repFiltro}
{refFiltro}
GROUP BY {groupCols}, ph.PreMes";
                }
                else
                {
                    // Sin equivalente por producto: grupoproducto (Productos permite VARIOS
                    // grupos por producto, atribución ambigua, fuera de alcance) y
                    // Categoría+Facturas (UNDDLINFACT no existe agregado por producto) — se
                    // mantiene el PreTipo propio de la familia con el filtro cruzado aproximado.
                    string tabla = familia == "suplidor" ? "Categoria3" : familia == "categoria" ? "Categoria1" : "";
                    string colId = familia == "suplidor" ? "Cat3ID" : familia == "categoria" ? "Cat1ID" : "";
                    string colNombre = familia == "suplidor" ? "Cat3Descripcion" : familia == "categoria" ? "Cat1Descripcion" : "";
                    var cruzadoFiltro = "";
                    if (tabla.Length > 0)
                    {
                        var mapaAnio = ObtenerMapaCat1ACat3Anio(cn, anio);
                        if (familia == "categoria" && vSuplidor.Count > 0)
                        {
                            var suplidorSet = vSuplidor.Select(s => int.TryParse(s, out var n) ? n : -1).ToHashSet();
                            var cat1Equiv = mapaAnio.Where(kv => suplidorSet.Contains(kv.Value)).Select(kv => kv.Key.ToString()).ToList();
                            cruzadoFiltro = cat1Equiv.Count > 0 ? BuildIn("d.Cat1ID", cat1Equiv, "cx", parms) : "AND 1=0";
                        }
                        else if (familia == "suplidor" && vCategoria.Count > 0)
                        {
                            var categoriaSet = vCategoria.Select(s => int.TryParse(s, out var n) ? n : -1).ToHashSet();
                            var cat3Equiv = mapaAnio.Where(kv => categoriaSet.Contains(kv.Key)).Select(kv => kv.Value.ToString()).Distinct().ToList();
                            cruzadoFiltro = cat3Equiv.Count > 0 ? BuildIn("d.Cat3ID", cat3Equiv, "cx", parms) : "AND 1=0";
                        }
                    }
                    sql = tabla.Length > 0
                        ? $@"
SELECT CAST(d.{colId} AS VARCHAR(20)) K, ISNULL(d.{colNombre},CAST(d.{colId} AS VARCHAR(20))) Nombre, '' Extra1,
       ph.PreMes, SUM(ph.PrePresupuesto) Presu, SUM(ph.PreEjecutado) Ejec
FROM PresupuestosHistoricos ph WITH (NOLOCK)
INNER JOIN {tabla} d WITH (NOLOCK) ON CAST(d.{colId} AS VARCHAR(20))=ph.PreReferencia
WHERE ph.PreAnio=@anio AND ph.PreTipo=@tp0
{repFiltro}
{refFiltro}
{cruzadoFiltro}
GROUP BY d.{colId}, d.{colNombre}, ph.PreMes"
                        : $@"
SELECT ph.PreReferencia K, ph.PreReferencia Nombre, '' Extra1,
       ph.PreMes, SUM(ph.PrePresupuesto) Presu, SUM(ph.PreEjecutado) Ejec
FROM PresupuestosHistoricos ph WITH (NOLOCK)
WHERE ph.PreAnio=@anio AND ph.PreTipo=@tp0
{repFiltro}
{refFiltro}
GROUP BY ph.PreReferencia, ph.PreMes";
                }
                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 90 };
                foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    string key = rd.GetValue(0)?.ToString() ?? "";
                    if (!entidades.TryGetValue(key, out var e))
                    {
                        e = (rd.GetValue(1)?.ToString() ?? "", new[] { rd.GetValue(2)?.ToString() ?? "" }, new Dictionary<int, (double, double)>());
                        entidades[key] = e;
                    }
                    int m = rd.GetInt16(3);
                    double presu = rd.IsDBNull(4) ? 0 : Convert.ToDouble(rd.GetValue(4));
                    double ejec = rd.IsDBNull(5) ? 0 : Convert.ToDouble(rd.GetValue(5));
                    e.porMes[m] = (presu, ejec);
                }
            }

            // Totales por mes (para el chart) — se derivan sumando "filas", sin 2da query.
            var mesesTotales = new Dictionary<int, (double presu, double ejec)>();
            foreach (var e in entidades.Values)
                foreach (var (m, v) in e.porMes)
                {
                    var acc = mesesTotales.TryGetValue(m, out var cur) ? cur : (0, 0);
                    mesesTotales[m] = (acc.Item1 + v.presu, acc.Item2 + v.ejec);
                }
            var meses = Enumerable.Range(1, 12).Select(m => new
            {
                Mes = m,
                Presupuesto = mesesTotales.TryGetValue(m, out var v) ? v.presu : 0,
                Ejecutado = mesesTotales.TryGetValue(m, out var v2) ? v2.ejec : 0,
            });

            var filas = entidades.Select(kv => new
            {
                Key = kv.Key,
                Nombre = kv.Value.nombre,
                Extra = kv.Value.extra,
                PorMes = Enumerable.Range(1, 12).ToDictionary(m => m, m => kv.Value.porMes.TryGetValue(m, out var v) ? new { v.presu, v.ejec } : new { presu = 0.0, ejec = 0.0 }),
                TotalPresupuesto = kv.Value.porMes.Values.Sum(v => v.presu),
                TotalEjecutado = kv.Value.porMes.Values.Sum(v => v.ejec),
            }).OrderByDescending(f => f.TotalPresupuesto).ToList();

            return Ok(new { ok = true, meses, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apipresupuestos/historico ──────────────────────────────────────
    // Vista "solo ejecución" para períodos ya cerrados, pedida aparte por el usuario —
    // a propósito NO incluye Presupuesto/Proyectado/Estimado en la respuesta, para que
    // el front no pueda mostrarlos por error en lo que debe ser un reporte histórico simple.
    [HttpGet("historico")]
    public IActionResult Historico(
        [FromQuery] int anioIni, [FromQuery] int anioFin, [FromQuery] string familia, [FromQuery] string kpi,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? suplidor, [FromQuery] string? categoria, [FromQuery] string? producto)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (!PresupuestosCore.Mapa.TryGetValue((familia, kpi), out var cfg))
            return BadRequest(new { ok = false, error = $"Combinación familia/kpi inválida: {familia}/{kpi}" });
        try
        {
            using var cn = NewConn();
            cn.Open();

            var reps = ResolverReps(cn, centros, canales, divisiones, supervisores, vendedores);
            var huboFiltroOrg = new[] { centros, canales, divisiones, supervisores, vendedores }.Any(s => ParseList(s).Count > 0);
            if (huboFiltroOrg && reps.Count == 0) return Ok(new { ok = true, serie = Array.Empty<object>() });

            var vSuplidor = ParseList(suplidor); var vCategoria = ParseList(categoria); var vProducto = ParseList(producto);
            var fuente = ResolverFuenteProducto(familia, kpi, vSuplidor, vProducto);
            var parms = new Dictionary<string, object> { ["@anioIni"] = anioIni, ["@anioFin"] = anioFin, ["@tp0"] = fuente?.TipoReal ?? cfg.TipoMensual };
            var repFiltro = reps.Count == 0 ? "" : BuildIn("ph.RepCodigo", reps, "rp", parms);
            string joinProd = "", refFiltro, sumaEjec = "SUM(ph.PreEjecutado)";
            if (fuente != null) (joinProd, refFiltro, _, sumaEjec) = FragmentosProducto(fuente, vSuplidor, vCategoria, vProducto, parms, "ph");
            else refFiltro = BuildRefFiltro(cfg.Ref, vSuplidor, vCategoria, vProducto, parms, "ph");

            var sql = $@"
SELECT ph.PreAnio, ph.PreMes, {sumaEjec} Ejec
FROM PresupuestosHistoricos ph WITH (NOLOCK)
{joinProd}
WHERE ph.PreAnio BETWEEN @anioIni AND @anioFin AND ph.PreTipo=@tp0
{repFiltro}
{refFiltro}
GROUP BY ph.PreAnio, ph.PreMes
ORDER BY ph.PreAnio, ph.PreMes";
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            var serie = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                serie.Add(new { Anio = rd.GetInt16(0), Mes = rd.GetInt16(1), Ejecutado = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2)) });

            return Ok(new { ok = true, serie });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    private double SumarPreTipo(SqlConnection cn, string preTipo, bool ejecutado, int anio, int mes, List<string> reps)
    {
        var parms = new Dictionary<string, object> { ["@anio"] = anio, ["@mes"] = mes, ["@tp0"] = preTipo };
        var repFiltro = reps.Count == 0 ? "" : BuildIn("RepCodigo", reps, "rp", parms);
        var col = ejecutado ? "PreEjecutado" : "PrePresupuesto";
        var sql = $"SELECT SUM({col}) FROM PresupuestosHistoricos WITH (NOLOCK) WHERE PreAnio=@anio AND PreMes=@mes AND PreTipo=@tp0 {repFiltro}";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        var v = cmd.ExecuteScalar();
        return v == null || v == DBNull.Value ? 0.0 : Convert.ToDouble(v);
    }

    // ── GET /apipresupuestos/extras ──────────────────────────────────────────
    // Las 4 métricas que no encajan en el modelo Alcance/Proyectado/Estimado:
    // Devoluciones (siempre negativas para el negocio, no se mezclan con ventas),
    // Meta Gerencial (PreEjecutado siempre 0 — solo objetivo, sin seguimiento real),
    // Efectividad de Visitas (conteo de visitas, no es venta),
    // Retención 2 meses (clientes activados en ventana de 2 meses, sin meta real).
    [HttpGet("extras")]
    public IActionResult Extras(
        [FromQuery] int anio, [FromQuery] int mes,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            var reps = ResolverReps(cn, centros, canales, divisiones, supervisores, vendedores);
            var huboFiltroOrg = new[] { centros, canales, divisiones, supervisores, vendedores }.Any(s => ParseList(s).Count > 0);
            if (huboFiltroOrg && reps.Count == 0)
                return Ok(new { ok = true, devolucionesPresupuesto = 0.0, devolucionesEjecutado = 0.0, metaGerencial = 0.0, efectividadPresupuesto = 0.0, efectividadEjecutado = 0.0, retencion2Meses = 0.0 });

            var devolucionesPresupuesto = SumarPreTipo(cn, PresupuestosCore.TipoDevoluciones, false, anio, mes, reps);
            var devolucionesEjecutado = SumarPreTipo(cn, PresupuestosCore.TipoDevoluciones, true, anio, mes, reps);
            var metaGerencial = SumarPreTipo(cn, PresupuestosCore.TipoMetaGerencial, false, anio, mes, reps);
            var efectividadPresupuesto = SumarPreTipo(cn, PresupuestosCore.TipoEfectividadVisitas, false, anio, mes, reps);
            var efectividadEjecutado = SumarPreTipo(cn, PresupuestosCore.TipoEfectividadVisitas, true, anio, mes, reps);
            var retencion2Meses = SumarPreTipo(cn, PresupuestosCore.TipoRetencion2Meses, true, anio, mes, reps);

            return Ok(new { ok = true, devolucionesPresupuesto, devolucionesEjecutado, metaGerencial, efectividadPresupuesto, efectividadEjecutado, retencion2Meses });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }
}
