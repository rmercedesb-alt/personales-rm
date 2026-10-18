using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DashVisitas.Controllers;

// ── API del modulo Geolocalizaciones ────────────────────────────────────────
// Ruta base /apigeo para no colisionar con /api (Visitas) ni /apip (Productividad)
[ApiController]
[Route("apigeo")]
public class ApiGeoController(IConfiguration config) : ControllerBase
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
        var wCentros = BuildIn("r.CedCodigo",     ParseList(centros),      "c",  parms);
        var wSups    = BuildIn("r.RepSupervisor", ParseList(supervisores), "s",  parms);
        var wReps    = BuildIn("r.RepCodigo",     ParseList(reps),         "rp", parms);
        var wDiv     = BuildIn("ra.RepTipe",      lDiv,                    "dv", parms);
        var wSup     = BuildIn("ps.Suplidor",     lSup,                    "sp", parms);
        var wCat     = BuildIn("ps.Categoria",    lCat,                    "ct", parms);

        var divJoin  = lDiv.Count > 0 ? "INNER JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = r.RepCodigo" : "";
        var repWhere = $"r.RepEstatus = 1 AND ISNULL(r.RepCargo,'') <> 'Supervisor' {wCentros} {wSups} {wReps} {wDiv}";

        // Condicion "compra lo filtrado" para el CASE (sup y/o categoria)
        var condParts = new List<string>();
        if (wSup != "") condParts.Add(wSup[4..]);
        if (wCat != "") condParts.Add(wCat[4..]);
        var buysCond = string.Join(" AND ", condParts);

        // Una columna por suplidor seleccionado para el "Top 3 Suplidores con Oportunidad"
        // (evita una 2a consulta pesada). Buy{i} = 1 si el cliente PIDIO ese suplidor.
        var catCond = wCat != "" ? " AND " + wCat[4..] : "";
        var supCols      = string.Concat(lSup.Select((s, i) => $", MAX(CASE WHEN ps.Suplidor = @sp{i}{catCond} THEN 1 ELSE 0 END) AS Buy{i}"));
        var supColsOuter = string.Concat(Enumerable.Range(0, lSup.Count).Select(i => $", vt.Buy{i}"));

        // Oportunidad se calcula contra PEDIDOS (VWPedidos + detalle → suplidor/categoria), no ventas.
        var sqlA = $@"
