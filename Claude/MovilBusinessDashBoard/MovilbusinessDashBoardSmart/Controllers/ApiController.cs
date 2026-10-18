using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

[ApiController]
[Route("api")]
public class ApiController(IConfiguration config, LicenseService licencia) : ControllerBase
{
    private SqlConnection NewConn() =>
        new(config.GetConnectionString("Default"));

    private bool IsAuth => HttpContext.Session.GetString("user_login") is not null;

    // ── GET /api/me ────────────────────────────────────────────────────────────
    [HttpGet("me")]
    public IActionResult Me()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        return Ok(new
        {
            ok     = true,
            login  = HttpContext.Session.GetString("user_login"),
            nombre = HttpContext.Session.GetString("user_nombre")
        });
    }

    // ── GET /api/licencia ────────────────────────────────────────────────────────
    // Infraestructura compartida (NO se gatea) — el Menú la usa para ocultar las
    // tarjetas de los módulos apagados. No expone nada sensible: solo lo mismo que ya
    // se deduce usando la app (qué módulos están prendidos) + la fecha de vencimiento,
    // nunca la llave ni el contenido crudo de license.lic.
    [HttpGet("licencia")]
    public IActionResult Licencia()
    {
        var exp = licencia.Expira;
        return Ok(new
        {
            ok = true,
            valida = licencia.EsValida,
            expira = exp?.ToString("yyyy-MM-dd"),
            diasRestantes = exp is not null ? (exp.Value.ToDateTime(TimeOnly.MinValue) - DateTime.Today).Days : (int?)null,
            estado = licencia.Estado.ToString(),
            atadaAServidor = licencia.AtadaAServidor,
            huellaServidor = licencia.HuellaServidor,
            modulos = licencia.ModulosActivos()
        });
    }

    // ── GET /api/data?fecha=yyyy-MM-dd ─────────────────────────────────────────
    // Módulo "visitas" (el resto de este controller — me/empresa/logo/empresa-cols — es
    // infraestructura compartida por TODOS los módulos, nunca se gatea por licencia).
    [HttpGet("data")]
    [RequiereModulo("visitas")]
    public async Task<IActionResult> Data([FromQuery] string? fecha, [FromQuery] string? tipo)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        // Validar y normalizar fecha
        if (!DateOnly.TryParse(fecha, out var fechaDate))
            fechaDate = DateOnly.FromDateTime(DateTime.Today);

        var min = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var max = DateOnly.FromDateTime(DateTime.Today);
        if (fechaDate < min) fechaDate = min;
        if (fechaDate > max) fechaDate = max;

        var fechaStr = fechaDate.ToString("yyyy-MM-dd");

        // Filtro por tipo de visita: presencial='1', virtual='2', todos=sin filtro (valores controlados, sin inyección)
        var tf = tipo == "presencial" ? "AND VisTipoVisita='1'" : tipo == "virtual" ? "AND VisTipoVisita='2'" : "";

        var Sql = $@"
