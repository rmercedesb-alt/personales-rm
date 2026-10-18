#pragma warning disable CA1416 // System.Drawing: el servidor del Dashboard es Windows (IIS)
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.SqlClient;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

// ── API del modulo Geolocalizaciones ────────────────────────────────────────
// Ruta base /apigeo para no colisionar con /api (Visitas) ni /apip (Productividad)
[ApiController]
[Route("apigeo")]
[RequiereModulo("geo")]
public class ApiGeoController(IConfiguration config, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) : ControllerBase
{
    private SqlConnection NewConn() =>
        new(config.GetConnectionString("Default"));

    private bool IsAuth => HttpContext.Session.GetString("user_login") is not null;

    // ── Helpers ────────────────────────────────────────────────────────────────
    private static List<string> ParseList(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? new() :
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string BuildIn(string column, List<string> vals, string prefix,
                                   Dictionary<string, string> parms)
    {
        if (vals.Count == 0) return "";
        var names = vals.Select((v, i) => { var n = $"@{prefix}{i}"; parms[n] = v; return n; }).ToList();
        return $"AND {column} IN ({string.Join(",", names)})";
    }

    private static (DateOnly ini, DateOnly fin) ParseRange(string? fi, string? ff)
    {
        if (!DateOnly.TryParse(fi, out var ini)) ini = DateOnly.FromDateTime(DateTime.Today);
        if (!DateOnly.TryParse(ff, out var fin)) fin = DateOnly.FromDateTime(DateTime.Today);
        if (fin < ini) fin = ini;
        return (ini, fin);
    }

    // ── GET /apigeo/me ──────────────────────────────────────────────────────────
    [HttpGet("me")]
    public IActionResult Me()
    {
        if (!IsAuth) return Unauthorized(new { ok = false });
        return Ok(new
        {
            ok     = true,
            login  = HttpContext.Session.GetString("user_login"),
            nombre = HttpContext.Session.GetString("user_nombre")
        });
    }

    // ── GET /apigeo/filtros ─────────────────────────────────────────────────────
    // Jerarquia de representantes para filtros dependientes Centro→Supervisor→Vendedor→Cargo.
    // El frontend construye los combos dependientes a partir de este arreglo (sin round-trips).
    [HttpGet("filtros")]
    public async Task<IActionResult> Filtros()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        const string Sql = @"
SELECT  r.RepCodigo,
        r.RepNombre                                 AS RepNombre,
        ISNULL(r.RepCargo, '')                      AS RepCargo,
        ISNULL(r.CedCodigo, '')                     AS CedCodigo,
        ISNULL(cd.CedDescripcion, r.CedCodigo)      AS CentroNombre,
        ISNULL(r.RepSupervisor, '')                 AS SupervisorCod,
        ISNULL(sup.RepNombre, r.RepSupervisor)      AS SupervisorNombre,
        ISNULL(ra.RepTipe, '')                      AS Division
FROM    Representantes r WITH (NOLOCK)
LEFT    JOIN Representantes sup          WITH (NOLOCK) ON sup.RepCodigo = r.RepSupervisor
LEFT    JOIN CentrosDistribucion cd      WITH (NOLOCK) ON cd.CedCodigo  = r.CedCodigo
LEFT    JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = r.RepCodigo
WHERE   r.RepEstatus = 1
ORDER   BY CentroNombre, SupervisorNombre, r.RepNombre;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(Sql, cn) { CommandTimeout = 30 };
            var reps = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                reps.Add(new
                {
                    RepCodigo        = rd.IsDBNull(0) ? "" : rd.GetString(0),
                    RepNombre        = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    RepCargo         = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    CedCodigo        = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    CentroNombre     = rd.IsDBNull(4) ? "" : rd.GetString(4),
                    SupervisorCod    = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    SupervisorNombre = rd.IsDBNull(6) ? "" : rd.GetString(6),
                    Division         = rd.IsDBNull(7) ? "" : rd.GetString(7)
                });
            }
            return Ok(new { ok = true, representantes = reps });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }

    // ── GET /apigeo/ubicaciones ─────────────────────────────────────────────────
    // Ultima sincronizacion (GPS) por representante. Params: centros, supervisores, reps, cargos (CSV);
    // horas (int, default 48); modo = 'sync' (frescura, default) | 'ruta' (cercania a clientes del dia).
    [HttpGet("ubicaciones")]
    public async Task<IActionResult> Ubicaciones(
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? reps,
        [FromQuery] string? cargos,
        [FromQuery] int?    horas,
        [FromQuery] string? modo)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var ventana = horas is > 0 and <= 720 ? horas.Value : 48;
        var esRuta  = string.Equals(modo, "ruta", StringComparison.OrdinalIgnoreCase);

        var lCentros = ParseList(centros);
        var lSups    = ParseList(supervisores);
        var lReps    = ParseList(reps);
        var lCargos  = ParseList(cargos);

        var parms    = new Dictionary<string, string>();
        var wCentros = BuildIn("r.CedCodigo",     lCentros, "c",  parms);
        var wSups    = BuildIn("r.RepSupervisor", lSups,    "s",  parms);
        var wReps    = BuildIn("r.RepCodigo",     lReps,    "rp", parms);
        var wCargos  = BuildIn("r.RepCargo",      lCargos,  "cg", parms);

        // En modo 'ruta' se calcula, por cada rep, el cliente de ruta de HOY mas cercano a su
        // ultima posicion (geography::STDistance en metros) via OUTER APPLY. El color se decide
        // en el frontend segun los umbrales configurables.
        var distSel  = esRuta
            ? "nc.CliID AS NearCliID, ISNULL(nc.CliNombre,'') AS NearCliNombre, nc.dist AS MinDistM"
            : "CAST(NULL AS int) AS NearCliID, CAST('' AS nvarchar(120)) AS NearCliNombre, CAST(NULL AS float) AS MinDistM";
        var distApply = esRuta ? @"
OUTER APPLY (
    SELECT TOP 1 c.CliID,
           ISNULL(c.CliNombreComercial, c.CliNombre) AS CliNombre,
           geography::Point(CAST(u.RssLatitud AS float), CAST(u.RssLongitud AS float), 4326)
               .STDistance(geography::Point(CAST(c.CliLatitud AS float), CAST(c.CliLongitud AS float), 4326)) AS dist
    FROM   RutaVisitasFechaHistoricos ru WITH (NOLOCK)
    INNER  JOIN Clientes c WITH (NOLOCK) ON c.CliID = ru.CliID
    WHERE  ru.RepCodigo = u.RepCodigo
      AND  CAST(ru.RutFecha AS DATE) = CAST(GETDATE() AS DATE)
      AND  c.CliLatitud IS NOT NULL AND c.CliLatitud <> 0
    ORDER  BY dist
) nc" : "";

        var sql = $@"
;WITH Ult AS (
    SELECT v.UsuInicioSesion AS RepCodigo, v.RssLatitud, v.RssLongitud, v.RssFechaFin,
           ROW_NUMBER() OVER (PARTITION BY v.UsuInicioSesion ORDER BY v.RssFechaFin DESC) rn
    FROM   VwReplicacionesSuscriptoresSincronizaciones v WITH (NOLOCK)
    WHERE  v.RssLatitud IS NOT NULL AND v.RssLatitud <> 0
      AND  v.RssFechaFin >= DATEADD(HOUR, -@ventana, GETDATE())
)
SELECT  r.RepCodigo,
        r.RepNombre,
        ISNULL(r.RepCargo, '')                  AS RepCargo,
        ISNULL(sup.RepNombre, r.RepSupervisor)  AS SupervisorNombre,
        ISNULL(r.RepSupervisor, '')             AS SupervisorCod,
        ISNULL(r.CedCodigo, '')                 AS CedCodigo,
        ISNULL(cd.CedDescripcion, r.CedCodigo)  AS CentroNombre,
        ISNULL(r.RepColor, '')                  AS RepColor,
        u.RssLatitud, u.RssLongitud, u.RssFechaFin,
        {distSel}
