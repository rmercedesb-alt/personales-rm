using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

[ApiController]
[Route("apip")]
[RequiereModulo("productividad")]
public class ApiProductividadController(IConfiguration config) : ControllerBase
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

    // ── GET /api/me ────────────────────────────────────────────────────────────
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

    // ── GET /api/data ──────────────────────────────────────────────────────────
    // Params: fechaIni, fechaFin, centros, supervisores, suplidores, lineas, reptype, canales  (CSV)
    // Cuando el rango > 1 dia se suman TotalAVisitar, Visitados y PedReqalMomento
    [HttpGet("data")]
    public async Task<IActionResult> Data(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? suplidores,
        [FromQuery] string? lineas,
        [FromQuery] string? reptype,
        [FromQuery] string? canales,
        [FromQuery(Name = "reps")] string? repsFilter)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var fiStr = fi.ToString("yyyy-MM-dd");
        var ffStr = ff.ToString("yyyy-MM-dd");

        var lCentros = ParseList(centros);
        var lSups    = ParseList(supervisores);
        var lSuplid  = ParseList(suplidores);
        var lLineas  = ParseList(lineas);
        var lReptype = ParseList(reptype);
        var lCanales = ParseList(canales);
        var lReps    = ParseList(repsFilter);

        var parms    = new Dictionary<string, string>();
        var wCentros = BuildIn("cd.CedCodigo",                      lCentros, "c",  parms);
        var wSuperv  = BuildIn("rv.codSupervisor",                  lSups,    "s",  parms);
        var wReptype = BuildIn("CAST(rv.RepType AS NVARCHAR(50))",  lReptype, "rt", parms);
        var wCanales = BuildIn("CAST(ra.CanID AS NVARCHAR(50))",    lCanales, "cn", parms);
        var wReps    = BuildIn("rv.RepCodigo",                      lReps,    "rp", parms);

        var pedParms = new Dictionary<string, string>();
        var wSuplid  = BuildIn("CAST(c3.Cat3ID AS NVARCHAR(50))", lSuplid, "sp", pedParms);
        var wLineas  = BuildIn("CAST(l.LinID    AS NVARCHAR(50))", lLineas, "ln", pedParms);

        string wherePed = "";
        var pedParts = new List<string>();
        if (!string.IsNullOrEmpty(wSuplid)) pedParts.Add(wSuplid[4..]);
        if (!string.IsNullOrEmpty(wLineas)) pedParts.Add(wLineas[4..]);
        if (pedParts.Count > 0) wherePed = "WHERE " + string.Join(" AND ", pedParts);

        // BaseRaw aplica filtros fila a fila; Base agrupa por RepCodigo (necesario para rangos)
        var sql = $@"