WITH Reps AS (
    SELECT DISTINCT
           v.RepCodigo,
           r.RepNombre + ' - ' + CAST(r.RepCodigo AS NVARCHAR(20)) AS RepNombre,
           r.RepSupervisor,
           ISNULL(sup.RepNombre, r.RepSupervisor) AS SupervisorNombre,
           r.CedCodigo,
           ISNULL(cd.CedDescripcion, r.CedCodigo) AS CentroNombre
    FROM   Visitas v
    INNER  JOIN Representantes r        ON r.RepCodigo  = v.RepCodigo
    LEFT   JOIN Representantes sup      ON sup.RepCodigo = r.RepSupervisor
    LEFT   JOIN CentrosDistribucion cd  ON cd.CedCodigo  = r.CedCodigo
    WHERE  v.VisFechaEntrada >= DATEADD(DAY, -30, GETDATE())
      AND  r.RepEstatus = 1
      AND  ISNULL(r.RepCargo, '') <> 'Supervisor'
),
DiaFirst AS (
    SELECT RepCodigo, MIN(VisFechaEntrada) AS PrimeraEntrada
    FROM   Visitas
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha {tf}
    GROUP  BY RepCodigo
),
DiaRanked AS (
    SELECT RepCodigo,
           VisFechaEntrada AS UltimaEntrada,
           VisFechaSalida  AS UltimaSalida,
           DATEDIFF(SECOND, VisFechaEntrada,
               ISNULL(VisFechaSalida, GETDATE())) AS DuracionSeg,
           ROW_NUMBER() OVER (
               PARTITION BY RepCodigo ORDER BY VisFechaEntrada DESC
           ) AS rn
    FROM   Visitas
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha {tf}
),
PrimerPedido AS (
    SELECT RepCodigo, MIN(VisFechaEntrada) AS PrimerPedidoHora
    FROM   Visitas
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha {tf}
      AND  VisEstatus = 2
    GROUP  BY RepCodigo
),
UltimoPedido AS (
    SELECT RepCodigo, MAX(VisFechaEntrada) AS UltimoPedidoHora
    FROM   Visitas
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha {tf}
      AND  VisEstatus = 2
    GROUP  BY RepCodigo
),
TiempoPromVisita AS (
    SELECT RepCodigo,
           AVG(DATEDIFF(SECOND, VisFechaEntrada, VisFechaSalida)) AS PromDurSeg
    FROM   Visitas
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha {tf}
      AND  DATEPART(HOUR, VisFechaEntrada) >= 8
      AND  DATEPART(HOUR, VisFechaEntrada) < 15
      AND  VisFechaSalida IS NOT NULL
    GROUP  BY RepCodigo
),
HoraCounts AS (
    SELECT RepCodigo,
           DATEPART(HOUR, VisFechaEntrada) AS Hora,
           COUNT(*) AS Cnt
    FROM   Visitas
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha {tf}
      AND  VisEstatus = 2
      AND  DATEPART(HOUR, VisFechaEntrada) >= 9
      AND  DATEPART(HOUR, VisFechaEntrada) < 15
    GROUP  BY RepCodigo, DATEPART(HOUR, VisFechaEntrada)
),
HoraMenorProd AS (
    SELECT RepCodigo, Hora,
           ROW_NUMBER() OVER (PARTITION BY RepCodigo ORDER BY Cnt ASC, Hora ASC) AS rn
    FROM   HoraCounts
),
HoraMayorProd AS (
    SELECT RepCodigo, Hora,
           ROW_NUMBER() OVER (PARTITION BY RepCodigo ORDER BY Cnt DESC, Hora ASC) AS rn
    FROM   HoraCounts
),
MaxGap AS (
    -- Mayor lapso entre dos pedidos efectivos consecutivos
    SELECT RepCodigo, GapInicio, GapFin, GapSeg,
           ROW_NUMBER() OVER (PARTITION BY RepCodigo ORDER BY GapSeg DESC) AS rn
    FROM (
        SELECT RepCodigo,
               LAG(VisFechaEntrada) OVER (PARTITION BY RepCodigo ORDER BY VisFechaEntrada) AS GapInicio,
               VisFechaEntrada AS GapFin,
               DATEDIFF(SECOND,
                   LAG(VisFechaEntrada) OVER (PARTITION BY RepCodigo ORDER BY VisFechaEntrada),
                   VisFechaEntrada) AS GapSeg
        FROM   Visitas
        WHERE  CAST(VisFechaEntrada AS DATE) = @fecha {tf}
          AND  VisEstatus = 2
    ) g
    WHERE GapInicio IS NOT NULL
),
Cuadre AS (
    -- Estatus del cuadre del dia: 2=Cerrado, 1=Abierto (CuaEstatus). Fechas basura (año 2085/1900) se excluyen.
    SELECT RepCodigo, MAX(CuaEstatus) AS CuaEstatus
    FROM   Cuadres WITH (NOLOCK)
    WHERE  CAST(CuaFechaInicio AS DATE) = @fecha AND CuaFechaInicio > '2000-01-01'
    GROUP  BY RepCodigo
)
SELECT r.RepCodigo,
       r.RepNombre,
       r.RepSupervisor,
       r.SupervisorNombre,
       r.CedCodigo,
       r.CentroNombre,
       df.PrimeraEntrada,
       dr.UltimaEntrada,
       dr.UltimaSalida,
       dr.DuracionSeg,
       pp.PrimerPedidoHora,
       up.UltimoPedidoHora,
       tpv.PromDurSeg,
       hmp.Hora  AS HoraMenorProd,
       hma.Hora  AS HoraMayorProd,
       mg.GapInicio AS MaxGapInicio,
       mg.GapFin    AS MaxGapFin,
       mg.GapSeg    AS MaxGapSeg,
       cu.CuaEstatus AS CuadreEstatus