FROM    Ult u
INNER   JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = u.RepCodigo AND r.RepEstatus = 1
LEFT    JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo = r.RepSupervisor
LEFT    JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo = r.CedCodigo
{distApply}
WHERE   u.rn = 1
{wCentros}
{wSups}
{wReps}
{wCargos}
ORDER   BY u.RssFechaFin DESC;";

        try
        {
            var now = DateTime.Now;
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 45 };
            cmd.Parameters.AddWithValue("@ventana", ventana);
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);

            var rows = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                double? minDist = rd.IsDBNull(13) ? null : Convert.ToDouble(rd.GetValue(13));
                rows.Add(new
                {
                    RepCodigo        = rd.IsDBNull(0) ? "" : rd.GetString(0),
                    RepNombre        = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    RepCargo         = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    SupervisorNombre = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    SupervisorCod    = rd.IsDBNull(4) ? "" : rd.GetString(4),
                    CedCodigo        = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    CentroNombre     = rd.IsDBNull(6) ? "" : rd.GetString(6),
                    RepColor         = rd.IsDBNull(7) ? "" : rd.GetString(7),
                    Latitud          = rd.GetDouble(8),
                    Longitud         = rd.GetDouble(9),
                    Sync             = rd.GetDateTime(10).ToString("yyyy-MM-ddTHH:mm:ss"),
                    NearCliID        = rd.IsDBNull(11) ? (int?)null : rd.GetInt32(11),
                    NearCliNombre    = rd.IsDBNull(12) ? "" : rd.GetString(12),
                    MinDistM         = minDist.HasValue ? (int?)Math.Round(minDist.Value) : null
                });
            }
            return Ok(new
            {
                ok = true, modo = esRuta ? "ruta" : "sync",
                ventanaHoras = ventana,
                servidor = now.ToString("yyyy-MM-ddTHH:mm:ss"),
                reps = rows
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }

    // ── GET /apigeo/catalogo ────────────────────────────────────────────────────
    // Jerarquia Suplidor(Categoria3) → Categoria(Lineas) desde tablas maestras.
    // El frontend arma los combos dependientes; productos se cargan aparte.
    [HttpGet("catalogo")]
    public async Task<IActionResult> Catalogo()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            var suplidores = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT DISTINCT c3.Cat3Descripcion AS nombre
                FROM   Productos p WITH (NOLOCK)
                INNER  JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = p.Cat3ID
                WHERE  c3.Cat3Descripcion IS NOT NULL
                ORDER  BY nombre", cn))
            {
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    suplidores.Add(new { v = rd.GetString(0), t = rd.GetString(0) });
            }

            var categorias = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT DISTINCT c3.Cat3Descripcion AS sup, l.LinDescripcion AS nombre
                FROM   Productos p WITH (NOLOCK)
                INNER  JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = p.Cat3ID
                INNER  JOIN Lineas     l  WITH (NOLOCK) ON l.LinID   = p.LinID
                WHERE  l.LinDescripcion IS NOT NULL
                ORDER  BY nombre", cn))
            {
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    categorias.Add(new { v = rd.GetString(1), t = rd.GetString(1), sup = rd.GetString(0) });
            }

            // Divisiones (RepresentantesAdicionales.RepTipe)
            var divisiones = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT DISTINCT RepTipe FROM RepresentantesAdicionales WITH (NOLOCK)
                WHERE RepTipe IS NOT NULL AND LTRIM(RTRIM(RepTipe)) <> '' ORDER BY RepTipe", cn))
            {
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var d = rd.GetString(0);
                    divisiones.Add(new { v = d, t = "División " + d });
                }
            }

            return Ok(new { ok = true, suplidores, categorias, divisiones });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apigeo/productos?suplidores=&categorias= ───────────────────────────
    [HttpGet("productos")]
    public async Task<IActionResult> Productos(
        [FromQuery] string? suplidores,
        [FromQuery] string? categorias)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        var lSup = ParseList(suplidores);
        var lCat = ParseList(categorias);

        var parms = new Dictionary<string, string>();
        var wSup  = BuildIn("c3.Cat3Descripcion", lSup, "sp", parms);
        var wCat  = BuildIn("l.LinDescripcion",   lCat, "ct", parms);

        var sql = $@"
SELECT TOP 500 p.ProID, p.ProDescripcion
FROM   Productos p WITH (NOLOCK)
INNER  JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = p.Cat3ID
INNER  JOIN Lineas     l  WITH (NOLOCK) ON l.LinID   = p.LinID
WHERE  p.ProDescripcion IS NOT NULL
{wSup}
{wCat}
ORDER  BY p.ProDescripcion;";
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
            var prods = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                prods.Add(new { v = rd.GetInt32(0).ToString(), t = rd.IsDBNull(1) ? "" : rd.GetString(1) });
            return Ok(new { ok = true, productos = prods });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apigeo/clientes-ventas ─────────────────────────────────────────────
    // Efectividad: Universo = clientes en RutaVisitasFechaHistoricos de los reps filtrados (rango).
    // ConPedido = el cliente tiene pedido en VWPedidos (mismos reps, rango) de la seleccion
    // suplidor/categoria/producto (si se filtra, via VWPedidosDetalle). Verde=con pedido, Rojo=sin pedido.
    // Se usa VWPedidos (no VwPedidosResumenMesActual, que es ~150x mas lenta) por rendimiento.
    [HttpGet("clientes-ventas")]
    public async Task<IActionResult> ClientesVentas(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? reps,
        [FromQuery] string? suplidores,
        [FromQuery] string? categorias,
        [FromQuery] string? productos,
        [FromQuery] string? cargos,
        [FromQuery] string? divisiones)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var fiStr = fi.ToString("yyyy-MM-dd");
        var ffStr = ff.ToString("yyyy-MM-dd");

        var lSup  = ParseList(suplidores);
        var lCat  = ParseList(categorias);
        var lProd = ParseList(productos);
        var lDiv  = ParseList(divisiones);
        var needDetalle = lSup.Count > 0 || lCat.Count > 0 || lProd.Count > 0;

        // Filtro de reps (join directo a Representantes; division via RepresentantesAdicionales).
        var repParms = new Dictionary<string, string>();
        var wCentros = BuildIn("r.CedCodigo",     ParseList(centros),      "c",  repParms);
        var wSups    = BuildIn("r.RepSupervisor", ParseList(supervisores), "s",  repParms);
        var wReps    = BuildIn("r.RepCodigo",     ParseList(reps),         "rp", repParms);
        var wCargo   = BuildIn("r.RepCargo",      ParseList(cargos),       "cg", repParms);
        var wDiv     = BuildIn("ra.RepTipe",      lDiv,                    "dv", repParms);
        var divJoin  = lDiv.Count > 0 ? "INNER JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = r.RepCodigo" : "";
        var repWhere = $"r.RepEstatus = 1 AND ISNULL(r.RepCargo,'') <> 'Supervisor' {wCentros} {wSups} {wReps} {wCargo} {wDiv}";

        var pedParms = new Dictionary<string, string>();
        var wSup  = BuildIn("c3.Cat3Descripcion", lSup,  "sp", pedParms);
        var wCat  = BuildIn("l.LinDescripcion",   lCat,  "ct", pedParms);
        var wProd = BuildIn("pd.ProID",           lProd, "pr", pedParms);
        var pedidosJoins = needDetalle ? @"
    INNER  JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia = p.PedSecuencia AND pd.RepCodigo = p.RepCodigo
    INNER  JOIN Productos  pr WITH (NOLOCK) ON pr.ProID  = pd.ProID
    INNER  JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = pr.Cat3ID
    INNER  JOIN Lineas     l  WITH (NOLOCK) ON l.LinID   = pr.LinID" : "";

        // Consultas separadas (una fuente cada una) para que el optimizador use el mejor plan.
        // Combinarlas con VWvisitas/VWMensajes en un solo SELECT hace que se materialicen (timeout).
        // Universo = clientes de ruta del dia UNION clientes con pedido (para incluir pedidos FUERA de ruta).
        // EnRuta=1 estaba planificado; ConPedido=1 hizo pedido. Un cliente con pedido y EnRuta=0 = "otro dia".
        // IMPORTANTE: el analisis es por (RepCodigo, CliID). Un mismo cliente programado para 2
        // vendedores distintos entra 2 veces (una visita por cada vendedor), separadas.
        var sqlUniverso = $@"
