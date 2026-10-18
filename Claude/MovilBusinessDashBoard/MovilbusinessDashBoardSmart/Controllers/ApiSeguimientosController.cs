using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

// ── API del modulo Seguimientos Semanales (Torre de Control / IEGT) ─────────────
// Devuelve METRICAS CRUDAS por vendedor de la semana; el IEGT/bandas/conclusiones
// se calculan en el frontend con la configuracion editable (recalibrar = instantaneo).
[ApiController]
[Route("apiseg")]
[RequiereModulo("seguimientos")]
public class ApiSeguimientosController(IConfiguration config) : ControllerBase
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));
    private bool IsAuth => HttpContext.Session.GetString("user_login") is not null;

    private static List<string> ParseList(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? new() :
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string BuildIn(string column, List<string> vals, string prefix, Dictionary<string, string> parms)
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

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (!IsAuth) return Unauthorized(new { ok = false });
        return Ok(new { ok = true, login = HttpContext.Session.GetString("user_login"), nombre = HttpContext.Session.GetString("user_nombre") });
    }

    // ── GET /apiseg/branding ── apodo del IEGT (ConfiguracionApp_RM) ─────────────
    [HttpGet("branding")]
    public IActionResult Branding()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        string apodo = "";
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand("SELECT TOP 1 Valor FROM dbo.ConfiguracionApp_RM WITH (NOLOCK) WHERE Clave='IegtApodo'", cn);
            var o = cmd.ExecuteScalar();
            if (o is string s) apodo = s.Trim();
        }
        catch { apodo = ""; }
        return Ok(new { ok = true, iegtApodo = apodo });
    }

    // ── GET /apiseg/filtros ─────────────────────────────────────────────────────
    // Jerarquia de representantes para drill-down y filtros dependientes.
    [HttpGet("filtros")]
    public async Task<IActionResult> Filtros()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        const string Sql = @"