FROM   Reps r
LEFT   JOIN DiaFirst        df  ON df.RepCodigo  = r.RepCodigo
LEFT   JOIN DiaRanked       dr  ON dr.RepCodigo  = r.RepCodigo AND dr.rn = 1
LEFT   JOIN PrimerPedido    pp  ON pp.RepCodigo  = r.RepCodigo
LEFT   JOIN UltimoPedido    up  ON up.RepCodigo  = r.RepCodigo
LEFT   JOIN TiempoPromVisita tpv ON tpv.RepCodigo = r.RepCodigo
LEFT   JOIN HoraMenorProd   hmp ON hmp.RepCodigo = r.RepCodigo AND hmp.rn = 1
LEFT   JOIN HoraMayorProd   hma ON hma.RepCodigo = r.RepCodigo AND hma.rn = 1
LEFT   JOIN MaxGap          mg  ON mg.RepCodigo  = r.RepCodigo AND mg.rn = 1
LEFT   JOIN Cuadre          cu  ON cu.RepCodigo  = r.RepCodigo
ORDER  BY r.RepNombre;";

        var SqlHoras = $@"
SELECT DATEPART(HOUR, VisFechaEntrada) AS Hora, COUNT(*) AS Total
FROM   Visitas
WHERE  CAST(VisFechaEntrada AS DATE) = @fecha {tf}
  AND  VisEstatus = 2
  AND  DATEPART(HOUR, VisFechaEntrada) >= 9
  AND  DATEPART(HOUR, VisFechaEntrada) < 15
GROUP  BY DATEPART(HOUR, VisFechaEntrada)
ORDER  BY Hora;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(Sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@fecha", fechaStr);

            var reps = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                // Formatear datetimes como ISO sin zona horaria (el JS los interpreta como hora local)
                static string? Fmt(SqlDataReader r, int i) =>
                    r.IsDBNull(i) ? null : r.GetDateTime(i).ToString("yyyy-MM-ddTHH:mm:ss");

                reps.Add(new
                {
                    RepCodigo        = rd.IsDBNull(0)  ? null : rd.GetString(0),
                    RepNombre        = rd.IsDBNull(1)  ? null : rd.GetString(1),
                    RepSupervisor    = rd.IsDBNull(2)  ? null : rd.GetString(2),
                    SupervisorNombre = rd.IsDBNull(3)  ? null : rd.GetString(3),
                    CedCodigo        = rd.IsDBNull(4)  ? null : rd.GetString(4),
                    CentroNombre     = rd.IsDBNull(5)  ? null : rd.GetString(5),
                    PrimeraEntrada   = Fmt(rd, 6),
                    UltimaEntrada    = Fmt(rd, 7),
                    UltimaSalida     = Fmt(rd, 8),
                    DuracionSeg      = rd.IsDBNull(9)  ? (int?)null : Convert.ToInt32(rd.GetValue(9)),
                    PrimerPedidoHora = Fmt(rd, 10),
                    UltimoPedidoHora = Fmt(rd, 11),
                    PromDurSeg       = rd.IsDBNull(12) ? (int?)null : Convert.ToInt32(rd.GetValue(12)),
                    HoraMenorProd    = rd.IsDBNull(13) ? (int?)null : Convert.ToInt32(rd.GetValue(13)),
                    HoraMayorProd    = rd.IsDBNull(14) ? (int?)null : Convert.ToInt32(rd.GetValue(14)),
                    MaxGapInicio     = Fmt(rd, 15),
                    MaxGapFin        = Fmt(rd, 16),
                    MaxGapSeg        = rd.IsDBNull(17) ? (int?)null : Convert.ToInt32(rd.GetValue(17)),
                    CuadreEstatus    = rd.IsDBNull(18) ? (int?)null : Convert.ToInt32(rd.GetValue(18))
                });
            }

            // Estadisticas globales de productividad por hora
            var horaStats = new List<object>();
            await using var cmd2 = new SqlCommand(SqlHoras, cn) { CommandTimeout = 30 };
            cmd2.Parameters.AddWithValue("@fecha", fechaStr);
            await using var rd2 = await cmd2.ExecuteReaderAsync();
            while (await rd2.ReadAsync())
                horaStats.Add(new { hora = Convert.ToInt32(rd2.GetValue(0)), total = Convert.ToInt32(rd2.GetValue(1)) });

            return Ok(new { ok = true, fecha = fechaStr, usuario = HttpContext.Session.GetString("user_nombre"), reps, horaStats });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }

    // ── GET /api/data-fecha?fechaIni=&fechaFin=&sups=&centros=&umbral= ──────────
    // Desglose POR FECHA: una fila por dia del rango con reps activos, visitas,
    // pedidos, prom 1ra visita, % a tiempo (segun umbral en minutos) y prom duracion.
    [HttpGet("data-fecha")]
    [RequiereModulo("visitas")]
    public async Task<IActionResult> DataFecha(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin,
        [FromQuery] string? sups,
        [FromQuery] string? centros,
        [FromQuery] int?    umbral,
        [FromQuery] string? tipo,
        [FromQuery] string? q,
        [FromQuery] string? reps)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        if (!DateOnly.TryParse(fechaIni, out var fi)) fi = DateOnly.FromDateTime(DateTime.Today);
        if (!DateOnly.TryParse(fechaFin, out var ff)) ff = DateOnly.FromDateTime(DateTime.Today);
        if (ff < fi) ff = fi;
        // Ventana maxima 90 dias hacia atras
        var minRange = DateOnly.FromDateTime(DateTime.Today.AddDays(-90));
        if (fi < minRange) fi = minRange;
        var maxRange = DateOnly.FromDateTime(DateTime.Today);
        if (ff > maxRange) ff = maxRange;

        var fiStr = fi.ToString("yyyy-MM-dd");
        var ffStr = ff.ToString("yyyy-MM-dd");
        var umbralMin = umbral ?? (8 * 60);   // por defecto 08:00

        static List<string> Parse(string? csv) =>
            string.IsNullOrWhiteSpace(csv) ? new() :
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var lSups    = Parse(sups);
        var lCentros = Parse(centros);
        var parms    = new Dictionary<string, string>();
        string Build(string col, List<string> vals, string pfx)
        {
            if (vals.Count == 0) return "";
            var ns = vals.Select((v, i) => { var n = $"@{pfx}{i}"; parms[n] = v; return n; });
            return $"AND {col} IN ({string.Join(",", ns)})";
        }
        var wSups    = Build("r.RepSupervisor", lSups,    "s");
        var wCentros = Build("r.CedCodigo",     lCentros, "c");
        var wReps    = Build("r.RepCodigo",     Parse(reps), "rp");
        // Filtro por tipo de visita: presencial='1', virtual='2', todos=sin filtro (valores controlados)
        var tf = tipo == "presencial" ? "AND v.VisTipoVisita='1'" : tipo == "virtual" ? "AND v.VisTipoVisita='2'" : "";
        // Busqueda por nombre o codigo del representante (parametrizada)
        var wQ = "";
        if (!string.IsNullOrWhiteSpace(q)) { parms["@q"] = "%" + q.Trim() + "%"; wQ = "AND (r.RepNombre LIKE @q OR CAST(r.RepCodigo AS NVARCHAR(20)) LIKE @q)"; }

        var sql = $@"