;WITH RepsF AS (
    SELECT r.RepCodigo FROM Representantes r WITH (NOLOCK)
    {divJoin}
    WHERE {repWhere}
),
PedSup AS (
    SELECT DISTINCT p.CliID,
           c3.Cat3Descripcion AS Suplidor,
           l.LinDescripcion   AS Categoria
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN RepsF rf ON rf.RepCodigo = p.RepCodigo
    INNER  JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia = p.PedSecuencia AND pd.RepCodigo = p.RepCodigo
    INNER  JOIN Productos  pr WITH (NOLOCK) ON pr.ProID  = pd.ProID
    INNER  JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = pr.Cat3ID
    INNER  JOIN Lineas     l  WITH (NOLOCK) ON l.LinID   = pr.LinID
    WHERE  CAST(p.PedFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
),
Ventas AS (
    SELECT ps.CliID,
           MAX(CASE WHEN {buysCond} THEN 1 ELSE 0 END) AS BuysSelected{supCols}
    FROM   PedSup ps
    GROUP  BY ps.CliID
),
MontosPed AS (
    SELECT p.CliID, SUM(ISNULL(pd.PedCantidad,0)) AS Uni,
           SUM(ISNULL(pd.PedCantidad,0) * ISNULL(pd.PedPrecio,0)) AS Val
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN RepsF rf ON rf.RepCodigo = p.RepCodigo
    INNER  JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia = p.PedSecuencia AND pd.RepCodigo = p.RepCodigo
    WHERE  CAST(p.PedFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
    GROUP  BY p.CliID
)
SELECT c.CliID, ISNULL(c.CliNombreComercial, c.CliNombre) AS Nombre,
       c.CliLatitud, c.CliLongitud, ISNULL(c.RepCodigo,'') AS RepCodigo, vt.BuysSelected{supColsOuter},
       ISNULL(mp.Uni,0) AS Unidades, ISNULL(mp.Val,0) AS ValorBruto
FROM   Ventas vt
INNER  JOIN Clientes c WITH (NOLOCK) ON c.CliID = vt.CliID
LEFT   JOIN MontosPed mp ON mp.CliID = vt.CliID;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            var incluir = incluirCompran == true;
            var clientes = new List<object>();
            var topVend = new Dictionary<string, int>();
            var supBuyers = new int[lSup.Count];
            int compradores = 0, oport = 0, sinGps = 0;

            await using (var cmd = new SqlCommand(sqlA, cn) { CommandTimeout = 90 })
            {
                cmd.Parameters.AddWithValue("@fechaIni", fiStr);
                cmd.Parameters.AddWithValue("@fechaFin", ffStr);
                foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    compradores++;
                    var buys = rd.GetInt32(5) == 1;
                    var repCod = rd.IsDBNull(4) ? "" : rd.GetString(4);
                    for (int i = 0; i < lSup.Count; i++)
                        if (Convert.ToInt32(rd.GetValue(6 + i)) == 1) supBuyers[i]++;
                    if (!buys) { oport++; topVend[repCod] = topVend.GetValueOrDefault(repCod) + 1; }
                    if (buys && !incluir) continue;

                    bool hasGps = !rd.IsDBNull(2) && !rd.IsDBNull(3) && Convert.ToDouble(rd.GetValue(2)) != 0;
                    if (!hasGps) { if (!buys) sinGps++; continue; }

                    var uni = Convert.ToDouble(rd.GetValue(6 + lSup.Count));
                    var val = Convert.ToDouble(rd.GetValue(7 + lSup.Count));
                    clientes.Add(new
                    {
                        CliID      = rd.GetInt32(0),
                        Nombre     = rd.IsDBNull(1) ? "" : rd.GetString(1),
                        Latitud    = Convert.ToDouble(rd.GetValue(2)),
                        Longitud   = Convert.ToDouble(rd.GetValue(3)),
                        RepCodigo  = repCod,
                        Compra     = buys,
                        Unidades   = uni,
                        ValorBruto = val
                    });
                }
            }

            var topSuplidores = lSup
                .Select((s, i) => new { Suplidor = s, oportunidad = compradores - supBuyers[i] })
                .OrderByDescending(x => x.oportunidad).Take(3);

            var topVendedores = topVend.OrderByDescending(x => x.Value).Take(3)
                .Select(x => new { RepCodigo = x.Key, clientes = x.Value });

            return Ok(new
            {
                ok = true, fechaIni = fiStr, fechaFin = ffStr,
                oportunidades = oport, compradores, compranFiltrado = compradores - oport, sinGps,
                topVendedores, topSuplidores, clientes
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
)
SELECT c.CliID, ISNULL(c.CliNombreComercial, c.CliNombre) AS Nombre,
       c.CliLatitud, c.CliLongitud, ISNULL(c.RepCodigo,'') AS RepCodigo,
       CASE WHEN ma.ClienteCodigo IS NOT NULL THEN 1 ELSE 0 END AS ComproActivo,
       CASE WHEN ce.ClienteCodigo IS NOT NULL THEN 1 ELSE 0 END AS ComproEval,
       ISNULL(ma.Uni, 0) AS Unidades, ISNULL(ma.Val, 0) AS ValorBruto
FROM   Universo u
INNER  JOIN Clientes c WITH (NOLOCK) ON c.CliID = u.CliID
LEFT   JOIN MontosActivo ma ON ma.ClienteCodigo = u.CliID
LEFT   JOIN CompraEval  ce ON ce.ClienteCodigo = u.CliID;";

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
                    ValorBruto = Convert.ToDouble(rd.GetValue(8))
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
                topDejaron, clientes
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
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