SELECT  r.RepCodigo, r.RepNombre,
        ISNULL(r.RepCargo,'')                   AS RepCargo,
        ISNULL(r.CedCodigo,'')                  AS CedCodigo,
        ISNULL(cd.CedDescripcion, r.CedCodigo)  AS CentroNombre,
        ISNULL(r.RepSupervisor,'')              AS SupervisorCod,
        ISNULL(sup.RepNombre, r.RepSupervisor)  AS SupervisorNombre,
        ISNULL(ra.RepTipe,'')                   AS Division
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
            return Ok(new { ok = true, representantes = reps });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Fragmento SQL: "AND NOT EXISTS (el cliente está en la ruta del vendedor en el mismo día de la semana)".
    // RutaVisitas guarda 5 columnas RutSemana1..5 (semanas del ciclo) de 7 caracteres, uno por día (posición 1 = lunes …
    // 7 = domingo; "0100000" = martes — verificado contra RutaVisitasFechaHistoricos). 1900-01-01 fue lunes, por eso
    // DATEDIFF(DAY,'19000101',día) % 7 + 1 da la posición del día sin depender de DATEFIRST.
    private static string DiaSemanaEnRuta(string rep, string cli, string dia)
    {
        var pos = $"(DATEDIFF(DAY,'19000101',{dia}) % 7) + 1";
        return $@"AND NOT EXISTS (SELECT 1 FROM RutaVisitas rvd WITH (NOLOCK)
                      WHERE rvd.RepCodigo = {rep} AND rvd.CliID = {cli}
                        AND '1' IN (SUBSTRING(rvd.RutSemana1,{pos},1), SUBSTRING(rvd.RutSemana2,{pos},1), SUBSTRING(rvd.RutSemana3,{pos},1),
                                    SUBSTRING(rvd.RutSemana4,{pos},1), SUBSTRING(rvd.RutSemana5,{pos},1)))";
    }

    // ── GET /apiseg/semana ──────────────────────────────────────────────────────
    // Metricas crudas por vendedor para la semana (lun-vie). Params: fechaIni, fechaFin,
    // centros, supervisores, reps, divisiones, cargos (CSV); pdvMetros (umbral fuera de PDV).
    [HttpGet("semana")]
    public async Task<IActionResult> Semana(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? reps,
        [FromQuery] string? divisiones,
        [FromQuery] string? cargos,
        [FromQuery] int?    pdvMetros,
        [FromQuery] string? pdvModo,
        [FromQuery] string? tipoVisita)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var liStr = fi.ToString("yyyy-MM-dd");
        var lfStr = ff.ToString("yyyy-MM-dd");
        var pdv = pdvMetros is > 0 and <= 5000 ? pdvMetros.Value : 200;
        // 'ruta' = visitas a clientes NO programados ese dia (indicador fuera de ruta); si no, por distancia GPS.
        var esPdvRuta = string.Equals(pdvModo, "ruta", StringComparison.OrdinalIgnoreCase);

        var lDiv = ParseList(divisiones);
        var parms = new Dictionary<string, string>();
        var wCentros = BuildIn("r.CedCodigo",     ParseList(centros),      "c",  parms);
        var wSups    = BuildIn("r.RepSupervisor", ParseList(supervisores), "s",  parms);
        var wReps    = BuildIn("r.RepCodigo",     ParseList(reps),         "rp", parms);
        var wCargo   = BuildIn("r.RepCargo",      ParseList(cargos),       "cg", parms);
        var wDiv     = BuildIn("ra.RepTipe",      lDiv,                    "dv", parms);
        var divJoin  = lDiv.Count > 0 ? "INNER JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = r.RepCodigo" : "";
        var repWhere = $"r.RepEstatus = 1 AND ISNULL(r.RepCargo,'') <> 'Supervisor' {wCentros} {wSups} {wReps} {wCargo} {wDiv}";

        // "Fuera de ruta" (2026-10-06, pedido del usuario): NO cuenta si el cliente es de la ruta del vendedor en ESE
        // MISMO DÍA DE LA SEMANA (ej. un cliente de los martes visitado un martes aunque no tocara esa semana del ciclo);
        // sí cuenta si el cliente es de otro día calendario de su ruta o no está en su ruta.
        var exFR = DiaSemanaEnRuta("d.RepCodigo", "d.CliID", "d.Dia");
        var exFP = DiaSemanaEnRuta("v.RepCodigo", "v.CliID", "CAST(v.VisFechaEntrada AS DATE)");

        // tipoVisita=presencial (2026-10-09, comparación pedida por el usuario): mide el AMCO-GT SOLO con visitas presenciales
        // (Visitas.VisTipoVisita='1'; '2' = virtual). Por defecto (todas) NADA cambia. En modo presencial: Inicio/Cierre (hora del
        // primer/último pedido), Ritmo, Fuera de PDV, sin venta e inventarios salen solo de visitas presenciales; los pedidos que nacieron
        // de una visita virtual no cuentan (Efectividad y Fuera de ruta); y 'Visitados' (Cumplimiento) = clientes distintos con visita
        // presencial por día (ResumenVisitasRm.Visitados incluye las virtuales).
        var soloPres = string.Equals(tipoVisita, "presencial", StringComparison.OrdinalIgnoreCase);
        var vP = soloPres ? " AND v.VisTipoVisita = '1'" : "";
        var exVirtP = soloPres ? " AND NOT EXISTS (SELECT 1 FROM Visitas vx WITH (NOLOCK) WHERE vx.RepCodigo = p.RepCodigo AND vx.VisSecuencia = p.VisSecuencia AND vx.VisTipoVisita = '2')" : "";
        var rvVisitados = soloPres ? "SUM(ISNULL(pd.N,0))" : "SUM(ISNULL(rv.Visitados,0))";
        var rvJoin = soloPres ? "LEFT JOIN (SELECT v.RepCodigo, CAST(v.VisFechaEntrada AS DATE) AS Dia, COUNT(DISTINCT v.CliID) AS N FROM Visitas v WITH (NOLOCK) WHERE CAST(v.VisFechaEntrada AS DATE) BETWEEN @li AND @lf AND v.VisTipoVisita = '1' GROUP BY v.RepCodigo, CAST(v.VisFechaEntrada AS DATE)) pd ON pd.RepCodigo = rv.RepCodigo AND pd.Dia = CAST(rv.FechaVisita AS DATE)" : "";

        var sql = $@"