WITH DiaRep AS (
    -- Primera y ultima entrada por rep por dia (reps con al menos una visita ese dia)
    SELECT CAST(v.VisFechaEntrada AS DATE) AS Fecha,
           v.RepCodigo,
           MIN(v.VisFechaEntrada)          AS PrimeraEntrada,
           MAX(v.VisFechaEntrada)          AS UltimaEntrada
    FROM   Visitas v
    INNER  JOIN Representantes r ON r.RepCodigo = v.RepCodigo
    WHERE  CAST(v.VisFechaEntrada AS DATE) BETWEEN @fi AND @ff
      AND  r.RepEstatus = 1
      AND  ISNULL(r.RepCargo, '') <> 'Supervisor'
    {wSups}
    {wCentros}
    {wReps}
    {tf}
    {wQ}
    GROUP  BY CAST(v.VisFechaEntrada AS DATE), v.RepCodigo
),
Agg AS (
    SELECT CAST(v.VisFechaEntrada AS DATE) AS Fecha,
           COUNT(*)                                                    AS Visitas,
           SUM(CASE WHEN v.VisEstatus = 2 THEN 1 ELSE 0 END)           AS Pedidos,
           AVG(CASE WHEN v.VisFechaSalida IS NOT NULL
                     AND DATEPART(HOUR, v.VisFechaEntrada) >= 8
                     AND DATEPART(HOUR, v.VisFechaEntrada) < 15
                    THEN DATEDIFF(SECOND, v.VisFechaEntrada, v.VisFechaSalida) END) AS PromDurSeg
    FROM   Visitas v
    INNER  JOIN Representantes r ON r.RepCodigo = v.RepCodigo
    WHERE  CAST(v.VisFechaEntrada AS DATE) BETWEEN @fi AND @ff
      AND  r.RepEstatus = 1
      AND  ISNULL(r.RepCargo, '') <> 'Supervisor'
    {wSups}
    {wCentros}
    {wReps}
    {tf}
    {wQ}
    GROUP  BY CAST(v.VisFechaEntrada AS DATE)
),
DiaAgg AS (
    SELECT Fecha,
           COUNT(*) AS RepsActivos,
           AVG(DATEPART(HOUR, PrimeraEntrada) * 3600
             + DATEPART(MINUTE, PrimeraEntrada) * 60
             + DATEPART(SECOND, PrimeraEntrada)) AS PromPrimVisSec,
           SUM(CASE WHEN DATEPART(HOUR, PrimeraEntrada) * 60
                       + DATEPART(MINUTE, PrimeraEntrada) < @umbral
                    THEN 1 ELSE 0 END) AS ATiempo,
           AVG(DATEDIFF(SECOND, PrimeraEntrada, UltimaEntrada)) AS PromTotalTpoSeg,
           AVG(DATEPART(HOUR, UltimaEntrada) * 3600
             + DATEPART(MINUTE, UltimaEntrada) * 60
             + DATEPART(SECOND, UltimaEntrada)) AS PromUltVisSec
    FROM   DiaRep
    GROUP  BY Fecha
)
SELECT a.Fecha,
       da.RepsActivos,
       a.Visitas,
       a.Pedidos,
       ISNULL(a.PromDurSeg, 0)      AS PromDurSeg,
       ISNULL(da.PromPrimVisSec,0)  AS PromPrimVisSec,
       da.ATiempo,
       ISNULL(da.PromTotalTpoSeg,0) AS PromTotalTpoSeg,
       ISNULL(da.PromUltVisSec,0)   AS PromUltVisSec
