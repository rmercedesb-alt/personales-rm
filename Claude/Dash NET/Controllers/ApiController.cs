using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DashVisitas.Controllers;

[ApiController]
[Route("api")]
public class ApiController(IConfiguration config) : ControllerBase
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

    // ── GET /api/data?fecha=yyyy-MM-dd ─────────────────────────────────────────
    [HttpGet("data")]
    public async Task<IActionResult> Data([FromQuery] string? fecha)
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

        const string Sql = @"
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
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha
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
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha
),
PrimerPedido AS (
    SELECT RepCodigo, MIN(VisFechaEntrada) AS PrimerPedidoHora
    FROM   Visitas
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha
      AND  VisEstatus = 2
    GROUP  BY RepCodigo
),
UltimoPedido AS (
    SELECT RepCodigo, MAX(VisFechaEntrada) AS UltimoPedidoHora
    FROM   Visitas
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha
      AND  VisEstatus = 2
    GROUP  BY RepCodigo
),
TiempoPromVisita AS (
    SELECT RepCodigo,
           AVG(DATEDIFF(SECOND, VisFechaEntrada, VisFechaSalida)) AS PromDurSeg
    FROM   Visitas
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha
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
    WHERE  CAST(VisFechaEntrada AS DATE) = @fecha
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
        WHERE  CAST(VisFechaEntrada AS DATE) = @fecha
          AND  VisEstatus = 2
    ) g
    WHERE GapInicio IS NOT NULL
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
       mg.GapSeg    AS MaxGapSeg
FROM   Reps r
LEFT   JOIN DiaFirst        df  ON df.RepCodigo  = r.RepCodigo
LEFT   JOIN DiaRanked       dr  ON dr.RepCodigo  = r.RepCodigo AND dr.rn = 1
LEFT   JOIN PrimerPedido    pp  ON pp.RepCodigo  = r.RepCodigo
LEFT   JOIN UltimoPedido    up  ON up.RepCodigo  = r.RepCodigo
LEFT   JOIN TiempoPromVisita tpv ON tpv.RepCodigo = r.RepCodigo
LEFT   JOIN HoraMenorProd   hmp ON hmp.RepCodigo = r.RepCodigo AND hmp.rn = 1
LEFT   JOIN HoraMayorProd   hma ON hma.RepCodigo = r.RepCodigo AND hma.rn = 1
LEFT   JOIN MaxGap          mg  ON mg.RepCodigo  = r.RepCodigo AND mg.rn = 1
ORDER  BY r.RepNombre;";

        const string SqlHoras = @"
SELECT DATEPART(HOUR, VisFechaEntrada) AS Hora, COUNT(*) AS Total
FROM   Visitas
WHERE  CAST(VisFechaEntrada AS DATE) = @fecha
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
                    MaxGapSeg        = rd.IsDBNull(17) ? (int?)null : Convert.ToInt32(rd.GetValue(17))
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