;WITH Reps AS (
    SELECT DISTINCT r.RepCodigo, r.RepNombre, ISNULL(r.RepCargo,'') AS Cargo,
           ISNULL(r.CedCodigo,'') AS CedCodigo, ISNULL(cd.CedDescripcion, r.CedCodigo) AS Centro,
           ISNULL(r.RepSupervisor,'') AS SupCod, ISNULL(sup.RepNombre, r.RepSupervisor) AS Supervisor,
           ISNULL(rad.RepTipe,'') AS Division, ISNULL(rad.CanID,0) AS CanID
    FROM   Representantes r WITH (NOLOCK)
    {divJoin}
    LEFT   JOIN Representantes sup           WITH (NOLOCK) ON sup.RepCodigo = r.RepSupervisor
    LEFT   JOIN CentrosDistribucion cd       WITH (NOLOCK) ON cd.CedCodigo  = r.CedCodigo
    LEFT   JOIN RepresentantesAdicionales rad WITH (NOLOCK) ON rad.RepCodigo = r.RepCodigo
    WHERE  {repWhere}
),
Rv AS (
    SELECT rv.RepCodigo, SUM(ISNULL(rv.TotalAVisitar,0)) AS TotVis, {rvVisitados} AS Visitados
    FROM   ResumenVisitasRm rv WITH (NOLOCK)
    {rvJoin}
    INNER  JOIN Reps rp ON rp.RepCodigo = rv.RepCodigo
    WHERE  CAST(rv.FechaVisita AS DATE) BETWEEN @li AND @lf
    GROUP  BY rv.RepCodigo
),
Ped AS (
    SELECT p.RepCodigo, COUNT(DISTINCT p.PedSecuencia) AS Pedidos, COUNT(DISTINCT p.CliID) AS CliConPed
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN Reps rp ON rp.RepCodigo = p.RepCodigo
    WHERE  CAST(p.PedFecha AS DATE) BETWEEN @li AND @lf{exVirtP}
    GROUP  BY p.RepCodigo
),
FueraRuta AS (
    SELECT d.RepCodigo, COUNT(1) AS N
    FROM ( SELECT DISTINCT p.RepCodigo, p.CliID, CAST(p.PedFecha AS DATE) AS Dia
           FROM VWPedidos p WITH (NOLOCK) INNER JOIN Reps rp ON rp.RepCodigo = p.RepCodigo
           WHERE CAST(p.PedFecha AS DATE) BETWEEN @li AND @lf{exVirtP} ) d
    WHERE NOT EXISTS (SELECT 1 FROM RutaVisitasFechaHistoricos ru WITH (NOLOCK)
                      WHERE ru.RepCodigo = d.RepCodigo AND ru.CliID = d.CliID AND CAST(ru.RutFecha AS DATE) = d.Dia)
      {exFR}
    GROUP BY d.RepCodigo
),
VisDia AS (
    SELECT v.RepCodigo, CAST(v.VisFechaEntrada AS DATE) AS Dia,
           MIN(v.VisFechaEntrada) AS PrimeraVisita,
           MIN(CASE WHEN v.VisEstatus = 2 THEN v.VisFechaEntrada END) AS PrimerPed,
           MAX(CASE WHEN v.VisEstatus = 2 THEN v.VisFechaEntrada END) AS UltimoPed
    FROM   Visitas v WITH (NOLOCK) INNER JOIN Reps rp ON rp.RepCodigo = v.RepCodigo
    WHERE  CAST(v.VisFechaEntrada AS DATE) BETWEEN @li AND @lf{vP}
    GROUP  BY v.RepCodigo, CAST(v.VisFechaEntrada AS DATE)
),
Horas AS (
    -- PrimerMin/UltimoMin: promedio solo de los dias CON pedido (igual que siempre).
    -- PrimeraVisitaMin (nuevo, AMCO-GT Mayorista): promedio de TODOS los dias con
    -- alguna visita, tenga pedido o no (Mayorista mide la visita, no el pedido).
    SELECT RepCodigo,
           AVG(CASE WHEN PrimerPed IS NOT NULL THEN DATEDIFF(MINUTE, CAST(Dia AS datetime), PrimerPed) END) AS PrimerMin,
           AVG(CASE WHEN UltimoPed IS NOT NULL THEN DATEDIFF(MINUTE, CAST(Dia AS datetime), UltimoPed) END) AS UltimoMin,
           AVG(DATEDIFF(MINUTE, CAST(Dia AS datetime), PrimeraVisita)) AS PrimeraVisitaMin
    FROM   VisDia GROUP BY RepCodigo
),
InvCli AS (
    -- AMCO-GT Mayorista, indicador 5: % de visitas de la semana que tienen un
    -- inventario físico levantado (VwInventarioFisico, unido por RepCodigo+
    -- VisSecuencia — confirmado con el usuario 2026-09-04, NO es InventariosClientes).
    SELECT v.RepCodigo,
           COUNT(DISTINCT v.VisSecuencia) AS VisitasSemana,
           COUNT(DISTINCT CASE WHEN inv.VisSecuencia IS NOT NULL THEN v.VisSecuencia END) AS VisitasConInventario
    FROM   Visitas v WITH (NOLOCK) INNER JOIN Reps rp ON rp.RepCodigo = v.RepCodigo
    LEFT   JOIN VwInventarioFisico inv WITH (NOLOCK) ON inv.RepCodigo = v.RepCodigo AND inv.VisSecuencia = v.VisSecuencia
    WHERE  CAST(v.VisFechaEntrada AS DATE) BETWEEN @li AND @lf{vP}
    GROUP  BY v.RepCodigo
),
Tiempo AS (
    SELECT v.RepCodigo, AVG(DATEDIFF(SECOND, v.VisFechaEntrada, v.VisFechaSalida)) AS PromSeg
    FROM   Visitas v WITH (NOLOCK) INNER JOIN Reps rp ON rp.RepCodigo = v.RepCodigo
    WHERE  CAST(v.VisFechaEntrada AS DATE) BETWEEN @li AND @lf{vP} AND v.VisFechaSalida IS NOT NULL
      AND  DATEPART(HOUR, v.VisFechaEntrada) >= 8 AND DATEPART(HOUR, v.VisFechaEntrada) < 15
    GROUP  BY v.RepCodigo
),
FueraPDV AS (
{(esPdvRuta ? @"
    -- Modo 'ruta': visitas a clientes que NO estaban programados ese dia (fuera de ruta)
    SELECT v.RepCodigo, COUNT(1) AS N
    FROM   Visitas v WITH (NOLOCK) INNER JOIN Reps rp ON rp.RepCodigo = v.RepCodigo
    WHERE  CAST(v.VisFechaEntrada AS DATE) BETWEEN @li AND @lf" + vP + @"
      AND  NOT EXISTS (SELECT 1 FROM RutaVisitasFechaHistoricos ru WITH (NOLOCK)
                       WHERE ru.RepCodigo = v.RepCodigo AND ru.CliID = v.CliID
                         AND CAST(ru.RutFecha AS DATE) = CAST(v.VisFechaEntrada AS DATE))" + exFP + @"
    GROUP  BY v.RepCodigo" : @"
    -- Modo 'distancia': captura GPS lejos del cliente ( > @pdv metros )
    SELECT v.RepCodigo, COUNT(1) AS N
    FROM   Visitas v WITH (NOLOCK) INNER JOIN Reps rp ON rp.RepCodigo = v.RepCodigo
    INNER  JOIN Clientes c WITH (NOLOCK) ON c.CliID = v.CliID
    WHERE  CAST(v.VisFechaEntrada AS DATE) BETWEEN @li AND @lf" + vP + @"
      AND  v.VisLatitud IS NOT NULL AND v.VisLatitud <> 0 AND c.CliLatitud IS NOT NULL AND c.CliLatitud <> 0
      AND  geography::Point(CAST(v.VisLatitud AS float), CAST(v.VisLongitud AS float), 4326)
              .STDistance(geography::Point(CAST(c.CliLatitud AS float), CAST(c.CliLongitud AS float), 4326)) > @pdv
    GROUP  BY v.RepCodigo")}
),
Visitados AS (
    SELECT vv.RepCodigo, COUNT(1) AS SinVenta
    FROM ( SELECT DISTINCT v.RepCodigo, v.CliID FROM Visitas v WITH (NOLOCK)
           INNER JOIN Reps rp ON rp.RepCodigo = v.RepCodigo
           WHERE CAST(v.VisFechaEntrada AS DATE) BETWEEN @li AND @lf{vP} ) vv
    WHERE NOT EXISTS (SELECT 1 FROM VWPedidos p WITH (NOLOCK)
                      WHERE p.RepCodigo = vv.RepCodigo AND p.CliID = vv.CliID
                        AND CAST(p.PedFecha AS DATE) BETWEEN @li AND @lf)
    GROUP BY vv.RepCodigo
),
Ventas AS (
    -- Volumen del vendedor: Unidades (PedCantidad), Cajas (PedCantidad/ProUnidades), Valor (PedCantidad*PedPrecio)
    -- Join directo a Representantes (no al CTE Reps) para que el optimizador use el plan rapido.
    SELECT p.RepCodigo,
           SUM(pd.PedCantidad)                                   AS Unidades,
           SUM(pd.PedCantidad / NULLIF(pr.ProUnidades,0))        AS Cajas,
           SUM(pd.PedCantidad * pd.PedPrecio)                    AS Valor
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = p.RepCodigo
    {divJoin}
    INNER  JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia = p.PedSecuencia AND pd.RepCodigo = p.RepCodigo
    INNER  JOIN Productos pr WITH (NOLOCK) ON pr.ProID = pd.ProID
    WHERE  {repWhere} AND CAST(p.PedFecha AS DATE) BETWEEN @li AND @lf
    GROUP  BY p.RepCodigo
)
SELECT rp.RepCodigo, rp.RepNombre, rp.Cargo, rp.CedCodigo, rp.Centro, rp.SupCod, rp.Supervisor, rp.Division, rp.CanID,
       ISNULL(rv.TotVis,0)     AS TotVis,   ISNULL(rv.Visitados,0) AS Visitados,
       ISNULL(pe.Pedidos,0)    AS Pedidos,  ISNULL(pe.CliConPed,0) AS CliConPed,
       ISNULL(fr.N,0)          AS FueraRuta,
       h.PrimerMin, h.UltimoMin, h.PrimeraVisitaMin, t.PromSeg,
       ISNULL(fp.N,0)          AS FueraPDV, ISNULL(sv.SinVenta,0)  AS SinVenta,
       ISNULL(vt.Unidades,0)   AS Unidades, ISNULL(vt.Cajas,0)     AS Cajas, ISNULL(vt.Valor,0) AS Valor,
       ISNULL(ic.VisitasSemana,0) AS VisitasSemana, ISNULL(ic.VisitasConInventario,0) AS VisitasConInventario
FROM   Reps rp
INNER  JOIN Rv rv ON rv.RepCodigo = rp.RepCodigo      -- solo reps con actividad en la semana
LEFT   JOIN Ped        pe ON pe.RepCodigo = rp.RepCodigo
LEFT   JOIN FueraRuta  fr ON fr.RepCodigo = rp.RepCodigo
LEFT   JOIN Horas      h  ON h.RepCodigo  = rp.RepCodigo
LEFT   JOIN Tiempo     t  ON t.RepCodigo  = rp.RepCodigo
LEFT   JOIN FueraPDV   fp ON fp.RepCodigo = rp.RepCodigo
LEFT   JOIN Visitados  sv ON sv.RepCodigo = rp.RepCodigo
LEFT   JOIN Ventas     vt ON vt.RepCodigo = rp.RepCodigo
LEFT   JOIN InvCli     ic ON ic.RepCodigo = rp.RepCodigo
ORDER  BY rp.Centro, rp.Supervisor, rp.RepNombre;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
            cmd.Parameters.AddWithValue("@li", liStr);
            cmd.Parameters.AddWithValue("@lf", lfStr);
            cmd.Parameters.AddWithValue("@pdv", pdv);
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);

            // AMCO-GT — "Logro Pre Ventas" (Minorista/Prevendedor) y "Cumple presupuesto
            // de ventas" (Mayorista): presupuesto/ejecutado del vendedor (MNTDCAT1DIA,
            // mismo PreTipo/patrón que ya usa "Cobros del día" — ver
            // ApiSupervisionController.ValoresPresupuesto) sumado sobre TODO el rango de
            // fechas de la semana (puede cruzar mes). Consulta aparte porque
            // PresupuestosHistoricos se particiona por Año/Mes, no admite un simple
            // BETWEEN de fechas como el resto de las CTEs de arriba.
            var presupSemana = ValoresPresupuestoRango(cn, fi, ff);

            var vendedores = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                int? IntN(int i) => rd.IsDBNull(i) ? null : Convert.ToInt32(rd.GetValue(i));
                var repCodigo = rd.GetString(0);
                presupSemana.TryGetValue(repCodigo, out var presup);
                vendedores.Add(new
                {
                    RepCodigo   = repCodigo,
                    RepNombre   = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    Cargo       = rd.GetString(2),
                    CedCodigo   = rd.GetString(3),
                    Centro      = rd.GetString(4),
                    SupCod      = rd.GetString(5),
                    Supervisor  = rd.GetString(6),
                    Division    = rd.GetString(7),
                    CanID       = rd.GetInt32(8),
                    TotVis      = rd.GetInt32(9),
                    Visitados   = rd.GetInt32(10),
                    Pedidos     = rd.GetInt32(11),
                    CliConPed   = rd.GetInt32(12),
                    FueraRuta   = rd.GetInt32(13),
                    PrimerMin   = IntN(14),
                    UltimoMin   = IntN(15),
                    PrimeraVisitaMin = IntN(16),
                    PromSeg     = IntN(17),
                    FueraPDV    = rd.GetInt32(18),
                    SinVenta    = rd.GetInt32(19),
                    Unidades    = Convert.ToDouble(rd.GetValue(20)),
                    Cajas       = Math.Round(Convert.ToDouble(rd.GetValue(21)), 1),
                    Valor       = Convert.ToDouble(rd.GetValue(22)),
                    VisitasSemana        = rd.GetInt32(23),
                    VisitasConInventario = rd.GetInt32(24),
                    PresupuestoSemana = Math.Round(presup.presu),
                    EjecutadoSemana   = Math.Round(presup.ejec)
                });
            }
            return Ok(new { ok = true, fechaIni = liStr, fechaFin = lfStr, pdvMetros = pdv, pdvModo = esPdvRuta ? "ruta" : "distancia", vendedores });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Presupuesto/Ejecutado del vendedor (MNTDCAT1DIA, todas las dimensiones sumadas —
    // mismo patrón que ApiSupervisionController.ValoresPresupuesto, "Cobros del día")
    // para un RANGO de fechas que puede cruzar de un mes a otro (PresupuestosHistoricos
    // se particiona por Año/Mes — se consulta 1 vez por cada mes que toque el rango y
    // se suma en C#).
    private static Dictionary<string, (double presu, double ejec)> ValoresPresupuestoRango(SqlConnection cn, DateOnly fi, DateOnly ff)
    {
        var res = new Dictionary<string, (double presu, double ejec)>(StringComparer.OrdinalIgnoreCase);
        var cur = new DateOnly(fi.Year, fi.Month, 1);
        while (cur <= ff)
        {
            int anio = cur.Year, mes = cur.Month;
            var d0 = cur;
            var d1 = cur.AddMonths(1).AddDays(-1);
            int diaIni = (fi > d0 ? fi : d0).Day;
            int diaFin = (ff < d1 ? ff : d1).Day;
            using var cmd = new SqlCommand(@"
SELECT RepCodigo, SUM(PrePresupuesto), SUM(PreEjecutado)
FROM PresupuestosHistoricos WITH (NOLOCK)
WHERE PreTipo='MNTDCAT1DIA' AND PreAnio=@an AND PreMes=@me
  AND TRY_CAST(LEFT(PreReferencia,2) AS INT) BETWEEN @d0 AND @d1
GROUP BY RepCodigo", cn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@an", anio);
            cmd.Parameters.AddWithValue("@me", mes);
            cmd.Parameters.AddWithValue("@d0", diaIni);
            cmd.Parameters.AddWithValue("@d1", diaFin);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var rep = rd.GetString(0);
                double p = rd.IsDBNull(1) ? 0 : Convert.ToDouble(rd.GetValue(1));
                double e = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
                res[rep] = res.TryGetValue(rep, out var acc) ? (acc.presu + p, acc.ejec + e) : (p, e);
            }
            cur = d0.AddMonths(1);
        }
        return res;
    }

    // ── GET /apiseg/suplidor ────────────────────────────────────────────────────
    // IEGT por Suplidor / Categoria / Producto. Devuelve por (item, vendedor ASIGNADO)
    // las metricas del suplidor de la semana: Pedidos, CliConPed, Unidades, Cajas, Valor.
    // "Asignado" = tiene el producto en su grupo (RepresentantesGrupoProductos -> GrupoProductosDetalle).
    // Se incluyen asignados con venta 0 (Valor=0) para medir penetracion. Los indicadores de
    // jornada (visitas/horas/ritmo/PDV) los toma el frontend de /semana (por RepCodigo).
    // agrupar = 'suplidor' | 'categoria' | 'producto'. Drill: suplidor->categoria->producto.
    [HttpGet("suplidor")]
    public async Task<IActionResult> Suplidor(
        [FromQuery] string? fechaIni,
        [FromQuery] string? fechaFin,
        [FromQuery] string? centros,
        [FromQuery] string? supervisores,
        [FromQuery] string? reps,
        [FromQuery] string? divisiones,
        [FromQuery] string? cargos,
        [FromQuery] string? agrupar,
        [FromQuery] int?    suplidorId,
        [FromQuery] int?    categoriaId)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        var (fi, ff) = ParseRange(fechaIni, fechaFin);
        var liStr = fi.ToString("yyyy-MM-dd");
        var lfStr = ff.ToString("yyyy-MM-dd");

        var lDiv = ParseList(divisiones);
        var parms = new Dictionary<string, string>();
        var wCentros = BuildIn("r.CedCodigo",     ParseList(centros),      "c",  parms);
        var wSups    = BuildIn("r.RepSupervisor", ParseList(supervisores), "s",  parms);
        var wReps    = BuildIn("r.RepCodigo",     ParseList(reps),         "rp", parms);
        var wCargo   = BuildIn("r.RepCargo",      ParseList(cargos),       "cg", parms);
        var wDiv     = BuildIn("ra.RepTipe",      lDiv,                    "dv", parms);
        var divJoin  = lDiv.Count > 0 ? "INNER JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = r.RepCodigo" : "";
        var repWhere = $"r.RepEstatus = 1 AND ISNULL(r.RepCargo,'') <> 'Supervisor' {wCentros} {wSups} {wReps} {wCargo} {wDiv}";

        // Expresiones de item por nivel + filtro de drill por ID (indexado). El mismo filtro
        // sobre pr.Cat3ID/pr.LinID sirve para el CTE de asignados y el de ventas (ambos usan alias pr).
        string asigItemId, saleItemId, itemNameExpr, itemNameJoin, drillFilter = "";
        var ag = (agrupar ?? "suplidor").ToLowerInvariant();
        if (ag == "producto")
        {
            asigItemId = "asg.ProID"; saleItemId = "pd.ProID";
            itemNameExpr = "pn.ProDescripcion"; itemNameJoin = "INNER JOIN Productos pn WITH (NOLOCK) ON pn.ProID = ai.ItemId";
            if (suplidorId  != null) drillFilter += " AND pr.Cat3ID = @fsupId";
            if (categoriaId != null) drillFilter += " AND pr.LinID = @fcatId";
        }
        else if (ag == "categoria")
        {
            asigItemId = "asg.LinID"; saleItemId = "pr.LinID";
            itemNameExpr = "ln.LinDescripcion"; itemNameJoin = "INNER JOIN Lineas ln WITH (NOLOCK) ON ln.LinID = ai.ItemId";
            if (suplidorId != null) drillFilter += " AND pr.Cat3ID = @fsupId";
        }
        else
        {
            ag = "suplidor"; asigItemId = "asg.Cat3ID"; saleItemId = "pr.Cat3ID";
            itemNameExpr = "c3.Cat3Descripcion"; itemNameJoin = "INNER JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = ai.ItemId";
        }

        var sql = $@"