WITH BaseRaw AS (
    SELECT
        rv.RepCodigo,
        ISNULL(rep.RepNombre, rv.RepNombre) + ' - ' + CAST(rv.RepCodigo AS NVARCHAR(20)) AS Vendedor,
        ISNULL(sup.RepNombre,  ISNULL(rv.Supervisor, rv.codSupervisor))   AS Supervisor,
        ISNULL(rv.codSupervisor, '')                                       AS SupervisorCodigo,
        ISNULL(CAST(rv.RepType AS NVARCHAR(50)), '')                       AS Division,
        ISNULL(cd.CedDescripcion, ISNULL(cd.CedCodigo, ''))               AS CentroNombre,
        ISNULL(cd.CedCodigo, '')                                           AS CedCodigo,
        ISNULL(cn.CanDescripcion, '')                                      AS CanalNombre,
        ISNULL(rv.TotalAVisitar,   0)                                      AS TotalVisitas,
        ISNULL(rv.Visitados,       0)                                      AS TotalVisitados,
        ISNULL(rv.PedReqalMomento, 0)                                      AS PedReqalMomento
    FROM ResumenVisitasRm rv
    INNER JOIN Representantes      rep ON rep.RepCodigo = rv.RepCodigo
    LEFT  JOIN Representantes      sup ON sup.RepCodigo = rv.codSupervisor
    LEFT  JOIN CentrosDistribucion cd  ON cd.CedCodigo  = rep.CedCodigo
    LEFT  JOIN (SELECT RepCodigo, MIN(CanID) AS CanID
                FROM   RepresentantesAdicionales
                GROUP  BY RepCodigo) ra ON ra.RepCodigo = rep.RepCodigo
    LEFT  JOIN CanalDistribucion   cn  ON cn.CanID      = ra.CanID
    WHERE CAST(rv.FechaVisita AS DATE) BETWEEN @fechaIni AND @fechaFin
      AND rep.RepEstatus = 1
      AND ISNULL(rep.RepCargo, '') <> 'Supervisor'
    {wCentros}
    {wSuperv}
    {wReptype}
    {wCanales}
    {wReps}
),
Base AS (
    SELECT
        RepCodigo,
        MAX(Vendedor)         AS Vendedor,
        MAX(Supervisor)       AS Supervisor,
        MAX(SupervisorCodigo) AS SupervisorCodigo,
        MAX(Division)         AS Division,
        MAX(CentroNombre)     AS CentroNombre,
        MAX(CedCodigo)        AS CedCodigo,
        MAX(CanalNombre)      AS CanalNombre,
        SUM(TotalVisitas)     AS TotalVisitas,
        SUM(TotalVisitados)   AS TotalVisitados,
        SUM(PedReqalMomento)  AS PedReqalMomento
    FROM BaseRaw
    GROUP BY RepCodigo
),
PedidosCTE AS (
    SELECT
        b.RepCodigo,
        COUNT(DISTINCT p.PedSecuencia) AS PedidosRealizados
    FROM   Base b
    INNER  JOIN VWPedidos        p  ON  p.RepCodigo    = b.RepCodigo
                                    AND CAST(p.PedFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
    LEFT   JOIN VWPedidosDetalle pd ON  pd.PedSecuencia = p.PedSecuencia
                                    AND pd.RepCodigo    = p.RepCodigo
    LEFT   JOIN Productos        pr ON  pr.ProID        = pd.ProID
    LEFT   JOIN Categoria3       c3 ON  c3.Cat3ID       = pr.Cat3ID
    LEFT   JOIN Lineas           l  ON  l.LinID         = pr.LinID
    {wherePed}
    GROUP  BY b.RepCodigo
)
SELECT
    b.Supervisor,
    b.SupervisorCodigo,
    b.Vendedor,
    b.RepCodigo,
    b.CentroNombre,
    b.CedCodigo,
    b.Division,
    b.CanalNombre,
    b.TotalVisitas,
    b.TotalVisitados,
    ISNULL(pc.PedidosRealizados, 0) AS PedidosRealizados,
    b.PedReqalMomento
FROM   Base b
LEFT   JOIN PedidosCTE pc ON pc.RepCodigo = b.RepCodigo
ORDER  BY b.Supervisor, b.Vendedor;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@fechaIni", fiStr);
            cmd.Parameters.AddWithValue("@fechaFin", ffStr);
            foreach (var (k, v) in parms)    cmd.Parameters.AddWithValue(k, v);
            foreach (var (k, v) in pedParms) cmd.Parameters.AddWithValue(k, v);

            var reps = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                reps.Add(new
                {
                    Supervisor        = rd.IsDBNull(0)  ? "" : rd.GetString(0),
                    SupervisorCodigo  = rd.IsDBNull(1)  ? "" : rd.GetString(1),
                    Vendedor          = rd.IsDBNull(2)  ? "" : rd.GetString(2),
                    RepCodigo         = rd.IsDBNull(3)  ? "" : rd.GetString(3),
                    CentroNombre      = rd.IsDBNull(4)  ? "" : rd.GetString(4),
                    CedCodigo         = rd.IsDBNull(5)  ? "" : rd.GetString(5),
                    Division          = rd.IsDBNull(6)  ? "" : rd.GetString(6),
                    CanalNombre       = rd.IsDBNull(7)  ? "" : rd.GetString(7),
                    TotalVisitas      = rd.IsDBNull(8)  ? 0 : Convert.ToInt32(rd.GetValue(8)),
                    TotalVisitados    = rd.IsDBNull(9)  ? 0 : Convert.ToInt32(rd.GetValue(9)),
                    PedidosRealizados = rd.IsDBNull(10) ? 0 : Convert.ToInt32(rd.GetValue(10)),
                    PedReqalMomento   = rd.IsDBNull(11) ? 0 : Convert.ToInt32(rd.GetValue(11))
                });
            }

            return Ok(new
            {
                ok       = true,
                fechaIni = fiStr,
                fechaFin = ffStr,
                usuario  = HttpContext.Session.GetString("user_nombre"),
                reps
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }

    // ── GET /api/data-suplidor ─────────────────────────────────────────────────
    // Agrupa pedidos por Suplidor (Categoria3) + Categoría (Linea)
    [HttpGet("data-suplidor")]
    public async Task<IActionResult> DataSuplidor(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? suplidores,
        [FromQuery] string? lineas,
        [FromQuery] string? reptype,
        [FromQuery] string? canales,
        [FromQuery(Name = "reps")] string? repsFilter)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var fiStr = fi.ToString("yyyy-MM-dd");
        var ffStr = ff.ToString("yyyy-MM-dd");

        var lCentros = ParseList(centros);
        var lSups    = ParseList(supervisores);
        var lSuplid  = ParseList(suplidores);
        var lLineas  = ParseList(lineas);
        var lReptype = ParseList(reptype);
        var lCanales = ParseList(canales);
        var lReps    = ParseList(repsFilter);

        var repParms = new Dictionary<string, string>();
        var wCentros = BuildIn("cd.CedCodigo",                      lCentros, "c",  repParms);
        var wSuperv  = BuildIn("rv.codSupervisor",                  lSups,    "s",  repParms);
        var wReptype = BuildIn("CAST(rv.RepType AS NVARCHAR(50))",  lReptype, "rt", repParms);
        var wCanales = BuildIn("CAST(ra.CanID AS NVARCHAR(50))",    lCanales, "cn", repParms);
        var wReps    = BuildIn("rv.RepCodigo",                      lReps,    "rp", repParms);

        var pedParms = new Dictionary<string, string>();
        var wSuplid  = BuildIn("CAST(c3.Cat3ID AS NVARCHAR(50))", lSuplid, "sp", pedParms);
        var wLineas  = BuildIn("CAST(l.LinID    AS NVARCHAR(50))", lLineas, "ln", pedParms);

        var pedPartsWhere = new List<string>();
        if (!string.IsNullOrEmpty(wSuplid)) pedPartsWhere.Add(wSuplid[4..]);
        if (!string.IsNullOrEmpty(wLineas)) pedPartsWhere.Add(wLineas[4..]);
        var extraWhere = pedPartsWhere.Count > 0
            ? "AND " + string.Join(" AND ", pedPartsWhere)
            : "";

        var sql = $@"
WITH RepsFiltrados AS (
    SELECT DISTINCT rv.RepCodigo
    FROM   ResumenVisitasRm rv
    INNER  JOIN Representantes      rep ON rep.RepCodigo = rv.RepCodigo
    LEFT   JOIN CentrosDistribucion cd  ON cd.CedCodigo  = rep.CedCodigo
    LEFT   JOIN (SELECT RepCodigo, MIN(CanID) AS CanID
                 FROM   RepresentantesAdicionales
                 GROUP  BY RepCodigo) ra ON ra.RepCodigo = rep.RepCodigo
    WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @fechaIni AND @fechaFin
      AND  rep.RepEstatus = 1
      AND  ISNULL(rep.RepCargo, '') <> 'Supervisor'
    {wCentros}
    {wSuperv}
    {wReptype}
    {wCanales}
    {wReps}
)
SELECT
    GROUPING(c3.Cat3ID) AS gSup,
    GROUPING(l.LinID)   AS gCat,
    ISNULL(c3.Cat3Descripcion, CAST(c3.Cat3ID AS NVARCHAR(50))) AS Suplidor,
    CAST(c3.Cat3ID AS NVARCHAR(50))                             AS SuplidorID,
    ISNULL(l.LinDescripcion,   CAST(l.LinID   AS NVARCHAR(50))) AS Categoria,
    CAST(l.LinID AS NVARCHAR(50))                               AS CategoriaID,
    COUNT(DISTINCT p.PedSecuencia)              AS Pedidos,
    COUNT(DISTINCT p.RepCodigo)                 AS NroReps,
    COUNT(DISTINCT CAST(p.PedFecha AS DATE))    AS NroDias
FROM   RepsFiltrados rf
INNER  JOIN VWPedidos        p  ON  p.RepCodigo    = rf.RepCodigo
                                AND CAST(p.PedFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
LEFT   JOIN VWPedidosDetalle pd ON  pd.PedSecuencia = p.PedSecuencia
                                AND pd.RepCodigo    = p.RepCodigo
LEFT   JOIN Productos        pr ON  pr.ProID        = pd.ProID
LEFT   JOIN Categoria3       c3 ON  c3.Cat3ID       = pr.Cat3ID
LEFT   JOIN Lineas           l  ON  l.LinID         = pr.LinID
WHERE  c3.Cat3ID IS NOT NULL
{extraWhere}
GROUP  BY GROUPING SETS (
           (c3.Cat3ID, c3.Cat3Descripcion, l.LinID, l.LinDescripcion),  -- detalle: suplidor+categoria
           (c3.Cat3ID, c3.Cat3Descripcion),                             -- subtotal por suplidor
           ()                                                            -- total general
         )
ORDER  BY gSup, Suplidor, gCat, Categoria;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@fechaIni", fiStr);
            cmd.Parameters.AddWithValue("@fechaFin", ffStr);
            foreach (var (k, v) in repParms) cmd.Parameters.AddWithValue(k, v);
            foreach (var (k, v) in pedParms) cmd.Parameters.AddWithValue(k, v);

            var rows = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                int gSup = Convert.ToInt32(rd.GetValue(0));
                int gCat = Convert.ToInt32(rd.GetValue(1));
                var nivel = gSup == 1 ? "total" : (gCat == 1 ? "subtotal" : "detalle");
                rows.Add(new
                {
                    nivel,
                    Suplidor    = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    SuplidorID  = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    Categoria   = rd.IsDBNull(4) ? "" : rd.GetString(4),
                    CategoriaID = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    Pedidos     = rd.IsDBNull(6) ? 0 : Convert.ToInt32(rd.GetValue(6)),
                    NroReps     = rd.IsDBNull(7) ? 0 : Convert.ToInt32(rd.GetValue(7)),
                    NroDias     = rd.IsDBNull(8) ? 0 : Convert.ToInt32(rd.GetValue(8))
                });
            }

            return Ok(new
            {
                ok       = true,
                fechaIni = fiStr,
                fechaFin = ffStr,
                rows
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }

    // ── GET /api/data-tamano ──────────────────────────────────────────────────
    // Agrupa "A Visitar" por Tamaño de Cliente (Clasifica) via RutaVisitasFechaHistoricos→Clientes
    // y pedidos via VWPedidos→Clientes→Clasifica
    [HttpGet("data-tamano")]
    public async Task<IActionResult> DataTamano(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? lineas,
        [FromQuery] string? reptype,
        [FromQuery] string? canales,
        [FromQuery] string? tamanos,
        [FromQuery(Name = "reps")] string? repsFilter)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var fiStr = fi.ToString("yyyy-MM-dd");
        var ffStr = ff.ToString("yyyy-MM-dd");

        var lCentros = ParseList(centros);
        var lSups    = ParseList(supervisores);
        var lReptype = ParseList(reptype);
        var lCanales = ParseList(canales);
        var lTamanos = ParseList(tamanos);
        var lReps    = ParseList(repsFilter);

        var repParms = new Dictionary<string, string>();
        var wCentros = BuildIn("cd.CedCodigo",                      lCentros, "c",  repParms);
        var wSuperv  = BuildIn("rv.codSupervisor",                  lSups,    "s",  repParms);
        var wReptype = BuildIn("CAST(rv.RepType AS NVARCHAR(50))",  lReptype, "rt", repParms);
        var wCanales = BuildIn("CAST(ra.CanID AS NVARCHAR(50))",    lCanales, "cn", repParms);
        var wReps    = BuildIn("rv.RepCodigo",                      lReps,    "rp", repParms);

        var tamParms = new Dictionary<string, string>();
        var wTamanos = BuildIn("CAST(cl.ClaID AS NVARCHAR(50))",    lTamanos, "ta", tamParms);

        var sql = $@"
WITH RepsFiltrados AS (
    SELECT DISTINCT rv.RepCodigo
    FROM   ResumenVisitasRm rv
    INNER  JOIN Representantes      rep ON rep.RepCodigo = rv.RepCodigo
    LEFT   JOIN CentrosDistribucion cd  ON cd.CedCodigo  = rep.CedCodigo
    LEFT   JOIN (SELECT RepCodigo, MIN(CanID) AS CanID
                 FROM   RepresentantesAdicionales
                 GROUP  BY RepCodigo) ra ON ra.RepCodigo = rep.RepCodigo
    WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @fechaIni AND @fechaFin
      AND  rep.RepEstatus = 1
      AND  ISNULL(rep.RepCargo, '') <> 'Supervisor'
    {wCentros}
    {wSuperv}
    {wReptype}
    {wCanales}
    {wReps}
),
AVisitarCTE AS (
    -- Clientes en ruta para el rango de fechas, agrupados por ClaID
    SELECT  c.ClaID,
            COUNT(DISTINCT rut.CliId) AS AVisitar
    FROM    RutaVisitasFechaHistoricos rut
    INNER   JOIN RepsFiltrados rf ON rf.RepCodigo = rut.RepCodigo
    INNER   JOIN Clientes      c  ON c.CliId      = rut.CliId
    WHERE   CAST(rut.RutFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
    GROUP   BY c.ClaID
),
PedidosCTE AS (
    -- Pedidos agrupados por ClaID del cliente
    SELECT  c.ClaID,
            COUNT(DISTINCT p.PedSecuencia) AS Pedidos
    FROM    RepsFiltrados rf
    INNER   JOIN VWPedidos p ON  p.RepCodigo = rf.RepCodigo
                             AND CAST(p.PedFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
    INNER   JOIN Clientes  c ON  c.CliId     = p.CliID
    GROUP   BY c.ClaID
)
SELECT
    ISNULL(cl.ClaDescripcion, CAST(cl.ClaID AS NVARCHAR(50))) AS TamanoCliente,
    CAST(cl.ClaID AS NVARCHAR(50))                             AS ClaID,
    ISNULL(av.AVisitar, 0)                                     AS AVisitar,
    ISNULL(pe.Pedidos,  0)                                     AS Pedidos
FROM   Clasifica cl
LEFT   JOIN AVisitarCTE av ON av.ClaID = cl.ClaID
LEFT   JOIN PedidosCTE  pe ON pe.ClaID = cl.ClaID
WHERE  (ISNULL(av.AVisitar, 0) > 0 OR ISNULL(pe.Pedidos, 0) > 0)
{wTamanos}
ORDER  BY cl.ClaDescripcion;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@fechaIni", fiStr);
            cmd.Parameters.AddWithValue("@fechaFin", ffStr);
            foreach (var (k, v) in repParms) cmd.Parameters.AddWithValue(k, v);
            foreach (var (k, v) in tamParms) cmd.Parameters.AddWithValue(k, v);

            var rows = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                rows.Add(new
                {
                    TamanoCliente = rd.IsDBNull(0) ? "" : rd.GetString(0),
                    ClaID         = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    AVisitar      = rd.IsDBNull(2) ?  0 : Convert.ToInt32(rd.GetValue(2)),
                    Pedidos       = rd.IsDBNull(3) ?  0 : Convert.ToInt32(rd.GetValue(3))
                });
            }

            return Ok(new { ok = true, fechaIni = fiStr, fechaFin = ffStr, rows });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }

    // ── GET /api/data-fecha ────────────────────────────────────────────────────
    // Desglose POR FECHA: una fila por dia del rango con visitas/visitados/pedidos.
    [HttpGet("data-fecha")]
    public async Task<IActionResult> DataFecha(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? suplidores,
        [FromQuery] string? lineas,
        [FromQuery] string? reptype,
        [FromQuery] string? canales,
        [FromQuery(Name = "reps")] string? repsFilter)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var fiStr = fi.ToString("yyyy-MM-dd");
        var ffStr = ff.ToString("yyyy-MM-dd");

        var lCentros = ParseList(centros);
        var lSups    = ParseList(supervisores);
        var lSuplid  = ParseList(suplidores);
        var lLineas  = ParseList(lineas);
        var lReptype = ParseList(reptype);
        var lCanales = ParseList(canales);
        var lReps    = ParseList(repsFilter);

        var repParms = new Dictionary<string, string>();
        var wCentros = BuildIn("cd.CedCodigo",                      lCentros, "c",  repParms);
        var wSuperv  = BuildIn("rv.codSupervisor",                  lSups,    "s",  repParms);
        var wReptype = BuildIn("CAST(rv.RepType AS NVARCHAR(50))",  lReptype, "rt", repParms);
        var wCanales = BuildIn("CAST(ra.CanID AS NVARCHAR(50))",    lCanales, "cn", repParms);
        var wReps    = BuildIn("rv.RepCodigo",                      lReps,    "rp", repParms);

        var pedParms = new Dictionary<string, string>();
        var wSuplid  = BuildIn("CAST(c3.Cat3ID AS NVARCHAR(50))", lSuplid, "sp", pedParms);
        var wLineas  = BuildIn("CAST(l.LinID    AS NVARCHAR(50))", lLineas, "ln", pedParms);

        string wherePed = "";
        var pedParts = new List<string>();
        if (!string.IsNullOrEmpty(wSuplid)) pedParts.Add(wSuplid[4..]);
        if (!string.IsNullOrEmpty(wLineas)) pedParts.Add(wLineas[4..]);
        if (pedParts.Count > 0) wherePed = "WHERE " + string.Join(" AND ", pedParts);

        var sql = $@"
WITH RepsFiltrados AS (
    SELECT DISTINCT rv.RepCodigo
    FROM   ResumenVisitasRm rv
    INNER  JOIN Representantes      rep ON rep.RepCodigo = rv.RepCodigo
    LEFT   JOIN CentrosDistribucion cd  ON cd.CedCodigo  = rep.CedCodigo
    LEFT   JOIN (SELECT RepCodigo, MIN(CanID) AS CanID
                 FROM   RepresentantesAdicionales
                 GROUP  BY RepCodigo) ra ON ra.RepCodigo = rep.RepCodigo
    WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @fechaIni AND @fechaFin
      AND  rep.RepEstatus = 1
      AND  ISNULL(rep.RepCargo, '') <> 'Supervisor'
    {wCentros}
    {wSuperv}
    {wReptype}
    {wCanales}
    {wReps}
),
VisitasCTE AS (
    SELECT  CAST(rv.FechaVisita AS DATE)      AS Fecha,
            SUM(ISNULL(rv.TotalAVisitar,0))   AS TotalVisitas,
            SUM(ISNULL(rv.Visitados,0))       AS TotalVisitados,
            COUNT(DISTINCT rv.RepCodigo)      AS NroReps
    FROM    ResumenVisitasRm rv
    INNER   JOIN RepsFiltrados rf ON rf.RepCodigo = rv.RepCodigo
    WHERE   CAST(rv.FechaVisita AS DATE) BETWEEN @fechaIni AND @fechaFin
    GROUP   BY CAST(rv.FechaVisita AS DATE)
),
PedidosCTE AS (
    SELECT  CAST(p.PedFecha AS DATE)          AS Fecha,
            COUNT(DISTINCT p.PedSecuencia)    AS Pedidos
    FROM    RepsFiltrados rf
    INNER   JOIN VWPedidos        p  ON  p.RepCodigo    = rf.RepCodigo
                                     AND CAST(p.PedFecha AS DATE) BETWEEN @fechaIni AND @fechaFin
    LEFT    JOIN VWPedidosDetalle pd ON  pd.PedSecuencia = p.PedSecuencia
                                     AND pd.RepCodigo    = p.RepCodigo
    LEFT    JOIN Productos        pr ON  pr.ProID        = pd.ProID
    LEFT    JOIN Categoria3       c3 ON  c3.Cat3ID       = pr.Cat3ID
    LEFT    JOIN Lineas           l  ON  l.LinID         = pr.LinID
    {wherePed}
    GROUP   BY CAST(p.PedFecha AS DATE)
)
SELECT
    ISNULL(v.Fecha, pe.Fecha)      AS Fecha,
    ISNULL(v.TotalVisitas,   0)    AS TotalVisitas,
    ISNULL(v.TotalVisitados, 0)    AS TotalVisitados,
    ISNULL(v.NroReps,        0)    AS NroReps,
    ISNULL(pe.Pedidos,       0)    AS PedidosRealizados
FROM   VisitasCTE v
FULL   OUTER JOIN PedidosCTE pe ON pe.Fecha = v.Fecha
ORDER  BY Fecha;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@fechaIni", fiStr);
            cmd.Parameters.AddWithValue("@fechaFin", ffStr);
            foreach (var (k, v) in repParms) cmd.Parameters.AddWithValue(k, v);
            foreach (var (k, v) in pedParms) cmd.Parameters.AddWithValue(k, v);

            var rows = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var f = rd.IsDBNull(0) ? (DateTime?)null : rd.GetDateTime(0);
                rows.Add(new
                {
                    Fecha             = f?.ToString("yyyy-MM-dd") ?? "",
                    TotalVisitas      = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd.GetValue(1)),
                    TotalVisitados    = rd.IsDBNull(2) ? 0 : Convert.ToInt32(rd.GetValue(2)),
                    NroReps           = rd.IsDBNull(3) ? 0 : Convert.ToInt32(rd.GetValue(3)),
                    PedidosRealizados = rd.IsDBNull(4) ? 0 : Convert.ToInt32(rd.GetValue(4))
                });
            }

            return Ok(new { ok = true, fechaIni = fiStr, fechaFin = ffStr, rows });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }

    // ── GET /api/filtros ───────────────────────────────────────────────────────
    [HttpGet("filtros")]
    public async Task<IActionResult> Filtros(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var fiStr = fi.ToString("yyyy-MM-dd");
        var ffStr = ff.ToString("yyyy-MM-dd");

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            // 1. Centros
            var centros = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT DISTINCT
                       ISNULL(cd.CedDescripcion, cd.CedCodigo) AS nombre, cd.CedCodigo AS valor
                FROM   ResumenVisitasRm rv
                INNER  JOIN Representantes      rep ON rep.RepCodigo = rv.RepCodigo
                LEFT   JOIN CentrosDistribucion cd  ON cd.CedCodigo  = rep.CedCodigo
                WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @fi AND @ff
                  AND  cd.CedCodigo IS NOT NULL
                ORDER  BY nombre", cn))
            {
                cmd.Parameters.AddWithValue("@fi", fiStr);
                cmd.Parameters.AddWithValue("@ff", ffStr);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    centros.Add(new { nombre = rd.IsDBNull(0) ? "" : rd.GetString(0),
                                      valor  = rd.IsDBNull(1) ? "" : rd.GetString(1) });
            }

            // 2. Supervisores
            var supervisores = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT DISTINCT
                       ISNULL(sup.RepNombre, ISNULL(rv.Supervisor, rv.codSupervisor)) AS nombre, rv.codSupervisor AS valor
                FROM   ResumenVisitasRm rv
                LEFT   JOIN Representantes sup ON sup.RepCodigo = rv.codSupervisor
                WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @fi AND @ff
                  AND  rv.codSupervisor IS NOT NULL
                ORDER  BY nombre", cn))
            {
                cmd.Parameters.AddWithValue("@fi", fiStr);
                cmd.Parameters.AddWithValue("@ff", ffStr);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    supervisores.Add(new { nombre = rd.IsDBNull(0) ? "" : rd.GetString(0),
                                           valor  = rd.IsDBNull(1) ? "" : rd.GetString(1) });
            }

            // 2b. Representantes (vendedores)
            var representantes = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT ISNULL(rep.RepNombre, MAX(rv.RepNombre)) + ' - ' + CAST(rv.RepCodigo AS NVARCHAR(20)) AS nombre,
                       rv.RepCodigo AS valor, MAX(rv.codSupervisor) AS sup, MAX(rep.CedCodigo) AS centro
                FROM   ResumenVisitasRm rv
                INNER  JOIN Representantes rep ON rep.RepCodigo = rv.RepCodigo
                WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @fi AND @ff
                  AND  rep.RepEstatus = 1 AND ISNULL(rep.RepCargo,'') <> 'Supervisor'
                GROUP  BY rep.RepNombre, rv.RepCodigo
                ORDER  BY nombre", cn))
            {
                cmd.Parameters.AddWithValue("@fi", fiStr);
                cmd.Parameters.AddWithValue("@ff", ffStr);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    representantes.Add(new { nombre = rd.IsDBNull(0) ? "" : rd.GetString(0),
                                             valor  = rd.IsDBNull(1) ? "" : rd.GetString(1),
                                             sup    = rd.IsDBNull(2) ? "" : Convert.ToString(rd.GetValue(2)) ?? "",      // P1: para que Vendedor dependa de Supervisor/Centro
                                             centro = rd.IsDBNull(3) ? "" : Convert.ToString(rd.GetValue(3)) ?? "" });
            }

            // 3. Suplidores
            var suplidores = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT DISTINCT
                       ISNULL(c3.Cat3Descripcion, CAST(c3.Cat3ID AS NVARCHAR(50))) AS nombre,
                       CAST(c3.Cat3ID AS NVARCHAR(50)) AS valor
                FROM   ResumenVisitasRm rv
                INNER  JOIN VWPedidos        p  ON  p.RepCodigo    = rv.RepCodigo
                                                AND CAST(p.PedFecha AS DATE) BETWEEN @fi AND @ff
                LEFT   JOIN VWPedidosDetalle pd ON  pd.PedSecuencia = p.PedSecuencia
                                                AND pd.RepCodigo    = p.RepCodigo
                LEFT   JOIN Productos        pr ON  pr.ProID        = pd.ProID
                LEFT   JOIN Categoria3       c3 ON  c3.Cat3ID       = pr.Cat3ID
                WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @fi AND @ff
                  AND  c3.Cat3ID IS NOT NULL
                ORDER  BY nombre", cn))
            {
                cmd.Parameters.AddWithValue("@fi", fiStr);
                cmd.Parameters.AddWithValue("@ff", ffStr);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    suplidores.Add(new { nombre = rd.IsDBNull(0) ? "" : rd.GetString(0),
                                         valor  = rd.IsDBNull(1) ? "" : rd.GetString(1) });
            }

            // 4. Lineas
            var lineas = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT DISTINCT
                       ISNULL(l.LinDescripcion, CAST(l.LinID AS NVARCHAR(50))) AS nombre,
                       CAST(l.LinID AS NVARCHAR(50)) AS valor
                FROM   ResumenVisitasRm rv
                INNER  JOIN VWPedidos        p  ON  p.RepCodigo    = rv.RepCodigo
                                                AND CAST(p.PedFecha AS DATE) BETWEEN @fi AND @ff
                LEFT   JOIN VWPedidosDetalle pd ON  pd.PedSecuencia = p.PedSecuencia
                                                AND pd.RepCodigo    = p.RepCodigo
                LEFT   JOIN Productos        pr ON  pr.ProID        = pd.ProID
                LEFT   JOIN Lineas           l  ON  l.LinID         = pr.LinID
                WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @fi AND @ff
                  AND  l.LinID IS NOT NULL
                ORDER  BY nombre", cn))
            {
                cmd.Parameters.AddWithValue("@fi", fiStr);
                cmd.Parameters.AddWithValue("@ff", ffStr);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    lineas.Add(new { nombre = rd.IsDBNull(0) ? "" : rd.GetString(0),
                                      valor  = rd.IsDBNull(1) ? "" : rd.GetString(1) });
            }

            // 5. Divisiones
            var divisiones = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT DISTINCT ISNULL(CAST(rv.RepType AS NVARCHAR(50)), '') AS nombre
                FROM   ResumenVisitasRm rv
                WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @fi AND @ff
                  AND  rv.RepType IS NOT NULL
                ORDER  BY nombre", cn))
            {
                cmd.Parameters.AddWithValue("@fi", fiStr);
                cmd.Parameters.AddWithValue("@ff", ffStr);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var nom = rd.IsDBNull(0) ? "" : rd.GetString(0);
                    if (!string.IsNullOrEmpty(nom))
                        divisiones.Add(new { nombre = nom, valor = nom });
                }
            }

            // 6. Canales de Ventas
            var canales = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT DISTINCT
                       ISNULL(cn.CanDescripcion, CAST(cn.CanID AS NVARCHAR(50))) AS nombre,
                       CAST(cn.CanID AS NVARCHAR(50)) AS valor
                FROM   ResumenVisitasRm rv
                INNER  JOIN Representantes            rep ON rep.RepCodigo = rv.RepCodigo
                INNER  JOIN RepresentantesAdicionales ra  ON ra.RepCodigo  = rep.RepCodigo
                INNER  JOIN CanalDistribucion         cn  ON cn.CanID      = ra.CanID
                WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @fi AND @ff
                ORDER  BY nombre", cn))
            {
                cmd.Parameters.AddWithValue("@fi", fiStr);
                cmd.Parameters.AddWithValue("@ff", ffStr);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    canales.Add(new { nombre = rd.IsDBNull(0) ? "" : rd.GetString(0),
                                      valor  = rd.IsDBNull(1) ? "" : rd.GetString(1) });
            }

            // 7. Tamaños de Cliente (Clasifica)
            var tamanos = new List<object>();
            await using (var cmd = new SqlCommand(@"
                SELECT ISNULL(ClaDescripcion, CAST(ClaID AS NVARCHAR(50))) AS nombre,
                       CAST(ClaID AS NVARCHAR(50)) AS valor
                FROM   Clasifica
                ORDER  BY nombre", cn))
            {
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    tamanos.Add(new { nombre = rd.IsDBNull(0) ? "" : rd.GetString(0),
                                      valor  = rd.IsDBNull(1) ? "" : rd.GetString(1) });
            }

            return Ok(new { ok = true, centros, supervisores, representantes, suplidores, lineas, divisiones, canales, tamanos });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }

    // ── GET /api/empresa ──────────────────────────────────────────────────────
    // Sin autenticacion — nombre comercial para la interfaz de usuario
    [HttpGet("empresa")]
    public async Task<IActionResult> Empresa()
    {
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT TOP 1 * FROM Empresa ORDER BY EmpID", cn);
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

    // ── GET /api/logo ──────────────────────────────────────────────────────────
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

    // ── GET /api/schema ────────────────────────────────────────────────────────
    [HttpGet("schema")]
    public async Task<IActionResult> Schema()
    {
        var tablas = new[] { "ResumenVisitasRm", "VWPedidos", "VWPedidosDetalle",
                             "Productos", "Categoria3", "Lineas", "RepresentantesAdicionales",
                             "RutaVisitasFechaHistoricos", "Clientes", "Clasifica" };
        var result = new Dictionary<string, object>();
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            foreach (var t in tablas)
            {
                try
                {
                    await using var cmd = new SqlCommand($"SELECT TOP 0 * FROM {t}", cn);
                    await using var rd  = await cmd.ExecuteReaderAsync();
                    result[t] = Enumerable.Range(0, rd.FieldCount)
                        .Select(i => new { nombre = rd.GetName(i), tipo = rd.GetFieldType(i).Name })
                        .ToList();
                }
                catch (Exception ex) { result[t] = new { error = ex.Message }; }
            }
            return Ok(result);
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }
}