FROM   Agg a
INNER  JOIN DiaAgg da ON da.Fecha = a.Fecha
ORDER  BY a.Fecha;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@fi", fiStr);
            cmd.Parameters.AddWithValue("@ff", ffStr);
            cmd.Parameters.AddWithValue("@umbral", umbralMin);
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);

            var rows = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var f = rd.IsDBNull(0) ? (DateTime?)null : rd.GetDateTime(0);
                rows.Add(new
                {
                    Fecha          = f?.ToString("yyyy-MM-dd") ?? "",
                    RepsActivos    = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd.GetValue(1)),
                    Visitas        = rd.IsDBNull(2) ? 0 : Convert.ToInt32(rd.GetValue(2)),
                    Pedidos        = rd.IsDBNull(3) ? 0 : Convert.ToInt32(rd.GetValue(3)),
                    PromDurSeg     = rd.IsDBNull(4) ? 0 : Convert.ToInt32(rd.GetValue(4)),
                    PromPrimVisSec = rd.IsDBNull(5) ? 0 : Convert.ToInt32(rd.GetValue(5)),
                    ATiempo        = rd.IsDBNull(6) ? 0 : Convert.ToInt32(rd.GetValue(6)),
                    PromTotalTpoSeg= rd.IsDBNull(7) ? 0 : Convert.ToInt32(rd.GetValue(7)),
                    PromUltVisSec  = rd.IsDBNull(8) ? 0 : Convert.ToInt32(rd.GetValue(8))
                });
            }
            return Ok(new { ok = true, fechaIni = fiStr, fechaFin = ffStr, rows });
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

            // Intentar varias columnas de logo
            foreach (var col in new[] { "EmpLogo", "EmpImagen", "EmpFoto", "Logo" })
            {
                SqlCommand? cmd = null;
                try
                {
                    cmd = new SqlCommand($"SELECT TOP 1 [{col}] FROM Empresa WHERE [{col}] IS NOT NULL ORDER BY EmpID", cn);
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync() || rd.IsDBNull(0)) continue;

                    var data = (byte[])rd.GetValue(0);
                    if (data.Length < 4) continue;

                    // Detectar tipo por magic bytes
                    var mime = (data[0], data[1]) switch
                    {
                        (0xFF, 0xD8)             => "image/jpeg",
                        (0x89, 0x50)             => "image/png",
                        (0x47, 0x49)             => "image/gif",
                        (0x42, 0x4D)             => "image/bmp",
                        _                        => "image/jpeg"
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

    // ── GET /api/empresa-cols  (diagnostico — borrar en produccion) ────────────
    [HttpGet("empresa-cols")]
    public async Task<IActionResult> EmpresaCols()
    {
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            await using var cmd = new SqlCommand("SELECT TOP 1 * FROM Empresa", cn);
            await using var rd  = await cmd.ExecuteReaderAsync();

            var cols = new List<object>();
            for (var i = 0; i < rd.FieldCount; i++)
            {
                cols.Add(new
                {
                    nombre = rd.GetName(i),
                    tipo   = rd.GetFieldType(i).Name,
                    valor  = rd.HasRows && await rd.ReadAsync() && !rd.IsDBNull(i)
                             ? rd.GetValue(i)?.ToString()?[..Math.Min(60, rd.GetValue(i)?.ToString()?.Length ?? 0)]
                             : null
                });
            }
            return Ok(cols);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