;WITH Asig AS (   -- vendedor <-> item (asignacion por grupo de productos), respetando filtros del tablero
    SELECT DISTINCT rgp.RepCodigo, pr.Cat3ID, pr.LinID, gpd.ProID
    FROM   RepresentantesGrupoProductos rgp WITH (NOLOCK)
    INNER  JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo = rgp.GrpCodigo
    INNER  JOIN Productos pr WITH (NOLOCK) ON pr.ProID = gpd.ProID
    INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = rgp.RepCodigo
    {divJoin}
    WHERE  {repWhere} {drillFilter}
),
AItem AS ( SELECT DISTINCT {asigItemId} AS ItemId, asg.RepCodigo FROM Asig asg ),
Venta AS (   -- ventas del item en la semana, por vendedor
    SELECT {saleItemId} AS ItemId, p.RepCodigo,
           COUNT(DISTINCT p.PedSecuencia)                 AS Pedidos,
           COUNT(DISTINCT p.CliID)                        AS CliConPed,
           SUM(pd.PedCantidad)                            AS Unidades,
           SUM(pd.PedCantidad / NULLIF(pr.ProUnidades,0)) AS Cajas,
           SUM(pd.PedCantidad * pd.PedPrecio)             AS Valor
    FROM   VWPedidos p WITH (NOLOCK)
    INNER  JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia = p.PedSecuencia AND pd.RepCodigo = p.RepCodigo
    INNER  JOIN Productos pr WITH (NOLOCK) ON pr.ProID = pd.ProID
    WHERE  CAST(p.PedFecha AS DATE) BETWEEN @li AND @lf {drillFilter}
    GROUP  BY {saleItemId}, p.RepCodigo
)
SELECT {itemNameExpr} AS Item, ai.ItemId, ai.RepCodigo, r.RepNombre,
       ISNULL(cd.CedDescripcion, r.CedCodigo)  AS Centro,
       ISNULL(sup.RepNombre, r.RepSupervisor)  AS Supervisor,
       ISNULL(rad.RepTipe,'')                  AS Division,
       ISNULL(v.Pedidos,0)   AS Pedidos,  ISNULL(v.CliConPed,0) AS CliConPed,
       ISNULL(v.Unidades,0)  AS Unidades, ISNULL(v.Cajas,0)     AS Cajas, ISNULL(v.Valor,0) AS Valor