;WITH Ruta AS (
    SELECT DISTINCT ru.RepCodigo, ru.CliID
    FROM   RutaVisitasFechaHistoricos ru WITH (NOLOCK)
    INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = ru.RepCodigo
    {divJoin}
    WHERE  {repWhere}
      AND  CAST(ru.RutFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
),
Ped AS (
    SELECT DISTINCT p.RepCodigo, p.CliID
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = p.RepCodigo
    {divJoin}
    {pedidosJoins}
    WHERE  {repWhere}
      AND  CAST(p.PedFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
      {wSup} {wCat} {wProd}
)
SELECT u.RepCodigo, c.CliID,
       ISNULL(c.CliNombreComercial, c.CliNombre) AS Nombre,
       c.CliLatitud, c.CliLongitud,
       CASE WHEN ru.RepCodigo IS NOT NULL THEN 1 ELSE 0 END AS EnRuta,
       CASE WHEN pe.RepCodigo IS NOT NULL THEN 1 ELSE 0 END AS ConPedido
FROM   ( SELECT RepCodigo, CliID FROM Ruta UNION SELECT RepCodigo, CliID FROM Ped ) u
INNER  JOIN Clientes c WITH (NOLOCK) ON c.CliID = u.CliID
LEFT   JOIN Ruta ru ON ru.RepCodigo = u.RepCodigo AND ru.CliID = u.CliID
LEFT   JOIN Ped  pe ON pe.RepCodigo = u.RepCodigo AND pe.CliID = u.CliID;";

        // Visitas por (rep, cliente): visitado + virtual. '2'=Virtual (tele-venta), '1'=Presencial.
        var sqlVis = $@"
SELECT v.RepCodigo, v.CliID,
       MAX(CASE WHEN v.VisTipoVisita = '2' THEN 1 ELSE 0 END) AS Virtual
FROM   Visitas v WITH (NOLOCK)
INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = v.RepCodigo
{divJoin}
WHERE  {repWhere}
  AND  CAST(v.VisFechaEntrada AS DATE) BETWEEN @fechaIni AND @fechaFin
GROUP  BY v.RepCodigo, v.CliID;";

        // Comentario / motivos de no-venta (VWMensajes TraID=13) por (rep, cliente).
        var sqlComent = $@"
SELECT m.RepCodigo, m.CliID, STRING_AGG(m.MenDescripcion, ', ') AS Comentario
FROM   VWMensajes m WITH (NOLOCK)
INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = m.RepCodigo
{divJoin}
WHERE  m.TraID = 13
  AND  {repWhere}
  AND  CAST(m.MenFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
GROUP  BY m.RepCodigo, m.CliID;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            void AddDates(SqlCommand c) { c.Parameters.AddWithValue("@fechaIni", fiStr); c.Parameters.AddWithValue("@fechaFin", ffStr); }
            void AddParms(SqlCommand c, Dictionary<string, string> d) { foreach (var (k, v) in d) c.Parameters.AddWithValue(k, v); }

            // Clave compuesta rep|cliente para separar a un mismo cliente en 2 vendedores.
            var vis = new Dictionary<string, bool>();
            await using (var cmd = new SqlCommand(sqlVis, cn) { CommandTimeout = 60 })
            {
                AddDates(cmd); AddParms(cmd, repParms);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    if (!rd.IsDBNull(0) && !rd.IsDBNull(1)) vis[rd.GetString(0) + "|" + rd.GetInt32(1)] = Convert.ToInt32(rd.GetValue(2)) == 1;
            }

            var comentarios = new Dictionary<string, string>();
            await using (var cmd = new SqlCommand(sqlComent, cn) { CommandTimeout = 60 })
            {
                AddDates(cmd); AddParms(cmd, repParms);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    if (rd.IsDBNull(0) || rd.IsDBNull(1) || rd.IsDBNull(2)) continue;
                    var com = rd.GetString(2).Trim();
                    if (com.Length > 300) com = com[..300];
                    if (!string.IsNullOrEmpty(com)) comentarios[rd.GetString(0) + "|" + rd.GetInt32(1)] = com;
                }
            }

            // Universo (ruta UNION pedidos) por (rep, cliente) + merge en memoria
            var clientes = new List<object>();
            int total = 0, sinGps = 0, rutaTotal = 0;
            int cVerde = 0, cAzul = 0, cOtros = 0, cAmar = 0, cRojo = 0, cGris = 0, visVirtualSinPed = 0;
            await using (var cmd = new SqlCommand(sqlUniverso, cn) { CommandTimeout = 60 })
            {
                AddDates(cmd); AddParms(cmd, repParms); AddParms(cmd, pedParms);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    total++;
                    var repCod    = rd.IsDBNull(0) ? "" : rd.GetString(0);
                    var cliId     = rd.GetInt32(1);
                    var enRuta    = rd.GetInt32(5) == 1;
                    var conPedido = rd.GetInt32(6) == 1;
                    var key       = repCod + "|" + cliId;
                    var visitado  = vis.TryGetValue(key, out var esVirtual);
                    var virtual_  = visitado && esVirtual;
                    var coment    = visitado && comentarios.TryGetValue(key, out var cm) ? cm : null;

                    if (enRuta) rutaTotal++;
                    // Estado (conPedido tiene prioridad; fuera de ruta con pedido = "otro dia")
                    if (conPedido)      { if (!enRuta) cOtros++; else if (virtual_) cAzul++; else cVerde++; }
                    else if (enRuta)    { if (!visitado) cGris++; else if (coment != null) cAmar++; else cRojo++; }
                    if (enRuta && visitado && virtual_ && !conPedido) visVirtualSinPed++;

                    bool hasGps = !rd.IsDBNull(3) && !rd.IsDBNull(4) && Convert.ToDouble(rd.GetValue(3)) != 0;
                    if (!hasGps) { sinGps++; continue; }

                    clientes.Add(new
                    {
                        CliID      = cliId,
                        Nombre     = rd.IsDBNull(2) ? "" : rd.GetString(2),
                        Latitud    = Convert.ToDouble(rd.GetValue(3)),
                        Longitud   = Convert.ToDouble(rd.GetValue(4)),
                        RepCodigo  = repCod,
                        EnRuta     = enRuta,
                        ConPedido  = conPedido,
                        Visitado   = visitado,
                        Virtual    = virtual_,
                        Comentario = coment
                    });
                }
            }

            var totalPedidos = cVerde + cAzul + cOtros;
            return Ok(new
            {
                ok = true, fechaIni = fiStr, fechaFin = ffStr,
                total, rutaTotal, sinGps,
                pedidos = new { delDia = cVerde, virtuales = cAzul, otrosDias = cOtros, total = totalPedidos },
                sinPedido = new { comentario = cAmar, rojo = cRojo, noVisitado = cGris },
                pedidosVirtuales = cAzul, virtualSinPedido = visVirtualSinPed,
                clientes
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apigeo/visitas-dia ────────────────────────────────────────────────
    // Pedido del usuario 2026-10-17 (P4): para una FECHA y los reps filtrados (centro/supervisor/vendedor):
    //   programados = clientes de ruta de ese día (RutaVisitasFechaHistoricos) con su GPS → «casitas»;
    //   visitas     = visitas realizadas (Visitas) con hora, GPS de la visita, distancia al cliente, tipo (1 presencial / 2 virtual),
    //                 si fue efectiva (tiene pedido enlazado por VisSecuencia en VWPedidos) y el monto prevendido
    //                 (Σ cantidad × precio de VWPedidosDetalle, bruto sin ITBIS, igual que los calendarios del Cierre Diario).
    // «Fuera de su lugar» = VisDistanciaMetros > pdvMetros (por defecto 200 m, el mismo criterio de Fuera de PDV del AMCO-GT);
    // el front decide el color de cada casita según la hora elegida en la línea de tiempo.
    [HttpGet("visitas-dia")]
    public async Task<IActionResult> VisitasDia(
        [FromQuery] string? fecha,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? reps,
        [FromQuery] string? divisiones,
        [FromQuery] int pdvMetros = 200)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        var (fi, _) = ParseRange(fecha, fecha);
        var f = fi.ToString("yyyy-MM-dd");
        if (pdvMetros < 10 || pdvMetros > 5000) pdvMetros = 200;

        var lDiv = ParseList(divisiones);
        var repParms = new Dictionary<string, string>();
        var wCentros = BuildIn("r.CedCodigo",     ParseList(centros),      "c",  repParms);
        var wSups    = BuildIn("r.RepSupervisor", ParseList(supervisores), "s",  repParms);
        var wReps    = BuildIn("r.RepCodigo",     ParseList(reps),         "rp", repParms);
        var wDiv     = BuildIn("ra.RepTipe",      lDiv,                    "dv", repParms);
        var divJoin  = lDiv.Count > 0 ? "INNER JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = r.RepCodigo" : "";
        var repWhere = $"ISNULL(r.RepCargo,'') <> 'Supervisor' {wCentros} {wSups} {wReps} {wDiv}";

        var sqlProg = $@"
SELECT ru.RepCodigo, ru.CliID, MIN(ru.RutPosicion) AS Pos, MAX(r.RepNombre) AS RepNombre,
       MAX(ISNULL(c.CliNombreComercial, c.CliNombre)) AS Nombre, MAX(c.CliLatitud) AS Lat, MAX(c.CliLongitud) AS Lon
FROM   RutaVisitasFechaHistoricos ru WITH (NOLOCK)
INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = ru.RepCodigo
{divJoin}
LEFT   JOIN Clientes c WITH (NOLOCK) ON c.CliID = ru.CliID
WHERE  {repWhere} AND CAST(ru.RutFecha AS DATE) = @f
GROUP  BY ru.RepCodigo, ru.CliID;";

        var sqlVis = $@"
;WITH P AS (
    SELECT p.RepCodigo, p.VisSecuencia, COUNT(DISTINCT p.PedSecuencia) AS Ped,
           SUM(ISNULL(d.PedCantidad,0) * ISNULL(d.PedPrecio,0)) AS Monto
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = p.RepCodigo
    {divJoin}
    LEFT   JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia = p.PedSecuencia AND d.RepCodigo = p.RepCodigo
    WHERE  {repWhere} AND CAST(p.PedFecha AS DATE) = @f AND p.VisSecuencia IS NOT NULL
    GROUP  BY p.RepCodigo, p.VisSecuencia
)
SELECT v.RepCodigo, v.VisSecuencia, v.CliID, v.VisFechaEntrada, v.VisFechaSalida, v.VisTipoVisita,
       v.VisLatitud, v.VisLongitud, v.VisDistanciaMetros, v.VisIndicadorFueraRuta,
       ISNULL(c.CliNombreComercial, c.CliNombre) AS Nombre, c.CliLatitud, c.CliLongitud,
       ISNULL(pp.Ped,0) AS Ped, ISNULL(pp.Monto,0) AS Monto, r.RepNombre
FROM   Visitas v WITH (NOLOCK)
INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = v.RepCodigo
{divJoin}
LEFT   JOIN Clientes c WITH (NOLOCK) ON c.CliID = v.CliID
LEFT   JOIN P pp ON pp.RepCodigo = v.RepCodigo AND pp.VisSecuencia = v.VisSecuencia
WHERE  {repWhere} AND CAST(v.VisFechaEntrada AS DATE) = @f
ORDER  BY v.VisFechaEntrada;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            void Add(SqlCommand c) { c.Parameters.AddWithValue("@f", f); foreach (var (k, v) in repParms) c.Parameters.AddWithValue(k, v); }
            static double? Gps(object o) { if (o == DBNull.Value) return null; var d = Convert.ToDouble(o); return d == 0 ? (double?)null : d; }

            var programados = new List<object>(); int sinGps = 0;
            await using (var cmd = new SqlCommand(sqlProg, cn) { CommandTimeout = 90 })
            {
                Add(cmd);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var lat = Gps(rd.GetValue(5)); var lon = Gps(rd.GetValue(6));
                    if (lat == null || lon == null) sinGps++;
                    programados.Add(new
                    {
                        rep = rd.GetString(0), cli = rd.GetInt32(1), pos = rd.IsDBNull(2) ? 0 : Convert.ToInt32(rd.GetValue(2)),
                        repNombre = rd.IsDBNull(3) ? "" : rd.GetString(3), nombre = rd.IsDBNull(4) ? "" : rd.GetString(4), lat, lon
                    });
                }
            }

            var visitas = new List<object>(); var repsVistos = new Dictionary<string, string>();
            await using (var cmd = new SqlCommand(sqlVis, cn) { CommandTimeout = 120 })
            {
                Add(cmd);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var ent = rd.GetDateTime(3);
                    double? dist = rd.IsDBNull(8) ? null : Convert.ToDouble(rd.GetValue(8));
                    int ped = Convert.ToInt32(rd.GetValue(13));
                    var rep = rd.GetString(0);
                    repsVistos[rep] = rd.IsDBNull(15) ? rep : rd.GetString(15);
                    visitas.Add(new
                    {
                        rep, visSec = rd.GetInt32(1), cli = rd.GetInt32(2),
                        hora = ent.ToString("yyyy-MM-ddTHH:mm:ss"), minuto = ent.Hour * 60 + ent.Minute + ent.Second / 60.0,
                        salida = rd.IsDBNull(4) ? null : rd.GetDateTime(4).ToString("HH:mm"),
                        tipo = rd.IsDBNull(5) ? "1" : rd.GetString(5).Trim(),
                        lat = Gps(rd.GetValue(6)), lon = Gps(rd.GetValue(7)),
                        dist = dist.HasValue ? Math.Round(dist.Value) : (double?)null,
                        fueraLugar = dist.HasValue && dist.Value > pdvMetros,
                        fueraRutaFlag = !rd.IsDBNull(9) && Convert.ToBoolean(rd.GetValue(9)),
                        nombre = rd.IsDBNull(10) ? "" : rd.GetString(10),
                        cliLat = Gps(rd.GetValue(11)), cliLon = Gps(rd.GetValue(12)),
                        efectiva = ped > 0, pedidos = ped, monto = Math.Round(Convert.ToDouble(rd.GetValue(14)), 2),
                        repNombre = rd.IsDBNull(15) ? "" : rd.GetString(15)
                    });
                }
            }
            return Ok(new { ok = true, fecha = f, pdvMetros, programados, sinGps, visitas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apigeo/fuera-ruta ──────────────────────────────────────────────────
    // Clientes a los que se les vendio en un dia que NO estaban programados en ruta (fuera de ruta).
    // Params: fechaIni, fechaFin, centros, supervisores, reps. Devuelve clientes + top3 reps + top3 supervisores.
    [HttpGet("fuera-ruta")]
    public async Task<IActionResult> FueraRuta(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? reps)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var fiStr = fi.ToString("yyyy-MM-dd");
        var ffStr = ff.ToString("yyyy-MM-dd");

        var parms = new Dictionary<string, string>();
        var wCentros = BuildIn("r.CedCodigo",     ParseList(centros),      "c",  parms);
        var wSups    = BuildIn("r.RepSupervisor", ParseList(supervisores), "s",  parms);
        var wReps    = BuildIn("r.RepCodigo",     ParseList(reps),         "rp", parms);
        var repWhere = $"r.RepEstatus = 1 AND ISNULL(r.RepCargo,'') <> 'Supervisor' {wCentros} {wSups} {wReps}";

        var sql = $@"
;WITH Ped AS (
    SELECT DISTINCT p.RepCodigo, p.CliID, CAST(p.PedFecha AS DATE) AS PedDia
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = p.RepCodigo
    WHERE  {repWhere}
      AND  CAST(p.PedFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
),
Fuera AS (
    SELECT pe.RepCodigo, pe.CliID, pe.PedDia
    FROM   Ped pe
    WHERE  NOT EXISTS (SELECT 1 FROM RutaVisitasFechaHistoricos ru WITH (NOLOCK)
                       WHERE ru.RepCodigo = pe.RepCodigo AND ru.CliID = pe.CliID
                         AND CAST(ru.RutFecha AS DATE) = pe.PedDia)
),
Agg AS (
    SELECT RepCodigo, CliID, COUNT(1) AS Pedidos, MAX(PedDia) AS UltPedDia
    FROM   Fuera GROUP BY RepCodigo, CliID
)
SELECT a.CliID, ISNULL(c.CliNombreComercial, c.CliNombre) AS Nombre,
       c.CliLatitud, c.CliLongitud,
       a.RepCodigo, rep.RepNombre,
       ISNULL(rep.RepSupervisor,'') AS SupCod,
       ISNULL(sup.RepNombre, rep.RepSupervisor) AS SupNom,
       a.Pedidos, a.UltPedDia
FROM   Agg a
INNER  JOIN Clientes c        WITH (NOLOCK) ON c.CliID = a.CliID
INNER  JOIN Representantes rep WITH (NOLOCK) ON rep.RepCodigo = a.RepCodigo
LEFT   JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo = rep.RepSupervisor
ORDER  BY a.Pedidos DESC;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@fechaIni", fiStr);
            cmd.Parameters.AddWithValue("@fechaFin", ffStr);
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);

            var clientes = new List<object>();
            var reps2 = new Dictionary<string, (string Nombre, int Cli, int Ped)>();
            var sups  = new Dictionary<string, (string Nombre, HashSet<string> Reps, int Cli, int Ped)>();
            int totalPed = 0, totalCli = 0, sinGps = 0;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var cliId  = rd.GetInt32(0);
                var nombre = rd.IsDBNull(1) ? "" : rd.GetString(1);
                var repCod = rd.IsDBNull(4) ? "" : rd.GetString(4);
                var repNom = rd.IsDBNull(5) ? "" : rd.GetString(5);
                var supCod = rd.IsDBNull(6) ? "" : rd.GetString(6);
                var supNom = rd.IsDBNull(7) ? "" : rd.GetString(7);
                var pedidos = rd.GetInt32(8);
                var ultPed  = rd.IsDBNull(9) ? (DateTime?)null : rd.GetDateTime(9);

                totalCli++; totalPed += pedidos;

                if (!reps2.TryGetValue(repCod, out var r)) r = (repNom, 0, 0);
                reps2[repCod] = (repNom, r.Cli + 1, r.Ped + pedidos);

                if (!sups.TryGetValue(supCod, out var s)) s = (supNom, new HashSet<string>(), 0, 0);
                s.Reps.Add(repCod);
                sups[supCod] = (supNom, s.Reps, s.Cli + 1, s.Ped + pedidos);

                bool hasGps = !rd.IsDBNull(2) && !rd.IsDBNull(3) && Convert.ToDouble(rd.GetValue(2)) != 0;
                if (!hasGps) { sinGps++; continue; }

                clientes.Add(new
                {
                    CliID     = cliId,
                    Nombre    = nombre,
                    Latitud   = Convert.ToDouble(rd.GetValue(2)),
                    Longitud  = Convert.ToDouble(rd.GetValue(3)),
                    RepCodigo = repCod,
                    RepNombre = repNom,
                    SupNombre = supNom,
                    Pedidos   = pedidos,
                    UltPedido = ultPed?.ToString("yyyy-MM-dd")
                });
            }

            var topReps = reps2.OrderByDescending(x => x.Value.Ped).Take(3)
                .Select(x => new { RepCodigo = x.Key, x.Value.Nombre, clientes = x.Value.Cli, pedidos = x.Value.Ped });
            var topSups = sups.OrderByDescending(x => x.Value.Ped).Take(3)
                .Select(x => new { SupCodigo = x.Key, x.Value.Nombre, vendedores = x.Value.Reps.Count, clientes = x.Value.Cli, pedidos = x.Value.Ped });

            return Ok(new
            {
                ok = true, fechaIni = fiStr, fechaFin = ffStr,
                totalPedidos = totalPed, totalClientes = totalCli, totalReps = reps2.Count, sinGps,
                topReps, topSups, clientes
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apigeo/fuera-ruta-vecinos ──────────────────────────────────────────
    // Los 10 clientes del MISMO vendedor mas cercanos al cliente vendido fuera de ruta,
    // con su ultimo dia programado de visita y si fue visitado presencial en el rango.
    [HttpGet("fuera-ruta-vecinos")]
    public async Task<IActionResult> FueraRutaVecinos(
        [FromQuery] string? rep,
        [FromQuery] int? cli,
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        if (string.IsNullOrWhiteSpace(rep) || cli is null)
            return Ok(new { ok = false, error = "Faltan parametros" });

        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var fiStr = fi.ToString("yyyy-MM-dd");
        var ffStr = ff.ToString("yyyy-MM-dd");

        // Primero TOP 10 por distancia (rapido); luego dia programado + visita solo para esos 10.
        const string Sql = @"
DECLARE @lat float, @lng float;
SELECT @lat = CliLatitud, @lng = CliLongitud FROM Clientes WITH (NOLOCK) WHERE CliID = @cli;
;WITH Near AS (
    SELECT TOP 10 c2.CliID, ISNULL(c2.CliNombreComercial, c2.CliNombre) AS Nombre,
           c2.CliLatitud, c2.CliLongitud, ISNULL(c2.CliCodigo,'') AS CliCodigo,
           CAST(geography::Point(@lat, @lng, 4326)
                .STDistance(geography::Point(CAST(c2.CliLatitud AS float), CAST(c2.CliLongitud AS float), 4326)) AS int) AS Metros
    FROM   Clientes c2 WITH (NOLOCK)
    WHERE  c2.RepCodigo = @rep AND c2.CliID <> @cli AND ISNULL(c2.CliEstatus,1) = 1
      AND  c2.CliLatitud IS NOT NULL AND c2.CliLatitud <> 0
    ORDER  BY geography::Point(@lat, @lng, 4326)
              .STDistance(geography::Point(CAST(c2.CliLatitud AS float), CAST(c2.CliLongitud AS float), 4326))
)
SELECT n.CliID, n.Nombre, n.CliCodigo, n.CliLatitud, n.CliLongitud, n.Metros,
       sched.UltDiaVisita,
       CASE WHEN vp.CliID IS NOT NULL THEN 1 ELSE 0 END AS VisitadoPresencial
FROM   Near n
OUTER  APPLY (SELECT MAX(CAST(ru.RutFecha AS DATE)) AS UltDiaVisita
              FROM RutaVisitasFechaHistoricos ru WITH (NOLOCK)
              WHERE ru.RepCodigo = @rep AND ru.CliID = n.CliID AND CAST(ru.RutFecha AS DATE) <= @ff) sched
OUTER  APPLY (SELECT TOP 1 vi.CliID FROM Visitas vi WITH (NOLOCK)
              WHERE vi.RepCodigo = @rep AND vi.CliID = n.CliID AND vi.VisTipoVisita = '1'
                AND CAST(vi.VisFechaEntrada AS DATE) BETWEEN @fi AND @ff) vp
ORDER  BY n.Metros;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(Sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@rep", rep.Trim());
            cmd.Parameters.AddWithValue("@cli", cli.Value);
            cmd.Parameters.AddWithValue("@fi", fiStr);
            cmd.Parameters.AddWithValue("@ff", ffStr);

            var vecinos = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                vecinos.Add(new
                {
                    CliID      = rd.GetInt32(0),
                    Nombre     = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    CliCodigo  = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    Latitud    = Convert.ToDouble(rd.GetValue(3)),
                    Longitud   = Convert.ToDouble(rd.GetValue(4)),
                    Metros     = rd.GetInt32(5),
                    DiaVisita  = rd.IsDBNull(6) ? null : rd.GetDateTime(6).ToString("yyyy-MM-dd"),
                    VisitadoPresencial = rd.GetInt32(7) == 1
                });
            }
            return Ok(new { ok = true, vecinos });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apigeo/oportunidades ───────────────────────────────────────────────
    // Clientes que compran de OTROS suplidores pero NO del suplidor/categoria filtrado (oportunidad perdida).
    // Params: fechaIni, fechaFin, centros, supervisores, reps, suplidores, categorias, divisiones (CSV);
    //         incluirCompran (bool) para tambien mostrar los que si compran.
    [HttpGet("oportunidades")]
    public async Task<IActionResult> Oportunidades(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? reps,
        [FromQuery] string? suplidores,
        [FromQuery] string? categorias,
        [FromQuery] string? divisiones,
        [FromQuery] string? tamanos,
        [FromQuery] bool? incluirCompran)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var fiStr = fi.ToString("yyyy-MM-dd");
        var ffStr = ff.ToString("yyyy-MM-dd");

        var lSup = ParseList(suplidores);
        var lCat = ParseList(categorias);
        var lDiv = ParseList(divisiones);
        if (lSup.Count == 0 && lCat.Count == 0)
            return Ok(new { ok = true, sinSeleccion = true, clientes = Array.Empty<object>() });

        var parms = new Dictionary<string, string>();
        var wCentros = BuildIn("r.CedCodigo",        ParseList(centros),      "c",  parms);
        var wSups    = BuildIn("r.RepSupervisor",    ParseList(supervisores), "s",  parms);
        var wReps    = BuildIn("r.RepCodigo",        ParseList(reps),         "rp", parms);
        var wDiv     = BuildIn("ra.RepTipe",         lDiv,                    "dv", parms);
        var wSup     = BuildIn("c3.Cat3Descripcion", lSup,                    "sp", parms);
        var wCat     = BuildIn("l.LinDescripcion",   lCat,                    "ct", parms);
        var wTam     = BuildIn("c.ClaID",            ParseList(tamanos),      "tm", parms);

        var divJoin  = lDiv.Count > 0 ? "INNER JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = r.RepCodigo" : "";
        var repWhere = $"r.RepEstatus = 1 AND ISNULL(r.RepCargo,'') <> 'Supervisor' {wCentros} {wSups} {wReps} {wDiv}";

        var condParts = new List<string>();
        if (wSup != "") condParts.Add(wSup[4..]);
        if (wCat != "") condParts.Add(wCat[4..]);
        var supCond = string.Join(" AND ", condParts);

        // "Vendido" = clientes a quienes se les tomo PEDIDO de la seleccion en el rango (VWPedidos).
        var vendidoSql = $@"
    SELECT p.CliID,
           SUM(ISNULL(pd.PedCantidad,0) * ISNULL(pd.PedPrecio,0)) AS Val,
           SUM(ISNULL(pd.PedCantidad,0)) AS Uni
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia = p.PedSecuencia AND pd.RepCodigo = p.RepCodigo
    INNER  JOIN Productos  pr WITH (NOLOCK) ON pr.ProID  = pd.ProID
    INNER  JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = pr.Cat3ID
    INNER  JOIN Lineas     l  WITH (NOLOCK) ON l.LinID   = pr.LinID
    WHERE  {supCond} AND CAST(p.PedFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
    GROUP  BY p.CliID";

        // Universo = clientes asignados a rutavisitas (de los reps filtrados) + tamaño + flag vendido
        var sqlMain = $@"
;WITH RepsF AS (SELECT r.RepCodigo FROM Representantes r WITH (NOLOCK) {divJoin} WHERE {repWhere}),
Ruta AS (
    SELECT ru.CliID, MIN(ru.RepCodigo) AS RepCodigo
    FROM   RutaVisitasFechaHistoricos ru WITH (NOLOCK)
    INNER  JOIN RepsF rf ON rf.RepCodigo = ru.RepCodigo
    WHERE  CAST(ru.RutFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
    GROUP  BY ru.CliID
),
Vendido AS ({vendidoSql}
),
UltCompra AS (
    SELECT p.CliID, MAX(p.PedFecha) AS Ult
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN Ruta rr ON rr.CliID = p.CliID
    WHERE  p.PedFecha >= DATEADD(YEAR,-1,GETDATE())
    GROUP  BY p.CliID
)
SELECT c.CliID, ISNULL(c.CliNombreComercial, c.CliNombre) AS Nombre, c.CliLatitud, c.CliLongitud,
       ISNULL(ru.RepCodigo,'') AS RepCodigo, ISNULL(c.ClaID,0) AS ClaID,
       CASE WHEN vd.CliID IS NOT NULL THEN 1 ELSE 0 END AS Vendido,
       CASE WHEN uc.Ult IS NULL THEN NULL ELSE DATEDIFF(DAY, uc.Ult, GETDATE()) END AS DiasUltCompra
FROM   Ruta ru
INNER  JOIN Clientes c WITH (NOLOCK) ON c.CliID = ru.CliID
LEFT   JOIN Vendido vd ON vd.CliID = ru.CliID
LEFT   JOIN UltCompra uc ON uc.CliID = ru.CliID
WHERE  1=1 {wTam};";

        // DropSize por tamaño (promedio Val/Uni por comprador de la seleccion)
        var sqlDrop = $@"
;WITH Vendido AS ({vendidoSql}
)
SELECT ISNULL(c.ClaID,0) AS ClaID, COUNT(*) AS Buyers, AVG(vd.Val) AS DropVal, AVG(vd.Uni) AS DropUni
FROM   Vendido vd INNER JOIN Clientes c WITH (NOLOCK) ON c.CliID = vd.CliID
WHERE  1=1 {wTam}
GROUP  BY ISNULL(c.ClaID,0);";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            var incluir = incluirCompran == true;

            // 1) DropSize por tamaño + global (promedio por comprador de la seleccion)
            var dropSize = new Dictionary<int, (double val, double uni, int buyers)>();
            double sumVal = 0, sumUni = 0; int totBuyers = 0;
            await using (var cmd = new SqlCommand(sqlDrop, cn) { CommandTimeout = 120 })
            {
                cmd.Parameters.AddWithValue("@fechaIni", fiStr);
                cmd.Parameters.AddWithValue("@fechaFin", ffStr);
                foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    int cla = Convert.ToInt32(rd.GetValue(0)); int b = rd.GetInt32(1);
                    double dv = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
                    double du = rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3));
                    dropSize[cla] = (dv, du, b); totBuyers += b; sumVal += dv * b; sumUni += du * b;
                }
            }
            double dropVal = totBuyers > 0 ? sumVal / totBuyers : 0;
            double dropUni = totBuyers > 0 ? sumUni / totBuyers : 0;

            // 2) Universo (rutavisitas) -> perdidos = no vendidos
            var clientes = new List<object>();
            var perdSize = new Dictionary<int, int>();
            var perdRep = new Dictionary<string, int>();
            int totalRuta = 0, perdidos = 0, sinGps = 0;
            await using (var cmd = new SqlCommand(sqlMain, cn) { CommandTimeout = 120 })
            {
                cmd.Parameters.AddWithValue("@fechaIni", fiStr);
                cmd.Parameters.AddWithValue("@fechaFin", ffStr);
                foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    totalRuta++;
                    bool vendido = rd.GetInt32(6) == 1;
                    var repCod = rd.IsDBNull(4) ? "" : rd.GetString(4);
                    int claId = Convert.ToInt32(rd.GetValue(5));
                    if (!vendido)
                    {
                        perdidos++;
                        perdSize[claId] = perdSize.GetValueOrDefault(claId) + 1;
                        perdRep[repCod] = perdRep.GetValueOrDefault(repCod) + 1;
                    }
                    if (vendido && !incluir) continue;
                    bool hasGps = !rd.IsDBNull(2) && !rd.IsDBNull(3) && Convert.ToDouble(rd.GetValue(2)) != 0;
                    if (!hasGps) { if (!vendido) sinGps++; continue; }
                    int? diasUlt = rd.IsDBNull(7) ? (int?)null : Convert.ToInt32(rd.GetValue(7));
                    clientes.Add(new
                    {
                        CliID = rd.GetInt32(0), Nombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                        Latitud = Convert.ToDouble(rd.GetValue(2)), Longitud = Convert.ToDouble(rd.GetValue(3)),
                        RepCodigo = repCod, ClaID = claId, Compra = vendido, DiasUltCompra = diasUlt
                    });
                }
            }

            double valorPerdido = perdidos * dropVal;
            // Top 3 tamaños con mas perdida (pesos y unidades) usando el dropSize de su propio tamaño
            var porTamano = perdSize.Select(kv =>
            {
                var d = dropSize.TryGetValue(kv.Key, out var x) ? x : (val: dropVal, uni: dropUni, buyers: 0);
                return new { claId = kv.Key, perdidos = kv.Value, valorPerdido = Math.Round(kv.Value * d.val), uniPerdidas = Math.Round(kv.Value * d.uni) };
            }).OrderByDescending(x => x.valorPerdido).Take(3).ToList();
            // Top 3 vendedores con mas clientes y monto perdido
            var topVendedores = perdRep.OrderByDescending(x => x.Value).Take(3)
                .Select(x => new { RepCodigo = x.Key, clientes = x.Value, monto = Math.Round(x.Value * dropVal) }).ToList();

            return Ok(new
            {
                ok = true, fechaIni = fiStr, fechaFin = ffStr,
                totalRuta, perdidos, sinGps,
                pctPerdidos = totalRuta > 0 ? Math.Round(perdidos * 100.0 / totalRuta, 1) : 0,
                dropSizeVal = Math.Round(dropVal), dropSizeUni = Math.Round(dropUni, 1),
                valorPerdido = Math.Round(valorPerdido),
                porTamano, topVendedores, clientes
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apigeo/recuperados ─────────────────────────────────────────────────
    // Clientes recuperados: de los que NO se les vendio el suplidor en el RANGO ACTIVO,
    // cuales SI se les vendio en el RANGO EVALUACION. Fuente = ventas facturadas (VwVentasResumen2026).
    [HttpGet("recuperados")]
    public async Task<IActionResult> Recuperados(
        [FromQuery] string? activoIni,
        [FromQuery] string? activoFin,
        [FromQuery] string? evalIni,
        [FromQuery] string? evalFin,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? reps,
        [FromQuery] string? suplidores,
        [FromQuery] string? categorias,
        [FromQuery] string? divisiones,
        [FromQuery] string? fuente)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var (ai, af) = ParseRange(activoIni, activoFin);
        var (ei, ef) = ParseRange(evalIni, evalFin);
        var lSup = ParseList(suplidores);
        var lCat = ParseList(categorias);
        var lDiv = ParseList(divisiones);
        if (lSup.Count == 0 && lCat.Count == 0)
            return Ok(new { ok = true, sinSeleccion = true, clientes = Array.Empty<object>() });

        var esPedidos = string.Equals(fuente, "pedidos", StringComparison.OrdinalIgnoreCase);

        var parms = new Dictionary<string, string>();
        var wCentros = BuildIn("r.CedCodigo",     ParseList(centros),      "c",  parms);
        var wSups    = BuildIn("r.RepSupervisor", ParseList(supervisores), "s",  parms);
        var wReps    = BuildIn("r.RepCodigo",     ParseList(reps),         "rp", parms);
        var wDiv     = BuildIn("ra.RepTipe",      lDiv,                    "dv", parms);
        // Columnas de suplidor/categoria segun la fuente
        var wSup     = BuildIn(esPedidos ? "c3.Cat3Descripcion" : "v.SUPLIDOR",  lSup, "sp", parms);
        var wCat     = BuildIn(esPedidos ? "l.LinDescripcion"   : "v.Categoria", lCat, "ct", parms);
        var divJoin  = lDiv.Count > 0 ? "INNER JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = r.RepCodigo" : "";
        var repWhere = $"r.RepEstatus = 1 AND ISNULL(r.RepCargo,'') <> 'Supervisor' {wCentros} {wSups} {wReps} {wDiv}";

        var condParts = new List<string>();
        if (wSup != "") condParts.Add(wSup[4..]);
        if (wCat != "") condParts.Add(wCat[4..]);
        var supCond = string.Join(" AND ", condParts);

        // Montos por cliente en el rango ACTIVO (unidades + valor bruto). Fuente-dependiente.
        var montosActivo = esPedidos ? $@"
    SELECT p.CliID AS ClienteCodigo, SUM(ISNULL(pd.PedCantidad,0)) AS Uni,
           SUM(ISNULL(pd.PedCantidad,0) * ISNULL(pd.PedPrecio,0)) AS Val
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN RepsF rf ON rf.RepCodigo = p.RepCodigo
    INNER  JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia = p.PedSecuencia AND pd.RepCodigo = p.RepCodigo
    INNER  JOIN Productos  pr WITH (NOLOCK) ON pr.ProID  = pd.ProID
    INNER  JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = pr.Cat3ID
    INNER  JOIN Lineas     l  WITH (NOLOCK) ON l.LinID   = pr.LinID
    WHERE  {supCond} AND CAST(p.PedFecha AS DATE) BETWEEN @ai AND @af
    GROUP  BY p.CliID" : $@"
    SELECT v.ClienteCodigo, SUM(ISNULL(v.VentasUnidades,0)) AS Uni, SUM(ISNULL(v.ValorBruto,0)) AS Val
    FROM   VwVentasResumen2026 v WITH (NOLOCK)
    INNER  JOIN RepsF rf ON rf.RepCodigo = v.CodigoVendedor
    WHERE  {supCond} AND CAST(v.VenFechaSinHora AS DATE) BETWEEN @ai AND @af
    GROUP  BY v.ClienteCodigo";

        // Existencia de compra en el rango EVALUACION
        var compraEval = esPedidos ? $@"
    SELECT DISTINCT p.CliID AS ClienteCodigo
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN RepsF rf ON rf.RepCodigo = p.RepCodigo
    INNER  JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia = p.PedSecuencia AND pd.RepCodigo = p.RepCodigo
    INNER  JOIN Productos  pr WITH (NOLOCK) ON pr.ProID  = pd.ProID
    INNER  JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = pr.Cat3ID
    INNER  JOIN Lineas     l  WITH (NOLOCK) ON l.LinID   = pr.LinID
    WHERE  {supCond} AND CAST(p.PedFecha AS DATE) BETWEEN @ei AND @ef" : $@"
    SELECT DISTINCT v.ClienteCodigo
    FROM   VwVentasResumen2026 v WITH (NOLOCK)
    INNER  JOIN RepsF rf ON rf.RepCodigo = v.CodigoVendedor
    WHERE  {supCond} AND CAST(v.VenFechaSinHora AS DATE) BETWEEN @ei AND @ef";

        var sql = $@"
;WITH RepsF AS (
    SELECT r.RepCodigo FROM Representantes r WITH (NOLOCK)
    {divJoin}
    WHERE {repWhere}
),
Universo AS (
    SELECT DISTINCT ru.CliID
    FROM   RutaVisitasFechaHistoricos ru WITH (NOLOCK)
    INNER  JOIN RepsF rf ON rf.RepCodigo = ru.RepCodigo
    WHERE  CAST(ru.RutFecha AS DATE) BETWEEN @ai AND @af
),
MontosActivo AS ({montosActivo}
),
CompraEval AS ({compraEval}
),
UltVenta AS (
    SELECT v.CliID, MAX(v.VenFecha) AS Ult
    FROM   VWVentas v WITH (NOLOCK)
    INNER  JOIN Universo uu ON uu.CliID = v.CliID
    WHERE  v.VenEstatus <> 0 AND v.VenFecha >= DATEADD(YEAR,-3,GETDATE())
    GROUP  BY v.CliID
)
SELECT c.CliID, ISNULL(c.CliNombreComercial, c.CliNombre) AS Nombre,
       c.CliLatitud, c.CliLongitud, ISNULL(c.RepCodigo,'') AS RepCodigo,
       CASE WHEN ma.ClienteCodigo IS NOT NULL THEN 1 ELSE 0 END AS ComproActivo,
       CASE WHEN ce.ClienteCodigo IS NOT NULL THEN 1 ELSE 0 END AS ComproEval,
       ISNULL(ma.Uni, 0) AS Unidades, ISNULL(ma.Val, 0) AS ValorBruto, ISNULL(c.ClaID,0) AS ClaID,
       uv.Ult AS UltimaCompra
FROM   Universo u
INNER  JOIN Clientes c WITH (NOLOCK) ON c.CliID = u.CliID
LEFT   JOIN MontosActivo ma ON ma.ClienteCodigo = u.CliID
LEFT   JOIN CompraEval  ce ON ce.ClienteCodigo = u.CliID
LEFT   JOIN UltVenta    uv ON uv.CliID = u.CliID;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 };
            cmd.Parameters.AddWithValue("@ai", ai.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@af", af.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@ei", ei.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@ef", ef.ToString("yyyy-MM-dd"));
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);

            var clientes = new List<object>();
            var topDej = new Dictionary<string, int>();
            int universo = 0, retenidos = 0, dejaron = 0, recuperados = 0, inactivos = 0, sinGps = 0;
            double sumMeses = 0; int nMeses = 0;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                universo++;
                var comproActivo = rd.GetInt32(5) == 1;
                var comproEval   = rd.GetInt32(6) == 1;
                var repCod = rd.IsDBNull(4) ? "" : rd.GetString(4);

                // 4 estados de retencion
                string estado;
                if (comproActivo && comproEval)       { retenidos++;   estado = "retenido"; }
                else if (comproActivo && !comproEval) { dejaron++;     estado = "dejo"; topDej[repCod] = topDej.GetValueOrDefault(repCod) + 1; }
                else if (!comproActivo && comproEval) { recuperados++; estado = "recuperado"; }
                else                                  { inactivos++;   estado = "inactivo"; }

                // Tiempo sin comprar (para inactivos): meses desde la ultima venta
                if (estado == "inactivo" && !rd.IsDBNull(10))
                { sumMeses += (DateTime.Now - rd.GetDateTime(10)).TotalDays / 30.44; nMeses++; }

                bool hasGps = !rd.IsDBNull(2) && !rd.IsDBNull(3) && Convert.ToDouble(rd.GetValue(2)) != 0;
                if (!hasGps) { sinGps++; continue; }

                clientes.Add(new
                {
                    CliID      = rd.GetInt32(0),
                    Nombre     = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    Latitud    = Convert.ToDouble(rd.GetValue(2)),
                    Longitud   = Convert.ToDouble(rd.GetValue(3)),
                    RepCodigo  = repCod,
                    Estado     = estado,
                    Unidades   = Convert.ToDouble(rd.GetValue(7)),
                    ValorBruto = Convert.ToDouble(rd.GetValue(8)),
                    ClaID      = Convert.ToInt32(rd.GetValue(9))
                });
            }

            var topDejaron = topDej.OrderByDescending(x => x.Value).Take(3)
                .Select(x => new { RepCodigo = x.Key, clientes = x.Value });

            return Ok(new
            {
                ok = true,
                activoIni = ai.ToString("yyyy-MM-dd"), activoFin = af.ToString("yyyy-MM-dd"),
                evalIni = ei.ToString("yyyy-MM-dd"), evalFin = ef.ToString("yyyy-MM-dd"),
                fuente = esPedidos ? "pedidos" : "ventas",
                universo, retenidos, dejaron, recuperados, inactivos, sinGps,
                mesesSinCompra = nMeses > 0 ? Math.Round(sumMeses / nMeses) : (double?)null,
                topDejaron, clientes
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── Fotos de representantes en el mapa (2026-10-08) ─────────────────────────
    // Las fotos viven en RegistrosImagenes (RepTabla='Representantes', RepTablaKey=RepCodigo, TraImagen = image/bytes,
    // TraFormato png/jpeg; ~200-430 KB cada una). Para el mapa se sirven como MINIATURAS cuadradas (recorte centrado,
    // anclado arriba para no cortar caras) cacheadas en memoria; el navegador las cachea 1 día y la versión (?v=) se
    // renueva sola cuando cambia TraFechaActualizacion.

    // GET /apigeo/fotos-reps → qué representantes tienen foto (sin descargar las imágenes) + versión para el caché
    [HttpGet("fotos-reps")]
    public async Task<IActionResult> FotosReps()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(@"
SELECT RepTablaKey, MAX(ISNULL(TraFechaActualizacion,'2000-01-01')) AS V
FROM RegistrosImagenes WITH (NOLOCK)
WHERE RepTabla='Representantes' AND TraImagen IS NOT NULL AND DATALENGTH(TraImagen)>100
GROUP BY RepTablaKey", cn) { CommandTimeout = 30 };
            var fotos = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                fotos.Add(new { cod = rd.GetString(0).Trim(), v = new DateTimeOffset(rd.GetDateTime(1)).ToUnixTimeSeconds() });
            return Ok(new { ok = true, fotos });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // GET /apigeo/foto-rep?cod=84&s=72 → miniatura PNG cuadrada de s×s px (24..256)
    [HttpGet("foto-rep")]
    public async Task<IActionResult> FotoRep([FromQuery] string cod, [FromQuery] int s = 72)
    {
        if (!IsAuth) return Unauthorized();
        if (string.IsNullOrWhiteSpace(cod)) return NotFound();
        s = Math.Clamp(s, 24, 256);
        var key = $"geo_foto|{cod.Trim()}|{s}";
        if (!cache.TryGetValue(key, out byte[]? png))
        {
            try
            {
                await using var cn = NewConn();
                await cn.OpenAsync();
                await using var cmd = new SqlCommand(
                    "SELECT TOP 1 TraImagen FROM RegistrosImagenes WITH (NOLOCK) WHERE RepTabla='Representantes' AND RepTablaKey=@k AND TraImagen IS NOT NULL ORDER BY TraFechaActualizacion DESC", cn);
                cmd.Parameters.AddWithValue("@k", cod.Trim());
                var raw = await cmd.ExecuteScalarAsync() as byte[];
                png = raw == null ? null : Miniatura(raw, s);
            }
            catch { png = null; }
            // También se cachea el "no hay / no se pudo leer" (null) por un rato, para no golpear la BD en cada marcador.
            cache.Set(key, png, png == null ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(6));
        }
        if (png == null) return NotFound();
        Response.Headers["Cache-Control"] = "public, max-age=86400";
        return File(png, "image/png");
    }

    // Recorte cuadrado centrado en X y anclado arriba (retratos), escalado a s×s con calidad alta; PNG con transparencia.
    private static byte[]? Miniatura(byte[] data, int s)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var img = System.Drawing.Image.FromStream(ms);
            int side = Math.Min(img.Width, img.Height);
            int x = (img.Width - side) / 2;
            using var bmp = new System.Drawing.Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(img, new System.Drawing.Rectangle(0, 0, s, s), new System.Drawing.Rectangle(x, 0, side, side), System.Drawing.GraphicsUnit.Pixel);
            }
            using var o = new MemoryStream();
            bmp.Save(o, System.Drawing.Imaging.ImageFormat.Png);
            return o.ToArray();
        }
        catch { return null; }
    }

    // ── GET /apigeo/empresa ─────────────────────────────────────────────────────
    [HttpGet("empresa")]
    public async Task<IActionResult> Empresa()
    {
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT TOP 1 * FROM Empresa ORDER BY EmpID", cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return Ok(new { ok = true, nombre = "" });

            var cols = Enumerable.Range(0, rd.FieldCount)
                                 .Select(i => rd.GetName(i))
                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string nombre = "";
            foreach (var col in new[] { "EmpNombre", "EmpRazonSocial", "EmpDescripcion", "Nombre" })
            {
                if (!cols.Contains(col)) continue;
                var idx = rd.GetOrdinal(col);
                if (rd.IsDBNull(idx)) continue;
                nombre = rd.GetString(idx).Trim();
                if (!string.IsNullOrEmpty(nombre)) break;
            }
            Response.Headers["Cache-Control"] = "public, max-age=3600";
            return Ok(new { ok = true, nombre });
        }
        catch { return Ok(new { ok = true, nombre = "" }); }
    }

    // ── GET /apigeo/logo ────────────────────────────────────────────────────────
    [HttpGet("logo")]
    public async Task<IActionResult> Logo()
    {
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            foreach (var col in new[] { "EmpLogo", "EmpImagen", "EmpFoto", "Logo" })
            {
                SqlCommand? cmd = null;
                try
                {
                    cmd = new SqlCommand(
                        $"SELECT TOP 1 [{col}] FROM Empresa WHERE [{col}] IS NOT NULL ORDER BY EmpID", cn);
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync() || rd.IsDBNull(0)) continue;
                    var data = (byte[])rd.GetValue(0);
                    if (data.Length < 4) continue;
                    var mime = (data[0], data[1]) switch
                    {
                        (0xFF, 0xD8) => "image/jpeg", (0x89, 0x50) => "image/png",
                        (0x47, 0x49) => "image/gif",  (0x42, 0x4D) => "image/bmp",
                        _            => "image/jpeg"
                    };
                    Response.Headers["Cache-Control"] = "public, max-age=86400";
                    return File(data, mime);
                }
                catch { cmd?.Dispose(); }
            }
        }
        catch { }
        return NotFound();
    }
}