FROM   AItem ai
INNER  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = ai.RepCodigo
LEFT   JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo = r.RepSupervisor
LEFT   JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo = r.CedCodigo
LEFT   JOIN RepresentantesAdicionales rad WITH (NOLOCK) ON rad.RepCodigo = r.RepCodigo
{itemNameJoin}
LEFT   JOIN Venta v ON v.ItemId = ai.ItemId AND v.RepCodigo = ai.RepCodigo
ORDER  BY Item, r.RepNombre;";

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
            cmd.Parameters.AddWithValue("@li", liStr);
            cmd.Parameters.AddWithValue("@lf", lfStr);
            if (suplidorId  != null) cmd.Parameters.AddWithValue("@fsupId", suplidorId.Value);
            if (categoriaId != null) cmd.Parameters.AddWithValue("@fcatId", categoriaId.Value);
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);

            var filas = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                filas.Add(new
                {
                    Item       = rd.IsDBNull(0) ? "(sin)" : rd.GetString(0),
                    ItemId     = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd.GetValue(1)),
                    RepCodigo  = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    RepNombre  = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    Centro     = rd.IsDBNull(4) ? "" : rd.GetString(4),
                    Supervisor = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    Division   = rd.IsDBNull(6) ? "" : rd.GetString(6),
                    Pedidos    = rd.GetInt32(7),
                    CliConPed  = rd.GetInt32(8),
                    Unidades   = Math.Round(Convert.ToDouble(rd.GetValue(9))),
                    Cajas      = Math.Round(Convert.ToDouble(rd.GetValue(10)), 1),
                    Valor      = Math.Round(Convert.ToDouble(rd.GetValue(11)))
                });
            }
            return Ok(new { ok = true, agrupar = ag, fechaIni = liStr, fechaFin = lfStr, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apiseg/parametros ──────────────────────────────────────────────────
    // Lee la tabla SeguimientoParametros_RM (baseline por Suplidor/Categoria/Producto).
    // Devuelve dict por nivel -> id -> {reps, valSem, uniSem, cajSem, efec, valTot, editado}.
    [HttpGet("parametros")]
    public async Task<IActionResult> Parametros([FromQuery] string? nivel, [FromQuery] int? suplidorId)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });

        var where = "1=1";
        if (!string.IsNullOrWhiteSpace(nivel)) where += " AND Nivel = @nivel";
        if (suplidorId != null)                where += " AND SuplidorID = @sup";
        var sql = $@"
SELECT Nivel, SuplidorID, CategoriaID, ProductoID, RepsAsignados,
       ValorSemVend, UnidadesSemVend, CajasSemVend, EfectividadObj,
       ValorTotal3m, Editado
FROM   dbo.SeguimientoParametros_RM WITH (NOLOCK)
WHERE  {where};";
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            if (!string.IsNullOrWhiteSpace(nivel)) cmd.Parameters.AddWithValue("@nivel", nivel);
            if (suplidorId != null)                cmd.Parameters.AddWithValue("@sup", suplidorId.Value);

            var suplidor  = new Dictionary<string, object>();
            var categoria = new Dictionary<string, object>();
            var producto  = new Dictionary<string, object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var niv = rd.GetString(0);
                int  sup = rd.GetInt32(1);
                int? cat = rd.IsDBNull(2) ? null : rd.GetInt32(2);
                int? pro = rd.IsDBNull(3) ? null : rd.GetInt32(3);
                var obj = new
                {
                    reps    = rd.GetInt32(4),
                    valSem  = Math.Round(Convert.ToDouble(rd.GetValue(5))),
                    uniSem  = Math.Round(Convert.ToDouble(rd.GetValue(6)), 1),
                    cajSem  = Math.Round(Convert.ToDouble(rd.GetValue(7)), 1),
                    efec    = rd.IsDBNull(8) ? (double?)null : Convert.ToDouble(rd.GetValue(8)),
                    valTot  = Math.Round(Convert.ToDouble(rd.GetValue(9))),
                    editado = !rd.IsDBNull(10) && rd.GetBoolean(10),
                    supId   = sup, catId = cat, proId = pro
                };
                if (niv == "Suplidor")  suplidor[sup.ToString()] = obj;
                else if (niv == "Categoria" && cat != null) categoria[cat.Value.ToString()] = obj;
                else if (niv == "Producto"  && pro != null) producto[pro.Value.ToString()] = obj;
            }
            return Ok(new { ok = true, suplidor, categoria, producto });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apiseg/parametros ─────────────────────────────────────────────────
    // Guarda ajustes manuales de una fila (marca Editado=1 para que el re-poblado no la pise).
    public record ParamEdit(string Nivel, int SuplidorID, int? CategoriaID, int? ProductoID,
                            double? ValorSemVend, double? UnidadesSemVend, double? CajasSemVend, double? EfectividadObj);

    [HttpPost("parametros")]
    public async Task<IActionResult> GuardarParametro([FromBody] ParamEdit e)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        if (e is null || string.IsNullOrWhiteSpace(e.Nivel)) return BadRequest(new { ok = false, error = "Datos invalidos" });

        var sql = @"
UPDATE dbo.SeguimientoParametros_RM
   SET ValorSemVend    = ISNULL(@val, ValorSemVend),
       UnidadesSemVend = ISNULL(@uni, UnidadesSemVend),
       CajasSemVend    = ISNULL(@caj, CajasSemVend),
       EfectividadObj  = COALESCE(@efe, EfectividadObj),
       Editado         = 1,
       UsuInicioSesion = @usu,
       FechaActualizacion = GETDATE()
 WHERE Nivel = @nivel AND SuplidorID = @sup
   AND ISNULL(CategoriaID,-1) = ISNULL(@cat,-1)
   AND ISNULL(ProductoID,-1)  = ISNULL(@pro,-1);";
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@nivel", e.Nivel);
            cmd.Parameters.AddWithValue("@sup", e.SuplidorID);
            cmd.Parameters.AddWithValue("@cat", (object?)e.CategoriaID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pro", (object?)e.ProductoID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@val", (object?)e.ValorSemVend ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@uni", (object?)e.UnidadesSemVend ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@caj", (object?)e.CajasSemVend ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@efe", (object?)e.EfectividadObj ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@usu", HttpContext.Session.GetString("user_login") ?? "");
            var n = await cmd.ExecuteNonQueryAsync();
            return Ok(new { ok = true, actualizados = n });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apiseg/empresa ─────────────────────────────────────────────────────
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
            var cols = Enumerable.Range(0, rd.FieldCount).Select(i => rd.GetName(i)).ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    // ── GET /apiseg/logo ────────────────────────────────────────────────────────
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
                    cmd = new SqlCommand($"SELECT TOP 1 [{col}] FROM Empresa WHERE [{col}] IS NOT NULL ORDER BY EmpID", cn);
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync() || rd.IsDBNull(0)) continue;
                    var data = (byte[])rd.GetValue(0);
                    if (data.Length < 4) continue;
                    var mime = (data[0], data[1]) switch
                    {
                        (0xFF, 0xD8) => "image/jpeg", (0x89, 0x50) => "image/png",
                        (0x47, 0x49) => "image/gif",  (0x42, 0x4D) => "image/bmp", _ => "image/jpeg"
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
