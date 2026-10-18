using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

// ── API de Historicos de Ventas ─────────────────────────────────────────────
// Fuente: VWVentas + VWVentasDetalle. Venta = VenEstatus<>0 (81=devolucion, negativo).
// Vendedor = VWVentas.RepVendedor. Monto NETO sin ITBIS = VenCantidad*VenPrecio - VenTotalDescuento.
// Filtros transaccionales: canal (Clientes.CanID), suplidor (Productos.Cat3ID),
// categoria (Productos.LinID), producto (ProID). Proyeccion del mes por CURVA REAL
// intra-mes de los ultimos 6 meses, contando solo dias laborables (lun-sab).
[ApiController]
[Route("apihist")]
[RequiereModulo("historicos")]
public partial class ApiHistoricosController(IConfiguration config, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) : ControllerBase
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));
    private bool IsAuth => HttpContext.Session.GetString("user_login") is not null;

    private static List<string> ParseList(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? new() :
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static (DateOnly ini, DateOnly fin) ParseRange(string? fi, string? ff)
    {
        if (!DateOnly.TryParse(fi, out var ini)) ini = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        if (!DateOnly.TryParse(ff, out var fin)) fin = DateOnly.FromDateTime(DateTime.Today);
        if (fin < ini) fin = ini;
        return (ini, fin);
    }

    private const string MontoNeto = "d.VenCantidad*d.VenPrecio - ISNULL(d.VenTotalDescuento,0)";

    public class VMetric { public double Uni, Caj, Monto; public int Cli, Fac; }
    public class RepInfo { public string Nombre = "", Centro = "", CedCodigo = "", SupCod = "", Supervisor = "", Division = ""; public int CanID; }

    // Filtros transaccionales (a nivel linea/cliente)
    private record Tx(List<int> Canal, List<int> Suplidor, List<int> Categoria, List<int> Producto, List<string> GrupoCliente)
    {
        public bool Any => Canal.Count > 0 || Suplidor.Count > 0 || Categoria.Count > 0 || Producto.Count > 0 || GrupoCliente.Count > 0;
        public bool NeedsClientes => Canal.Count > 0 || GrupoCliente.Count > 0;
    }
    private static List<int> ParseInts(string? csv) =>
        ParseList(csv).Select(s => int.TryParse(s, out var n) ? (int?)n : null).Where(n => n != null).Select(n => n!.Value).ToList();
    private static Tx ParseTx(string? canal, string? suplidor, string? categoria, string? producto, string? gruposCliente = null) =>
        new(ParseInts(canal), ParseInts(suplidor), ParseInts(categoria), ParseInts(producto), ParseList(gruposCliente));

    private static (string join, string where, List<KeyValuePair<string, object>> parms) TxSql(Tx tx)
    {
        var parms = new List<KeyValuePair<string, object>>();
        string In(string col, List<int> vals, string pre)
        {
            if (vals.Count == 0) return "";
            var names = vals.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        string InStr(string col, List<string> vals, string pre)
        {
            if (vals.Count == 0) return "";
            var names = vals.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        var join = tx.NeedsClientes ? "INNER JOIN Clientes c WITH (NOLOCK) ON c.CliID=v.CliID" : "";
        var w = In("c.CanID", tx.Canal, "canal") + In("pr.Cat3ID", tx.Suplidor, "suplidor")
              + In("pr.LinID", tx.Categoria, "categoria") + In("d.ProID", tx.Producto, "producto");
        if (tx.GrupoCliente.Count > 0)
        {
            var wGC = InStr("gcd.GrcCodigo", tx.GrupoCliente, "gc");
            w += $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=c.CliID{wGC})";
        }
        return (join, w, parms);
    }
    private static void AddTxParms(SqlCommand cmd, List<KeyValuePair<string, object>> parms)
    { foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value); }

    private static Dictionary<string, RepInfo> CargarReps(SqlConnection cn)
    {
        var d = new Dictionary<string, RepInfo>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand(@"
SELECT r.RepCodigo, r.RepNombre, ISNULL(r.CedCodigo,'') CedCodigo, ISNULL(cd.CedDescripcion,r.CedCodigo) Centro,
       ISNULL(r.RepSupervisor,'') SupCod, ISNULL(sup.RepNombre,r.RepSupervisor) Supervisor, ISNULL(ra.RepTipe,'') Division,
       ISNULL(ra.CanID,0) CanID
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo", cn) { CommandTimeout = 30 };
        using var rd = cmd.ExecuteReader();
        string S(int i) => rd.IsDBNull(i) ? "" : rd.GetString(i);
        while (rd.Read())
        {
            if (rd.IsDBNull(0)) continue;
            d[rd.GetString(0)] = new RepInfo { Nombre = S(1), CedCodigo = S(2), Centro = S(3), SupCod = S(4), Supervisor = S(5), Division = S(6), CanID = rd.GetInt32(7) };
        }
        return d;
    }

    // Agregado por RepVendedor en un rango. Si hay filtros transaccionales, los conteos
    // salen del mismo detalle (conjunto ya reducido); si no, van por cabecera (rapido).
    private Dictionary<string, VMetric> AggPorVendedor(SqlConnection cn, DateOnly ini, DateOnly fin, Tx tx)
    {
        var res = new Dictionary<string, VMetric>(StringComparer.OrdinalIgnoreCase);
        var iniS = ini.ToString("yyyy-MM-dd");
        var finS = fin.AddDays(1).ToString("yyyy-MM-dd");
        var (join, w, txp) = TxSql(tx);
        var counts = tx.Any ? ", COUNT(DISTINCT v.CliID) Cli, COUNT(DISTINCT v.VenSecuencia) Fac" : "";

        using (var cmd = new SqlCommand($@"
SELECT v.RepVendedor, SUM(d.VenCantidad) Uni, SUM(d.VenCantidad / NULLIF(pr.ProUnidades,0)) Caj, SUM({MontoNeto}) Monto {counts}
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
{join}
WHERE v.VenEstatus<>0 AND v.VenFecha>=@ini AND v.VenFecha<@fin AND v.RepVendedor IS NOT NULL {w}
GROUP BY v.RepVendedor", cn) { CommandTimeout = 180 })
        {
            cmd.Parameters.AddWithValue("@ini", iniS);
            cmd.Parameters.AddWithValue("@fin", finS);
            AddTxParms(cmd, txp);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var m = new VMetric
                {
                    Uni = rd.IsDBNull(1) ? 0 : Convert.ToDouble(rd.GetValue(1)),
                    Caj = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2)),
                    Monto = rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3))
                };
                if (tx.Any) { m.Cli = rd.GetInt32(4); m.Fac = rd.GetInt32(5); }
                res[rd.GetString(0)] = m;
            }
        }
        if (!tx.Any)
            using (var cmd = new SqlCommand(@"
SELECT v.RepVendedor, COUNT(DISTINCT v.CliID) Cli, COUNT(DISTINCT v.VenSecuencia) Fac
FROM VWVentas v WITH (NOLOCK)
WHERE v.VenEstatus<>0 AND v.VenFecha>=@ini AND v.VenFecha<@fin AND v.RepVendedor IS NOT NULL
GROUP BY v.RepVendedor", cn) { CommandTimeout = 120 })
            {
                cmd.Parameters.AddWithValue("@ini", iniS);
                cmd.Parameters.AddWithValue("@fin", finS);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    if (!res.TryGetValue(rd.GetString(0), out var m)) { m = new VMetric(); res[rd.GetString(0)] = m; }
                    m.Cli = rd.GetInt32(1); m.Fac = rd.GetInt32(2);
                }
            }
        return res;
    }

    private Func<string, bool> Filtro(Dictionary<string, RepInfo> reps, string? centros, string? sups, string? vends, string? divs)
    {
        var fCen = ParseList(centros).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fSup = ParseList(sups).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fRep = ParseList(vends).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fDiv = ParseList(divs).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return cod =>
        {
            if (!reps.TryGetValue(cod, out var ri)) ri = new RepInfo();
            return (fCen.Count == 0 || fCen.Contains(ri.CedCodigo)) &&
                   (fSup.Count == 0 || fSup.Contains(ri.SupCod)) &&
                   (fRep.Count == 0 || fRep.Contains(cod)) &&
                   (fDiv.Count == 0 || fDiv.Contains(ri.Division));
        };
    }

    // ── GET /apihist/ventas ─────────────────────────────────────────────────
    // 'vendedorDrill'+'granularidad' agregados 2026-09-27 (pedido explícito: drill-down
    // Fecha→Cliente en la pestaña Ventas Sup./Vend.) — cuando 'granularidad' viene
    // informado, se salta TODO el flujo normal (AggPorVendedor sobre todos los
    // vendedores + Δ% vs. período previo) y corre una consulta nueva, acotada a UN
    // vendedor, agrupada por día o por (día+cliente) — mismos filtros transaccionales
    // (Tx/TxSql/AddTxParms) y el mismo MontoNeto que ya usa AggPorVendedor, sin duplicar
    // esa lógica.
    [HttpGet("ventas")]
    public IActionResult Ventas(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? reps, [FromQuery] string? divisiones,
        [FromQuery] string? canal, [FromQuery] string? suplidor, [FromQuery] string? categoria, [FromQuery] string? producto,
        [FromQuery] string? gruposCliente,
        [FromQuery] string? vendedorDrill, [FromQuery] string? granularidad)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            if (!string.IsNullOrWhiteSpace(vendedorDrill) && granularidad is "dia" or "cliente")
            {
                var txDrill = ParseTx(canal, suplidor, categoria, producto, gruposCliente);
                var (joinD, wD, parmsD) = TxSql(txDrill);
                bool porCliente = granularidad == "cliente";
                var joinCli = porCliente ? "LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.CliID=v.CliID" : "";
                var selCli = porCliente ? "v.CliID CliID, MAX(cl.CliCodigo) CliCodigo, MAX(cl.CliNombre) CliNombre," : "CAST(v.VenFecha AS DATE) Fecha,";
                var groupCli = porCliente ? "v.CliID" : "CAST(v.VenFecha AS DATE)";
                using var cnD = NewConn();
                cnD.Open();
                using var cmdD = new SqlCommand($@"
SELECT {selCli} SUM(d.VenCantidad) Uni, SUM(d.VenCantidad/NULLIF(pr.ProUnidades,0)) Caj, SUM({MontoNeto}) Monto,
       COUNT(DISTINCT v.CliID) Cli, COUNT(DISTINCT v.VenSecuencia) Fac
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
{joinCli}
{joinD}
WHERE v.VenEstatus<>0 AND v.VenFecha>=@ini AND v.VenFecha<@fin AND v.RepVendedor=@vend {wD}
GROUP BY {groupCli}", cnD) { CommandTimeout = 60 };
                cmdD.Parameters.AddWithValue("@ini", ini.ToString("yyyy-MM-dd"));
                cmdD.Parameters.AddWithValue("@fin", fin.AddDays(1).ToString("yyyy-MM-dd"));
                cmdD.Parameters.AddWithValue("@vend", vendedorDrill);
                AddTxParms(cmdD, parmsD);
                var filasDrill = new List<object>();
                using (var rdD = cmdD.ExecuteReader())
                {
                    while (rdD.Read())
                    {
                        double uni = rdD.IsDBNull(rdD.GetOrdinal("Uni")) ? 0 : Convert.ToDouble(rdD["Uni"]);
                        double caj = rdD.IsDBNull(rdD.GetOrdinal("Caj")) ? 0 : Convert.ToDouble(rdD["Caj"]);
                        double monto = rdD.IsDBNull(rdD.GetOrdinal("Monto")) ? 0 : Convert.ToDouble(rdD["Monto"]);
                        int cli = rdD.GetInt32(rdD.GetOrdinal("Cli"));
                        int fac = rdD.GetInt32(rdD.GetOrdinal("Fac"));
                        if (porCliente)
                        {
                            filasDrill.Add(new
                            {
                                CliID = rdD.GetInt32(rdD.GetOrdinal("CliID")),
                                CliCodigo = rdD.IsDBNull(rdD.GetOrdinal("CliCodigo")) ? "" : rdD.GetString(rdD.GetOrdinal("CliCodigo")),
                                CliNombre = rdD.IsDBNull(rdD.GetOrdinal("CliNombre")) ? "" : rdD.GetString(rdD.GetOrdinal("CliNombre")),
                                Uni = Math.Round(uni), Caj = Math.Round(caj, 1), Monto = Math.Round(monto),
                                Cli = cli, Fac = fac, Ticket = fac > 0 ? Math.Round(monto / fac) : 0
                            });
                        }
                        else
                        {
                            filasDrill.Add(new
                            {
                                Fecha = rdD.GetDateTime(rdD.GetOrdinal("Fecha")).ToString("yyyy-MM-dd"),
                                Uni = Math.Round(uni), Caj = Math.Round(caj, 1), Monto = Math.Round(monto),
                                Cli = cli, Fac = fac, Ticket = fac > 0 ? Math.Round(monto / fac) : 0
                            });
                        }
                    }
                }
                return Ok(new { ok = true, granularidad, filas = filasDrill });
            }
            int dias = fin.DayNumber - ini.DayNumber + 1;
            var pFin = ini.AddDays(-1);
            var pIni = pFin.AddDays(-(dias - 1));
            var tx = ParseTx(canal, suplidor, categoria, producto, gruposCliente);

            using var cn = NewConn();
            cn.Open();
            var repInfo = CargarReps(cn);
            var pasa = Filtro(repInfo, centros, supervisores, reps, divisiones);
            var cur = AggPorVendedor(cn, ini, fin, tx);
            var prev = AggPorVendedor(cn, pIni, pFin, tx);

            var filas = new List<object>();
            foreach (var (cod, m) in cur)
            {
                if (!pasa(cod)) continue;
                if (!repInfo.TryGetValue(cod, out var ri)) ri = new RepInfo { Nombre = cod };
                prev.TryGetValue(cod, out var pm);
                double pMonto = pm?.Monto ?? 0;
                filas.Add(new
                {
                    Vendedor = cod, ri.Nombre, ri.Centro, ri.CedCodigo, ri.SupCod, ri.Supervisor, ri.Division,
                    Uni = Math.Round(m.Uni), Caj = Math.Round(m.Caj, 1), Monto = Math.Round(m.Monto),
                    Cli = m.Cli, Fac = m.Fac, Ticket = m.Fac > 0 ? Math.Round(m.Monto / m.Fac) : 0,
                    MontoPrev = Math.Round(pMonto),
                    Delta = pMonto != 0 ? Math.Round(100.0 * (m.Monto - pMonto) / pMonto, 1) : (double?)null
                });
            }
            return Ok(new
            {
                ok = true,
                rango = new { ini = ini.ToString("yyyy-MM-dd"), fin = fin.ToString("yyyy-MM-dd") },
                previo = new { ini = pIni.ToString("yyyy-MM-dd"), fin = pFin.ToString("yyyy-MM-dd") },
                filas
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apihist/ventas-mensual ─────────────────────────────────────────
    // Histórico Mes a Mes (pedido explícito 2026-09-28): 1 fila por (Año,Mes,Vendedor),
    // SIN límite de años hacia atrás (todo lo que haya en VWVentas) y SOLO meses ya
    // CERRADOS (el mes en curso se excluye — "quiero ver el histórico de meses
    // cerrados", no un mes parcial mezclado con los cerrados). El agregado a
    // Centro/Supervisor/Compañía (el "esquema") se hace 100% client-side a partir de
    // este único arreglo plano — mismo espíritu que agg()/renderAgg ya usa en esta misma
    // pestaña para el drill Centro→Supervisor→Vendedor — para no repetir la consulta
    // por cada esquema que el usuario elija en pantalla.
    [HttpGet("ventas-mensual")]
    public IActionResult VentasMensual(
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? reps, [FromQuery] string? divisiones,
        [FromQuery] string? canal, [FromQuery] string? suplidor, [FromQuery] string? categoria, [FromQuery] string? producto,
        [FromQuery] string? gruposCliente, [FromQuery] string? fechaIni, [FromQuery] string? fechaFin)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var tx = ParseTx(canal, suplidor, categoria, producto, gruposCliente);
            using var cn = NewConn();
            cn.Open();
            var repInfo = CargarReps(cn);
            var pasa = Filtro(repInfo, centros, supervisores, reps, divisiones);
            var (join, w, txp) = TxSql(tx);

            var hoy = DateTime.Today;
            // Mes cerrado más reciente = el mes calendario anterior al actual (si hoy es
            // 2026-09-28, el corte es 2026-09-01 exclusive -> agosto 2026 es el último
            // cerrado). Sin fechas se trae TODO lo que haya hacia atrás.
            var finExclusivo = new DateTime(hoy.Year, hoy.Month, 1);
            // Respeta Desde/Hasta (2026-10-06, pedido del usuario): se toman los MESES COMPLETOS que abarca el rango
            // (del mes de "Desde" al mes de "Hasta"), siempre limitados a meses ya cerrados.
            DateTime? iniMes = null;
            if (DateTime.TryParse(fechaIni, out var dIni)) iniMes = new DateTime(dIni.Year, dIni.Month, 1);
            if (DateTime.TryParse(fechaFin, out var dFin))
            {
                var tope = new DateTime(dFin.Year, dFin.Month, 1).AddMonths(1);
                if (tope < finExclusivo) finExclusivo = tope;
            }
            var filtroIni = iniMes.HasValue ? " AND v.VenFecha>=@iniMes" : "";

            var filas = new List<object>();
            var mesesSet = new SortedSet<(int Anio, int Mes)>();
            using (var cmd = new SqlCommand($@"
SELECT YEAR(v.VenFecha) Anio, MONTH(v.VenFecha) Mes, v.RepVendedor,
       SUM(d.VenCantidad) Uni, SUM(d.VenCantidad / NULLIF(pr.ProUnidades,0)) Caj, SUM({MontoNeto}) Monto
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
{join}
WHERE v.VenEstatus<>0 AND v.VenFecha<@finExclusivo{filtroIni} AND v.RepVendedor IS NOT NULL {w}
GROUP BY YEAR(v.VenFecha), MONTH(v.VenFecha), v.RepVendedor", cn) { CommandTimeout = 240 })
            {
                cmd.Parameters.AddWithValue("@finExclusivo", finExclusivo);
                if (iniMes.HasValue) cmd.Parameters.AddWithValue("@iniMes", iniMes.Value);
                AddTxParms(cmd, txp);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var cod = rd.IsDBNull(2) ? "" : rd.GetString(2);
                    if (!pasa(cod)) continue;
                    int anio = rd.GetInt32(0), mes = rd.GetInt32(1);
                    mesesSet.Add((anio, mes));
                    repInfo.TryGetValue(cod, out var ri); ri ??= new RepInfo { Nombre = cod };
                    filas.Add(new
                    {
                        Anio = anio, Mes = mes, RepVendedor = cod,
                        ri.Nombre, ri.Centro, ri.CedCodigo, ri.SupCod, ri.Supervisor,
                        Uni = Math.Round(rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3))),
                        Caj = Math.Round(rd.IsDBNull(4) ? 0 : Convert.ToDouble(rd.GetValue(4)), 1),
                        Monto = Math.Round(rd.IsDBNull(5) ? 0 : Convert.ToDouble(rd.GetValue(5)))
                    });
                }
            }
            var meses = mesesSet.Select(m => new { Anio = m.Anio, Mes = m.Mes }).ToList();
            return Ok(new { ok = true, filas, meses });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apihist/ventas-tarjetas ────────────────────────────────────────
    // Serie mensual YTD + PROYECCION por curva real de 6 meses (dias lun-sab).
    [HttpGet("ventas-tarjetas")]
    public IActionResult VentasTarjetas(
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? reps, [FromQuery] string? divisiones,
        [FromQuery] string? canal, [FromQuery] string? suplidor, [FromQuery] string? categoria, [FromQuery] string? producto,
        [FromQuery] string? gruposCliente)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var tx = ParseTx(canal, suplidor, categoria, producto, gruposCliente);
            using var cn = NewConn();
            cn.Open();
            var repInfo = CargarReps(cn);
            var pasa = Filtro(repInfo, centros, supervisores, reps, divisiones);
            var (join, w, txp) = TxSql(tx);

            // Serie mensual del anio en curso (por mes,vendedor -> filtrable en memoria)
            var meses = new double[13]; var mUni = new double[13]; var mCaj = new double[13];
            using (var cmd = new SqlCommand($@"
SELECT MONTH(v.VenFecha) M, v.RepVendedor,
       SUM(d.VenCantidad) Uni, SUM(d.VenCantidad / NULLIF(pr.ProUnidades,0)) Caj, SUM({MontoNeto}) Monto
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
{join}
WHERE v.VenEstatus<>0 AND v.VenFecha>=DATEFROMPARTS(YEAR(GETDATE()),1,1) AND v.RepVendedor IS NOT NULL {w}
GROUP BY MONTH(v.VenFecha), v.RepVendedor", cn) { CommandTimeout = 240 })
            {
                AddTxParms(cmd, txp);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    int mes = rd.GetInt32(0); var cod = rd.IsDBNull(1) ? "" : rd.GetString(1);
                    if (mes < 1 || mes > 12 || !pasa(cod)) continue;
                    meses[mes] += rd.IsDBNull(4) ? 0 : Convert.ToDouble(rd.GetValue(4));
                    mUni[mes] += rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
                    mCaj[mes] += rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3));
                }
            }

            var hoy = DateTime.Today;
            var curva = CurvaIntraMes(cn, tx.Canal);             // curva por CANAL (el peso de fin de mes es mayormente mayorista)
            int diasHabMes = DiasHabiles(hoy.Year, hoy.Month, DateTime.DaysInMonth(hoy.Year, hoy.Month));
            int diaHab = DiasHabiles(hoy.Year, hoy.Month, hoy.Day);
            double mtd = meses[hoy.Month];
            double factor = FactorAvance(curva, diaHab, diasHabMes); // fraccion del mes tipicamente lograda a hoy
            double proy = factor > 0.01 ? mtd / factor : mtd;

            var serie = new List<object>();
            for (int m = 1; m <= 12; m++)
                serie.Add(new { mes = m, monto = Math.Round(meses[m]), uni = Math.Round(mUni[m]), caj = Math.Round(mCaj[m], 1) });

            return Ok(new
            {
                ok = true, anio = hoy.Year, mesActual = hoy.Month, serie,
                mtd = Math.Round(mtd), proyeccion = Math.Round(proy),
                diaHabil = diaHab, diasHabilesMes = diasHabMes, avance = Math.Round(factor * 100, 1)
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Dias laborables (lun-sab) desde el 1 hasta 'hastaDia' del mes
    private static int DiasHabiles(int anio, int mes, int hastaDia)
    {
        int n = 0;
        for (int d = 1; d <= hastaDia; d++)
            if (new DateTime(anio, mes, d).DayOfWeek != DayOfWeek.Sunday) n++;
        return n;
    }

    // Curva de avance acumulado (% del total del mes) por indice de dia habil,
    // promediada sobre los ultimos 6 meses completos. Usa VenTotal (forma, no monto neto).
    private double[] CurvaIntraMes(SqlConnection cn, List<int> canal)
    {
        var hoy = DateTime.Today;
        var primerMes = new DateTime(hoy.Year, hoy.Month, 1);
        var desde = primerMes.AddMonths(-6);
        var daily = new Dictionary<DateTime, double>();
        var pn = canal.Select((v, i) => $"@cc{i}").ToList();
        var join = canal.Count > 0 ? "INNER JOIN Clientes c WITH (NOLOCK) ON c.CliID=v.CliID" : "";
        var w = canal.Count > 0 ? $" AND c.CanID IN ({string.Join(",", pn)})" : "";
        using (var cmd = new SqlCommand($@"
SELECT CAST(v.VenFecha AS date) D, SUM(v.VenTotal) M
FROM VWVentas v WITH (NOLOCK)
{join}
WHERE v.VenEstatus<>0 AND v.VenFecha>=@desde AND v.VenFecha<@hasta {w}
GROUP BY CAST(v.VenFecha AS date)", cn) { CommandTimeout = 90 })
        {
            cmd.Parameters.AddWithValue("@desde", desde);
            cmd.Parameters.AddWithValue("@hasta", primerMes);
            for (int i = 0; i < canal.Count; i++) cmd.Parameters.AddWithValue(pn[i], canal[i]);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                if (!rd.IsDBNull(0)) daily[rd.GetDateTime(0).Date] = rd.IsDBNull(1) ? 0 : Convert.ToDouble(rd.GetValue(1));
        }
        // acumular fracciones por dia habil, por mes
        var sums = new double[32]; var cnts = new int[32];
        for (int k = 6; k >= 1; k--)
        {
            var m0 = primerMes.AddMonths(-k);
            int dim = DateTime.DaysInMonth(m0.Year, m0.Month);
            double total = 0;
            for (int d = 1; d <= dim; d++) total += daily.TryGetValue(new DateTime(m0.Year, m0.Month, d), out var vv) ? vv : 0;
            if (total <= 0) continue;
            double cum = 0; int wd = 0;
            for (int d = 1; d <= dim; d++)
            {
                var dt = new DateTime(m0.Year, m0.Month, d);
                if (dt.DayOfWeek == DayOfWeek.Sunday) continue;
                wd++;
                cum += daily.TryGetValue(dt, out var vv) ? vv : 0;
                if (wd <= 31) { sums[wd] += cum / total; cnts[wd]++; }
            }
        }
        var curva = new double[32];
        for (int i = 1; i <= 31; i++) curva[i] = cnts[i] > 0 ? sums[i] / cnts[i] : 0;
        return curva;
    }

    // Fraccion esperada del mes lograda al dia habil 'wd' (con respaldo lineal)
    private static double FactorAvance(double[] curva, int wd, int diasHabMes)
    {
        if (wd < 1) return 0;
        if (wd <= 31 && curva[wd] > 0) return Math.Min(1.0, curva[wd]);
        return diasHabMes > 0 ? (double)wd / diasHabMes : 1.0; // respaldo lineal
    }

    // ── GET /apihist/filtros ────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════
    //  ACTIVACIONES  (Reporte 3)
    //  Activacion = CLIENTE DISTINTO que compro. Un cliente que compra en enero y
    //  febrero cuenta en AMBOS meses; el TOTAL de la fila son los clientes distintos
    //  de todo el rango (por eso el total < suma de periodos).
    //  Filas: Supervisor->Vendedor (modo=rep) | Suplidor/Categoria/Grupo/Producto (modo=prod).
    //  Columnas: periodo segun grano (general|mes|semana|dia). Semana = lunes-domingo.
    //  Fuente: ventas (VWVentas+Detalle, VenEstatus<>0, vendedor=RepVendedor) o
    //          pedidos (VWPedidos+Detalle, vendedor=RepCodigo).
    // ══════════════════════════════════════════════════════════════════════════
    private static readonly string[] MesAbr = { "ene","feb","mar","abr","may","jun","jul","ago","sep","oct","nov","dic" };

    private sealed class ActQ
    {
        public string Tabla = "", Fecha = "", Rep = "", Estatus = "", DetJoin = "";
    }
    private static ActQ FuenteSql(string fuente, bool needDet)
    {
        if (fuente == "pedidos")
            return new ActQ {
                Tabla = "VWPedidos", Fecha = "v.PedFecha", Rep = "v.RepCodigo", Estatus = "",
                DetJoin = needDet ? "INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=v.PedSecuencia AND d.RepCodigo=v.RepCodigo" : ""
            };
        return new ActQ {
            Tabla = "VWVentas", Fecha = "v.VenFecha", Rep = "v.RepVendedor", Estatus = " AND v.VenEstatus<>0",
            DetJoin = needDet ? "INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo" : ""
        };
    }

    [HttpGet("activaciones")]
    public IActionResult Activaciones(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? fuente, [FromQuery] string? grano, [FromQuery] string? modo,
        [FromQuery] string? dimProd, [FromQuery] string? baseCmp,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos,
        [FromQuery] string? grupos, [FromQuery] string? canales, [FromQuery] string? gruposCliente)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            fuente = fuente == "pedidos" ? "pedidos" : "ventas";
            grano  = grano is "mes" or "semana" or "dia" ? grano : "general";
            modo   = modo == "prod" ? "prod" : "rep";
            dimProd = dimProd is "categoria" or "grupo" or "producto" or "canal" ? dimProd : "suplidor";
            baseCmp = baseCmp is "ruta" or "rvfh_gen" or "rvfh_acum" ? baseCmp : "";
            bool esRep = modo == "rep";

            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias);
            var lPro = ParseInts(productos);  var lGru = ParseList(grupos);
            var lCan = ParseInts(canales);    var lGC = ParseList(gruposCliente);
            bool filtroProd = lSup.Count > 0 || lCat.Count > 0 || lPro.Count > 0 || lGru.Count > 0;
            bool needCli = lCan.Count > 0 || lGC.Count > 0 || (!esRep && dimProd == "canal");   // canal = Clientes.CanID
            bool needDet = filtroProd || (!esRep && dimProd != "canal");
            bool needGrupo = lGru.Count > 0 || (!esRep && dimProd == "grupo");

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);

            // Reps que pasan centro/supervisor/vendedor/division (se resuelve en C# -> IN de codigos)
            var fCen = ParseList(centros); var fSup = ParseList(supervisores);
            var fVen = ParseList(vendedores); var fDiv = ParseList(divisiones);
            bool filtroRep = fCen.Count > 0 || fSup.Count > 0 || fVen.Count > 0 || fDiv.Count > 0;
            var repsOk = reps.Where(kv =>
                    (fCen.Count == 0 || fCen.Contains(kv.Value.CedCodigo)) &&
                    (fSup.Count == 0 || fSup.Contains(kv.Value.SupCod)) &&
                    (fVen.Count == 0 || fVen.Contains(kv.Key)) &&
                    (fDiv.Count == 0 || fDiv.Contains(kv.Value.Division)))
                .Select(kv => kv.Key).ToList();
            if (filtroRep && repsOk.Count == 0)
                return Ok(new { ok = true, desde = ini.ToString("yyyy-MM-dd"), hasta = fin.ToString("yyyy-MM-dd"),
                                fuente, grano, modo, dimProd, baseCmp,
                                periodos = new List<object>(), filas = new List<object>(),
                                total = new { valores = new Dictionary<string, int>(), total = 0, totalPrev = 0, asignados = 0 } });

            var q = FuenteSql(fuente, needDet);

            string perExpr = grano switch
            {
                "semana" => $"CONVERT(varchar(10), DATEADD(DAY, 1-DATEPART(WEEKDAY,{q.Fecha}), CAST({q.Fecha} AS date)), 23)",
                "dia"    => $"CONVERT(varchar(10), {q.Fecha}, 23)",
                "mes"    => $"LEFT(CONVERT(varchar(10), {q.Fecha}, 23), 7)",
                _        => "'GEN'"
            };
            string itemExpr = dimProd switch
            {
                "categoria" => "CAST(pr.LinID AS varchar(20))",
                "grupo"     => "CAST(gd.GrpCodigo AS varchar(20))",
                "producto"  => "CAST(pr.ProID AS varchar(20))",
                "canal"     => "CAST(cl.CanID AS varchar(20))",
                _           => "CAST(pr.Cat3ID AS varchar(20))"
            };

            var parms = new List<KeyValuePair<string, object>>();
            string In(string col, IEnumerable<object> vals, string pre)
            {
                var lst = vals.ToList(); if (lst.Count == 0) return "";
                var names = lst.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
                return $" AND {col} IN ({string.Join(",", names)})";
            }
            var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                      + In("d.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
            var wCanal = In("cl.CanID", lCan.Cast<object>(), "cn");
            var wGC = lGC.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=cl.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})"
                : "";
            var wRep = filtroRep ? In(q.Rep, repsOk.Cast<object>(), "rp") : "";
            var joins = q.DetJoin
                      + (needDet ? " INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID" : "")
                      + (needGrupo ? " INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=pr.ProID" : "")
                      + (needCli ? " INNER JOIN Clientes cl WITH (NOLOCK) ON cl.CliID=v.CliID" : "");
            // En modo REP se une a Representantes para poder agrupar por Centro y Supervisor
            var repJoin = esRep ? $" INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo={q.Rep}" : "";
            var wDim = !esRep ? $" AND {itemExpr} IS NOT NULL" : "";

            // Dimensiones + grouping sets segun el modo
            string dimsSel = esRep
                ? $"ISNULL(r.CedCodigo,'') Ced, ISNULL(r.RepSupervisor,'') Sup, {q.Rep} Rep"
                : $"{itemExpr} Item";
            string selOut = esRep
                ? "Ced, Sup, Rep, Per, COUNT(DISTINCT Cli) N, GROUPING(Ced) gC, GROUPING(Sup) gS, GROUPING(Rep) gR, GROUPING(Per) gP"
                : "Item, Per, COUNT(DISTINCT Cli) N, GROUPING(Item) gI, GROUPING(Per) gP";
            string gsFull = esRep
                ? "((Ced,Sup,Rep,Per),(Ced,Sup,Rep),(Ced,Sup,Per),(Ced,Sup),(Ced,Per),(Ced),(Per),())"
                : "((Item,Per),(Item),())";
            string gsTot = esRep
                ? "((Ced,Sup,Rep),(Ced,Sup),(Ced),())"
                : "((Item),())";

            string Sql(bool soloTotales) => $@"
SET DATEFIRST 1;
;WITH B AS (
  SELECT {dimsSel}, v.CliID AS Cli, CAST({(soloTotales ? "'GEN'" : perExpr)} AS varchar(10)) AS Per
  FROM {q.Tabla} v WITH (NOLOCK)
  {repJoin}
  {joins}
  WHERE {q.Fecha} >= @d0 AND {q.Fecha} < @d1{q.Estatus}{wRep}{wProd}{wCanal}{wGC}{wDim}
)
SELECT {selOut}
FROM B
GROUP BY GROUPING SETS {(soloTotales ? gsTot : gsFull)}";

            // ── Nodo del arbol (centro / supervisor / vendedor  |  item)
            var nodos = new Dictionary<string, Dictionary<string, object>>();
            var pers = new SortedSet<string>(StringComparer.Ordinal);
            var totVals = new Dictionary<string, int>();
            int totGeneral = 0, totPrevGeneral = 0;

            string KeyRep(int gC, int gS, int gR, string ced, string sup, string rep) =>
                gC == 1 ? "T" : gS == 1 ? "C|" + ced : gR == 1 ? "S|" + ced + "|" + sup : "R|" + ced + "|" + sup + "|" + rep;

            Dictionary<string, object> Nodo(string key, string nivel, string ced, string sup, string rep)
            {
                if (!nodos.TryGetValue(key, out var n))
                {
                    n = new Dictionary<string, object> {
                        ["nivel"] = nivel, ["ced"] = ced, ["sup"] = sup, ["rep"] = rep,
                        ["valores"] = new Dictionary<string, int>(), ["total"] = 0, ["totalPrev"] = 0, ["asignados"] = 0
                    };
                    nodos[key] = n;
                }
                return n;
            }

            void LeerRep(SqlCommand cmd, bool esPrev)
            {
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    string ced = rd.IsDBNull(0) ? "" : rd.GetString(0).Trim();
                    string sup = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim();
                    string rep = rd.IsDBNull(2) ? "" : rd.GetString(2).Trim();
                    string per = rd.IsDBNull(3) ? "" : rd.GetString(3);
                    int n = Convert.ToInt32(rd.GetValue(4));
                    int gC = Convert.ToInt32(rd.GetValue(5)), gS = Convert.ToInt32(rd.GetValue(6));
                    int gR = Convert.ToInt32(rd.GetValue(7)), gP = Convert.ToInt32(rd.GetValue(8));

                    if (gC == 1 && gP == 1) { if (esPrev) totPrevGeneral = n; else totGeneral = n; continue; }
                    if (gC == 1) { if (!esPrev) { totVals[per] = n; pers.Add(per); } continue; }

                    var key = KeyRep(gC, gS, gR, ced, sup, rep);
                    var nivel = gS == 1 ? "centro" : gR == 1 ? "sup" : "rep";
                    var nd = Nodo(key, nivel, ced, gS == 1 ? "" : sup, gR == 1 ? "" : rep);
                    if (gP == 1) nd[esPrev ? "totalPrev" : "total"] = n;
                    else if (!esPrev) { ((Dictionary<string, int>)nd["valores"])[per] = n; pers.Add(per); }
                }
            }

            void LeerProd(SqlCommand cmd, bool esPrev)
            {
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    string item = rd.IsDBNull(0) ? "" : Convert.ToString(rd.GetValue(0))!.Trim();
                    string per = rd.IsDBNull(1) ? "" : rd.GetString(1);
                    int n = Convert.ToInt32(rd.GetValue(2));
                    int gI = Convert.ToInt32(rd.GetValue(3)), gP = Convert.ToInt32(rd.GetValue(4));
                    if (gI == 1 && gP == 1) { if (esPrev) totPrevGeneral = n; else totGeneral = n; continue; }
                    if (gI == 1) { if (!esPrev) { totVals[per] = n; pers.Add(per); } continue; }
                    if (item.Length == 0) continue;
                    var nd = Nodo("I|" + item, "item", "", "", item);
                    if (gP == 1) nd[esPrev ? "totalPrev" : "total"] = n;
                    else if (!esPrev) { ((Dictionary<string, int>)nd["valores"])[per] = n; pers.Add(per); }
                }
            }

            void Correr(DateOnly a, DateOnly b, bool soloTot, bool esPrev)
            {
                using var cmd = new SqlCommand(Sql(soloTot), cn) { CommandTimeout = 300 };
                cmd.Parameters.AddWithValue("@d0", a.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@d1", b.AddDays(1).ToString("yyyy-MM-dd"));
                foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);
                if (esRep) LeerRep(cmd, esPrev); else LeerProd(cmd, esPrev);
            }

            Correr(ini, fin, grano == "general", false);
            try { Correr(ini.AddYears(-1), fin.AddYears(-1), true, true); } catch { }   // YoY

            // ── Clientes asignados (base contra ruta).
            //    Modo REP: se reparte por nivel (centro/supervisor/vendedor).
            //    Modo PRODUCTO: un suplidor no tiene clientes asignados, así que se usa el TOTAL de
            //    clientes asignados (mismo denominador para todas las filas) -> % de cobertura del mes.
            int baseGlobal = 0;
            if (baseCmp != "")
            {
                var bp = new List<KeyValuePair<string, object>>();
                string InB(string col, IEnumerable<object> vals, string pre)
                {
                    var lst = vals.ToList(); if (lst.Count == 0) return "";
                    var names = lst.Select((v, i) => { var n = $"@{pre}{i}"; bp.Add(new(n, v)); return n; });
                    return $" AND {col} IN ({string.Join(",", names)})";
                }
                var wr = filtroRep ? InB("rv.RepCodigo", repsOk.Cast<object>(), "br") : "";
                var agg = baseCmp == "rvfh_acum" ? "COUNT(*)" : "COUNT(DISTINCT rv.CliID)";
                var tabla = baseCmp == "ruta" ? "RutaVisitas" : "RutaVisitasFechaHistoricos";
                var wFecha = baseCmp == "ruta" ? "" : " AND rv.RutFecha>=@d0 AND rv.RutFecha<@d1";
                var sqlB = $@"
SELECT ISNULL(r.CedCodigo,'') Ced, ISNULL(r.RepSupervisor,'') Sup, rv.RepCodigo Rep, {agg} N,
       GROUPING(ISNULL(r.CedCodigo,'')) gC, GROUPING(ISNULL(r.RepSupervisor,'')) gS, GROUPING(rv.RepCodigo) gR
FROM {tabla} rv WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=rv.RepCodigo
WHERE 1=1{wr}{wFecha}
GROUP BY GROUPING SETS ((ISNULL(r.CedCodigo,''), ISNULL(r.RepSupervisor,''), rv.RepCodigo),
                        (ISNULL(r.CedCodigo,''), ISNULL(r.RepSupervisor,'')),
                        (ISNULL(r.CedCodigo,'')), ())";
                using var cmd = new SqlCommand(sqlB, cn) { CommandTimeout = 180 };
                cmd.Parameters.AddWithValue("@d0", ini.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@d1", fin.AddDays(1).ToString("yyyy-MM-dd"));
                foreach (var p in bp) cmd.Parameters.AddWithValue(p.Key, p.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    string ced = rd.IsDBNull(0) ? "" : rd.GetString(0).Trim();
                    string sup = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim();
                    string rep = rd.IsDBNull(2) ? "" : rd.GetString(2).Trim();
                    int n = Convert.ToInt32(rd.GetValue(3));
                    int gC = Convert.ToInt32(rd.GetValue(4)), gS = Convert.ToInt32(rd.GetValue(5)), gR = Convert.ToInt32(rd.GetValue(6));
                    if (gC == 1) { baseGlobal = n; continue; }
                    if (!esRep) continue;                       // en producto solo interesa el total
                    var key = KeyRep(gC, gS, gR, ced, sup, rep);
                    if (nodos.TryGetValue(key, out var nd)) nd["asignados"] = n;
                }
                // Producto: el total de clientes es el denominador de TODAS las filas
                if (!esRep) foreach (var nd in nodos.Values) nd["asignados"] = baseGlobal;
            }

            // ── Nombres para modo producto
            var nomItem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!esRep)
            {
                string sqlN = dimProd switch
                {
                    "categoria" => "SELECT CAST(LinID AS varchar(20)), LinDescripcion FROM Lineas WITH (NOLOCK)",
                    "grupo"     => "SELECT CAST(GrpCodigo AS varchar(20)), GrpDescripcion FROM GrupoProductos WITH (NOLOCK)",
                    "producto"  => "SELECT CAST(ProID AS varchar(20)), ProDescripcion FROM Productos WITH (NOLOCK)",
                    "canal"     => "SELECT CAST(CanID AS varchar(20)), CanDescripcion FROM CanalDistribucion WITH (NOLOCK)",
                    _           => "SELECT CAST(Cat3ID AS varchar(20)), Cat3Descripcion FROM Categoria3 WITH (NOLOCK)"
                };
                using var cmd = new SqlCommand(sqlN, cn) { CommandTimeout = 60 };
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    if (!rd.IsDBNull(0)) nomItem[rd.GetString(0).Trim()] = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim();
            }

            // ── Etiquetas de periodo
            string Lbl(string k)
            {
                if (grano == "mes" && k.Length >= 7 && int.TryParse(k[..4], out var yy) && int.TryParse(k.Substring(5, 2), out var mm))
                    return $"{MesAbr[Math.Clamp(mm - 1, 0, 11)]} {yy}";
                if (grano == "semana" && DateOnly.TryParse(k, out var lun))
                {
                    var dom = lun.AddDays(6);
                    var sem = System.Globalization.ISOWeek.GetWeekOfYear(lun.ToDateTime(TimeOnly.MinValue));
                    return $"Sem {sem:D2} · {lun.Day}–{dom.Day} {MesAbr[dom.Month - 1]}";
                }
                if (grano == "dia" && DateOnly.TryParse(k, out var d2)) return $"{d2.Day} {MesAbr[d2.Month - 1]}";
                return k;
            }
            var periodos = (grano == "general" ? new List<string>() : pers.ToList())
                           .Select(k => new { k, label = Lbl(k) }).ToList();

            // ── Salida: filas planas con nivel + nombres (el frontend arma el arbol)
            var cedNom = reps.Values.GroupBy(v => v.CedCodigo).ToDictionary(g => g.Key, g => g.First().Centro);
            var supNom = reps.Values.GroupBy(v => v.SupCod).ToDictionary(g => g.Key, g => g.First().Supervisor);
            var filas = nodos.Values.Select(n =>
            {
                var nivel = (string)n["nivel"]; var ced = (string)n["ced"];
                var sup = (string)n["sup"]; var rep = (string)n["rep"];
                string id, nombre;
                if (nivel == "centro") { id = ced; nombre = cedNom.TryGetValue(ced, out var cn2) && cn2.Length > 0 ? cn2 : ced; }
                else if (nivel == "sup") { id = sup; nombre = supNom.TryGetValue(sup, out var sn) && sn.Length > 0 ? sn : sup; }
                else if (nivel == "rep") { id = rep; nombre = reps.TryGetValue(rep, out var ri) ? ri.Nombre : rep; }
                else { id = rep; nombre = nomItem.TryGetValue(rep, out var nn) && nn.Length > 0 ? nn : rep; }
                return new
                {
                    nivel, id, nombre, ced, sup, rep,
                    cedNombre = cedNom.TryGetValue(ced, out var c3) ? c3 : ced,
                    supNombre = supNom.TryGetValue(sup, out var s3) ? s3 : sup,
                    valores = (Dictionary<string, int>)n["valores"],
                    total = (int)n["total"], totalPrev = (int)n["totalPrev"], asignados = (int)n["asignados"]
                };
            }).OrderBy(x => x.cedNombre).ThenBy(x => x.supNombre)
              .ThenByDescending(x => x.nivel == "centro" ? 2 : x.nivel == "sup" ? 1 : 0)
              .ThenByDescending(x => x.total).ToList();

            return Ok(new
            {
                ok = true,
                desde = ini.ToString("yyyy-MM-dd"), hasta = fin.ToString("yyyy-MM-dd"),
                fuente, grano, modo, dimProd, baseCmp,
                periodos, filas,
                total = new { valores = totVals, total = totGeneral, totalPrev = totPrevGeneral, asignados = baseGlobal }
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }


    // ══════════════════════════════════════════════════════════════════════════
    //  COMPARATIVO  (Análisis 1)  — Ventas/Pedidos: rango vs rango o vs periodos
    //  anteriores alineados (mismos días de semana desplazados). Métricas:
    //  ValorBruto = cantidad*(precio-descuento) y Facturas/Pedidos distintos.
    //  Dimensión: supervisor(→vendedor) | suplidor(→categoría) | cliente.
    // ══════════════════════════════════════════════════════════════════════════
    [HttpGet("comparativo")]
    public IActionResult Comparativo(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? fechaIni2, [FromQuery] string? fechaFin2,
        [FromQuery] string? modo, [FromQuery] int? nPeriodos,
        [FromQuery] string? fuente, [FromQuery] string? dim,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos,
        [FromQuery] string? grupos, [FromQuery] string? canales, [FromQuery] string? gruposCliente)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (iniA, finA) = ParseRange(fechaIni, fechaFin);
            fuente = fuente == "pedidos" ? "pedidos" : "ventas";
            modo   = modo == "rangos" ? "rangos" : "alineado";
            dim    = dim is "suplidor" or "cliente" or "canal" ? dim : "supervisor";

            // ── Periodos: A (principal) + comparación
            var comps = new List<(DateOnly a, DateOnly b, string lbl)>();
            if (modo == "rangos")
            {
                var (iniB, finB) = ParseRange(fechaIni2, fechaFin2);
                comps.Add((iniB, finB, "B1"));
            }
            else
            {
                int len = finA.DayNumber - iniA.DayNumber + 1;
                int shift = ((len + 6) / 7) * 7;                    // redondea a semana -> mismos días
                int n = Math.Clamp(nPeriodos ?? 4, 1, 12);
                for (int k = 1; k <= n; k++)
                    comps.Add((iniA.AddDays(-shift * k), finA.AddDays(-shift * k), "B" + k));
            }
            int nComp = comps.Count;
            var minA = comps.Select(c => c.a).Append(iniA).Min();
            var maxB = comps.Select(c => c.b).Append(finA).Max();

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);

            // filtros de reps -> lista de códigos
            var fCen = ParseList(centros); var fSup = ParseList(supervisores);
            var fVen = ParseList(vendedores); var fDiv = ParseList(divisiones);
            bool filtroRep = fCen.Count > 0 || fSup.Count > 0 || fVen.Count > 0 || fDiv.Count > 0;
            var repsOk = reps.Where(kv =>
                    (fCen.Count == 0 || fCen.Contains(kv.Value.CedCodigo)) &&
                    (fSup.Count == 0 || fSup.Contains(kv.Value.SupCod)) &&
                    (fVen.Count == 0 || fVen.Contains(kv.Key)) &&
                    (fDiv.Count == 0 || fDiv.Contains(kv.Value.Division))).Select(kv => kv.Key).ToList();

            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias);
            var lPro = ParseInts(productos);  var lGru = ParseList(grupos);
            var lCan = ParseInts(canales);    var lGC = ParseList(gruposCliente);
            bool needGrupo = lGru.Count > 0;
            bool needCli = lCan.Count > 0 || lGC.Count > 0 || dim == "canal";   // canal = Clientes.CanID

            // fuente
            string tabla = fuente == "pedidos" ? "VWPedidos" : "VWVentas";
            string fecha = fuente == "pedidos" ? "v.PedFecha" : "v.VenFecha";
            string rep   = fuente == "pedidos" ? "v.RepCodigo" : "v.RepVendedor";
            string estat = fuente == "pedidos" ? "" : " AND v.VenEstatus<>0";
            string fac   = fuente == "pedidos" ? "v.PedSecuencia" : "v.VenSecuencia";
            string detJoin = fuente == "pedidos"
                ? "INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=v.PedSecuencia AND d.RepCodigo=v.RepCodigo"
                : "INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo";
            string bruto = fuente == "pedidos"
                ? "d.PedCantidad*(d.PedPrecio-ISNULL(d.PedDescuento,0))"
                : "d.VenCantidad*d.VenPrecio-ISNULL(d.VenTotalDescuento,0)";
            string cli   = "v.CliID";

            // parámetros base (fechas de buckets + filtros)
            var parms = new List<KeyValuePair<string, object>>();
            parms.Add(new("@a0", iniA.ToString("yyyy-MM-dd")));
            parms.Add(new("@a1", finA.AddDays(1).ToString("yyyy-MM-dd")));
            var whenParts = new List<string> { $"WHEN {fecha}>=@a0 AND {fecha}<@a1 THEN 'A'" };
            for (int i = 0; i < comps.Count; i++)
            {
                parms.Add(new($"@b{i}0", comps[i].a.ToString("yyyy-MM-dd")));
                parms.Add(new($"@b{i}1", comps[i].b.AddDays(1).ToString("yyyy-MM-dd")));
                whenParts.Add($"WHEN {fecha}>=@b{i}0 AND {fecha}<@b{i}1 THEN '{comps[i].lbl}'");
            }
            string bucket = "CASE " + string.Join(" ", whenParts) + " END";

            string In(string col, IEnumerable<object> vals, string pre)
            {
                var lst = vals.ToList(); if (lst.Count == 0) return "";
                var names = lst.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
                return $" AND {col} IN ({string.Join(",", names)})";
            }
            var wRep = filtroRep ? In(rep, repsOk.Cast<object>(), "rp") : "";
            var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                      + In("d.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
            var wCanal = In("cl.CanID", lCan.Cast<object>(), "cn");
            var wGC = lGC.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=cl.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})"
                : "";
            var grupoJoin = needGrupo ? " INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=pr.ProID" : "";
            var cliJoin = needCli ? " INNER JOIN Clientes cl WITH (NOLOCK) ON cl.CliID=v.CliID" : "";

            string whereBase = $"{fecha}>=@spanIni AND {fecha}<@spanFin{estat}{wRep}{wProd}{wCanal}{wGC}";
            parms.Add(new("@spanIni", minA.ToString("yyyy-MM-dd")));
            parms.Add(new("@spanFin", maxB.AddDays(1).ToString("yyyy-MM-dd")));

            void AddP(SqlCommand cmd) { foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value); }

            // valores comp: sum por bucket -> main=A, comp=promedio de B*
            (double main, double comp) Combina(Dictionary<string, double> byBk)
            {
                double a = byBk.TryGetValue("A", out var va) ? va : 0;
                double sb = 0; foreach (var c in comps) sb += byBk.TryGetValue(c.lbl, out var v) ? v : 0;
                return (a, nComp > 0 ? sb / nComp : 0);
            }

            // ── Q1: valor por (rep, cat3, bucket)  -> tarjetas suplidor y supervisor
            var vRepSup = new Dictionary<string, Dictionary<string, double>>();  // sup -> bucket -> valor
            var vSuplid = new Dictionary<string, Dictionary<string, double>>();  // cat3 -> bucket -> valor
            using (var cmd = new SqlCommand($@"
SELECT {rep} Rep, pr.Cat3ID Cat3, {bucket} Bk, SUM({bruto}) V
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID{grupoJoin}{cliJoin}
WHERE {whereBase} AND pr.Cat3ID IS NOT NULL
GROUP BY {rep}, pr.Cat3ID, {bucket}", cn) { CommandTimeout = 200 })
            {
                AddP(cmd);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    if (rd.IsDBNull(2)) continue;
                    var rp = rd.IsDBNull(0) ? "" : rd.GetString(0).Trim();
                    var c3 = rd.IsDBNull(1) ? "" : Convert.ToString(rd.GetValue(1))!.Trim();
                    var bk = rd.GetString(2);
                    var v = rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3));
                    var sup = reps.TryGetValue(rp, out var ri) ? ri.SupCod : "";
                    if (!vRepSup.TryGetValue(sup, out var m1)) { m1 = new(); vRepSup[sup] = m1; }
                    m1[bk] = m1.GetValueOrDefault(bk) + v;
                    if (!vSuplid.TryGetValue(c3, out var m2)) { m2 = new(); vSuplid[c3] = m2; }
                    m2[bk] = m2.GetValueOrDefault(bk) + v;
                }
            }

            // ── Q2: tabla del dim seleccionado (valor + facturas) por bucket.
            // supervisor: por rep + rollup en C#; suplidor: suplidor+categoría (GROUPING SETS); cliente: por cliente.
            var tblNodos = new Dictionary<string, (string niv, string k1, string k2, Dictionary<string, double> val, Dictionary<string, int> fac)>();
            void AddTbl(string niv, string k1, string k2, string bk, double v, int f)
            {
                var key = niv + "|" + k1 + "|" + k2;
                if (!tblNodos.TryGetValue(key, out var nd)) { nd = (niv, k1, k2, new(), new()); tblNodos[key] = nd; }
                nd.val[bk] = nd.val.GetValueOrDefault(bk) + v;
                nd.fac[bk] = nd.fac.GetValueOrDefault(bk) + f;
            }

            string q2Sql = dim == "canal"
                ? $@"SELECT CAST(cl.CanID AS varchar(20)) K1, {bucket} Bk, SUM({bruto}) V, COUNT(DISTINCT {fac}) F
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID{grupoJoin}{cliJoin}
WHERE {whereBase}
GROUP BY cl.CanID, {bucket}"
                : dim == "suplidor"
                ? $@"SELECT CAST(pr.Cat3ID AS varchar(20)) K1, CAST(pr.LinID AS varchar(20)) K2, {bucket} Bk,
        SUM({bruto}) V, COUNT(DISTINCT {fac}) F, GROUPING(pr.LinID) gL
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID{grupoJoin}{cliJoin}
WHERE {whereBase} AND pr.Cat3ID IS NOT NULL
GROUP BY GROUPING SETS ((pr.Cat3ID, {bucket}),(pr.Cat3ID, pr.LinID, {bucket}))"
                : dim == "cliente"
                ? $@"SELECT CAST({cli} AS varchar(20)) K1, {bucket} Bk, SUM({bruto}) V, COUNT(DISTINCT {fac}) F
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID{grupoJoin}{cliJoin}
WHERE {whereBase}
GROUP BY {cli}, {bucket}"
                : $@"SELECT {rep} K1, {bucket} Bk, SUM({bruto}) V, COUNT(DISTINCT {fac}) F
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID{grupoJoin}{cliJoin}
WHERE {whereBase}
GROUP BY {rep}, {bucket}";

            using (var cmd = new SqlCommand(q2Sql, cn) { CommandTimeout = 200 })
            {
                AddP(cmd);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    if (dim == "suplidor")
                    {
                        int gL = Convert.ToInt32(rd.GetValue(5));
                        if (rd.IsDBNull(2)) continue;
                        var k1 = rd.IsDBNull(0) ? "" : Convert.ToString(rd.GetValue(0))!.Trim();
                        var k2 = rd.IsDBNull(1) ? "" : Convert.ToString(rd.GetValue(1))!.Trim();
                        var bk = rd.GetString(2);
                        var v = rd.IsDBNull(3) ? 0 : Convert.ToDouble(rd.GetValue(3));
                        var f = rd.IsDBNull(4) ? 0 : Convert.ToInt32(rd.GetValue(4));
                        if (gL == 1) AddTbl("sup", k1, "", bk, v, f);   // suplidor
                        else AddTbl("cat", k1, k2, bk, v, f);            // categoría
                    }
                    else
                    {
                        if (rd.IsDBNull(1)) continue;
                        var k1 = rd.IsDBNull(0) ? "" : Convert.ToString(rd.GetValue(0))!.Trim();
                        var bk = rd.GetString(1);
                        var v = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
                        var f = rd.IsDBNull(3) ? 0 : Convert.ToInt32(rd.GetValue(3));
                        if (dim == "supervisor")
                        {
                            var sup = reps.TryGetValue(k1, out var ri) ? ri.SupCod : "";
                            AddTbl("sup", sup, "", bk, v, f);            // supervisor (rollup)
                            AddTbl("rep", sup, k1, bk, v, f);            // vendedor
                        }
                        else if (dim == "canal") AddTbl("can", k1, "", bk, v, f);
                        else AddTbl("cli", k1, "", bk, v, f);
                    }
                }
            }

            // ── Global (tarjeta monto y facturas): total del span por bucket
            var gVal = new Dictionary<string, double>(); var gFac = new Dictionary<string, int>();
            using (var cmd = new SqlCommand($@"
SELECT {bucket} Bk, SUM({bruto}) V, COUNT(DISTINCT {fac}) F
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID{grupoJoin}{cliJoin}
WHERE {whereBase}
GROUP BY {bucket}", cn) { CommandTimeout = 200 })
            {
                AddP(cmd);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    if (rd.IsDBNull(0)) continue;
                    var bk = rd.GetString(0);
                    gVal[bk] = rd.IsDBNull(1) ? 0 : Convert.ToDouble(rd.GetValue(1));
                    gFac[bk] = rd.IsDBNull(2) ? 0 : Convert.ToInt32(rd.GetValue(2));
                }
            }

            double? Pct(double m, double c) => c != 0 ? Math.Round((m - c) / c * 100, 1) : (double?)null;

            // nombres suplidor
            var supliNom = new Dictionary<string, string>();
            using (var cmd = new SqlCommand("SELECT CAST(Cat3ID AS varchar(20)), Cat3Descripcion FROM Categoria3 WITH (NOLOCK)", cn))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read()) if (!rd.IsDBNull(0)) supliNom[rd.GetString(0).Trim()] = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim();
            var catNom = new Dictionary<string, string>();
            using (var cmd = new SqlCommand("SELECT CAST(LinID AS varchar(20)), LinDescripcion FROM Lineas WITH (NOLOCK)", cn))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read()) if (!rd.IsDBNull(0)) catNom[rd.GetString(0).Trim()] = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim();
            var canalNom = new Dictionary<string, string>();
            using (var cmd = new SqlCommand("SELECT CAST(CanID AS varchar(20)), CanDescripcion FROM CanalDistribucion WITH (NOLOCK)", cn))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read()) if (!rd.IsDBNull(0)) canalNom[rd.GetString(0).Trim()] = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim();

            // Rollups para tarjetas
            List<(string id, string nom, double main, double comp, double? pct)> Ranking(Dictionary<string, Dictionary<string, double>> d, Func<string, string> nom)
            {
                var l = new List<(string, string, double, double, double?)>();
                foreach (var (k, byb) in d) { if (k.Length == 0) continue; var (m, c) = Combina(byb); l.Add((k, nom(k), m, c, Pct(m, c))); }
                return l;
            }
            var rkSup = Ranking(vRepSup, k => reps.Values.FirstOrDefault(x => x.SupCod == k)?.Supervisor ?? k);
            var rkSpl = Ranking(vSuplid, k => supliNom.TryGetValue(k, out var nn) ? nn : k);
            var conPct = rkSpl.Where(x => x.pct != null && x.main + x.comp > 0).ToList();
            var supPct = rkSup.Where(x => x.pct != null && x.main + x.comp > 0).ToList();

            var (gMain, gComp) = Combina(gVal);
            var (fMain, fComp) = (gFac.TryGetValue("A", out var fa) ? (double)fa : 0,
                                  comps.Sum(c => gFac.TryGetValue(c.lbl, out var fv) ? fv : 0) / (double)Math.Max(1, nComp));

            object Card(string id, string nom, double main, double comp) =>
                new { id, nombre = nom, main = Math.Round(main), comp = Math.Round(comp), pct = Pct(main, comp) };

            // Ranking por MAGNITUD (delta en $), no solo por %: una caída de 50k pesa más que −100% de 1k.
            var splMayor = conPct.OrderByDescending(x => x.main - x.comp).FirstOrDefault();
            var splMenor = conPct.OrderBy(x => x.main - x.comp).FirstOrDefault();
            var supMayor = supPct.OrderByDescending(x => x.main - x.comp).FirstOrDefault();
            var supMenor = supPct.OrderBy(x => x.main - x.comp).FirstOrDefault();

            var cards = new
            {
                monto = new { main = Math.Round(gMain), comp = Math.Round(gComp), pct = Pct(gMain, gComp) },
                facturas = new { main = Math.Round(fMain), comp = Math.Round(fComp), pct = Pct(fMain, fComp) },
                suplidorMayor = splMayor.id != null ? Card(splMayor.id, splMayor.nom, splMayor.main, splMayor.comp) : null,
                suplidorMenor = splMenor.id != null ? Card(splMenor.id, splMenor.nom, splMenor.main, splMenor.comp) : null,
                supervisorMayor = supMayor.id != null ? Card(supMayor.id, supMayor.nom, supMayor.main, supMayor.comp) : null,
                supervisorMenor = supMenor.id != null ? Card(supMenor.id, supMenor.nom, supMenor.main, supMenor.comp) : null
            };

            // ── Incidencias (auto): rankeadas por MAGNITUD del cambio en $ (representativo),
            //    no por %. Se listan las mayores caídas y subidas de suplidores y supervisores.
            string Dinero(double v) => (v < 0 ? "-" : "") + "RD$" + Math.Abs(v).ToString("#,0");
            var inci = new List<object>();
            var movSpl = conPct.Select(x => new { x.nom, tipo = "Suplidor", d = x.main - x.comp, x.pct, x.main, x.comp })
                               .Where(x => Math.Abs(x.d) >= 1).ToList();
            var movSup = supPct.Select(x => new { x.nom, tipo = "Supervisor", d = x.main - x.comp, x.pct, x.main, x.comp })
                               .Where(x => Math.Abs(x.d) >= 1).ToList();
            // 3 mayores CAÍDAS (en $) y 2 mayores SUBIDAS, mezclando suplidor+supervisor
            foreach (var x in movSpl.Concat(movSup).Where(x => x.d < 0).OrderBy(x => x.d).Take(4))
                inci.Add(new { tipo = x.tipo, nombre = x.nom, monto = Math.Round(x.d), pct = x.pct,
                    txt = $"{x.tipo} {x.nom} cayó {Dinero(Math.Abs(x.d))}{(x.pct != null ? $" ({Math.Abs(x.pct.Value):0.#}%)" : "")}" });
            foreach (var x in movSpl.Concat(movSup).Where(x => x.d > 0).OrderByDescending(x => x.d).Take(2))
                inci.Add(new { tipo = x.tipo, nombre = x.nom, monto = Math.Round(x.d), pct = x.pct,
                    txt = $"{x.tipo} {x.nom} subió {Dinero(x.d)}{(x.pct != null ? $" ({x.pct.Value:0.#}%)" : "")}" });
            // suplidores que desaparecieron (comp significativo, main=0)
            foreach (var x in rkSpl.Where(x => x.comp > 0 && x.main == 0).OrderByDescending(x => x.comp).Take(3))
                inci.Add(new { tipo = "Suplidor", nombre = x.nom, monto = Math.Round(-x.comp), pct = (double?)-100,
                    txt = $"Suplidor {x.nom} sin {(fuente == "pedidos" ? "pedidos" : "ventas")} en el periodo actual (antes {Dinero(x.comp)})" });

            // ── Filas de la tabla
            var repNom = reps;
            var filas = tblNodos.Values.Select(n =>
            {
                var (m, c) = Combina(n.val);
                var (fm, fc) = (n.fac.TryGetValue("A", out var fa2) ? (double)fa2 : 0,
                                comps.Sum(cp => n.fac.TryGetValue(cp.lbl, out var fv) ? fv : 0) / (double)Math.Max(1, nComp));
                string id = n.k1, nombre, padreId = "", padreNom = "";
                if (n.niv == "sup") { nombre = dim == "suplidor" ? (supliNom.TryGetValue(n.k1, out var sn2) ? sn2 : n.k1)
                                                                : (reps.Values.FirstOrDefault(x => x.SupCod == n.k1)?.Supervisor ?? n.k1); }
                else if (n.niv == "rep") { id = n.k2; nombre = reps.TryGetValue(n.k2, out var ri) ? ri.Nombre : n.k2; padreId = n.k1; padreNom = reps.Values.FirstOrDefault(x => x.SupCod == n.k1)?.Supervisor ?? n.k1; }
                else if (n.niv == "cat") { id = n.k2; nombre = catNom.TryGetValue(n.k2, out var cc) ? cc : n.k2; padreId = n.k1; padreNom = supliNom.TryGetValue(n.k1, out var sn) ? sn : n.k1; }
                else if (n.niv == "can") { nombre = canalNom.TryGetValue(n.k1, out var cnn) ? cnn : ("Canal " + n.k1); }
                else { nombre = "Cliente " + n.k1; }   // cliente (nombre se enriquece abajo)
                return new { nivel = n.niv, id, nombre, padreId, padreNom,
                             montoMain = Math.Round(m), montoComp = Math.Round(c), pctMonto = Pct(m, c),
                             facMain = Math.Round(fm), facComp = Math.Round(fc), pctFac = Pct(fm, fc) };
            }).OrderByDescending(x => x.montoMain).ToList();

            // nombres de cliente (si dim=cliente, top 200)
            if (dim == "cliente")
            {
                var ids = filas.Where(f => f.nivel == "cli").Select(f => f.id).Where(s => int.TryParse(s, out _)).Take(300).ToList();
                if (ids.Count > 0)
                {
                    var nm = new Dictionary<string, string>();
                    using var cmd = new SqlCommand($"SELECT CAST(CliID AS varchar(20)), CliNombre FROM Clientes WITH (NOLOCK) WHERE CliID IN ({string.Join(",", ids)})", cn) { CommandTimeout = 60 };
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read()) if (!rd.IsDBNull(0)) nm[rd.GetString(0).Trim()] = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim();
                    filas = filas.Select(f => f.nivel == "cli" && nm.TryGetValue(f.id, out var cnm)
                        ? new { f.nivel, f.id, nombre = cnm, f.padreId, f.padreNom, f.montoMain, f.montoComp, f.pctMonto, f.facMain, f.facComp, f.pctFac }
                        : f).ToList();
                }
            }

            var compLabels = comps.Select(c => c.a.ToString("yyyy-MM-dd") + " a " + c.b.ToString("yyyy-MM-dd")).ToList();
            return Ok(new
            {
                ok = true, fuente, modo, dim,
                rangoA = new { ini = iniA.ToString("yyyy-MM-dd"), fin = finA.ToString("yyyy-MM-dd") },
                comparacion = compLabels, nComp,
                cards, incidencias = inci, filas
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }


    // ══════════════════════════════════════════════════════════════════════════
    //  CLIENTES PERDIDOS y RECUPERADOS  (Análisis)
    //  Universo = RutaVisitas (asignación fija rep→cliente). "Compró" = pedido
    //  (origen pedidos) o venta NETA>0 (origen ventas, se netean devoluciones).
    //  Perdido en un grupo = cliente asignado al grupo que NO compró (el filtro)
    //  de NINGÚN rep del grupo en el rango. Cuentas = clientes DISTINTOS por nivel;
    //  el detalle es por asignación (rep,cliente). Filtro de tamaño = Clientes.ClaID.
    // ══════════════════════════════════════════════════════════════════════════
    private sealed class RepG { public string Ced="",Centro="",Sup="",SupNom="",Div="",Nom=""; }

    // (rep|cli) que COMPRARON el filtro en [a,b). fuente=pedidos|ventas.
    private HashSet<string> Compradores(SqlConnection cn, string fuente, DateOnly a, DateOnly b,
        List<int> lSup, List<int> lCat, List<int> lPro, List<string> lGru, List<string> repsOk)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool needGrupo = lGru.Count > 0;
        bool ventas = fuente == "ventas";
        var parms = new List<KeyValuePair<string, object>>();
        string In(string col, IEnumerable<object> vals, string pre)
        {
            var l = vals.ToList(); if (l.Count == 0) return "";
            var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        var repCol = ventas ? "v.RepVendedor" : "p.RepCodigo";
        var wRep = repsOk.Count > 0 ? In(repCol, repsOk.Cast<object>(), "rp") : "";
        var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                  + In("d.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
        bool needDet = ventas || lSup.Count > 0 || lCat.Count > 0 || lPro.Count > 0 || lGru.Count > 0;
        var grupoJoin = needGrupo ? " INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=pr.ProID" : "";
        string sql;
        if (ventas)
            sql = $@"
SELECT v.RepVendedor, v.CliID
FROM VWVentas v WITH (NOLOCK)
INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
{(needDet ? "INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID" : "")}{grupoJoin}
WHERE v.VenFecha>=@a AND v.VenFecha<@b{wRep}{wProd}
GROUP BY v.RepVendedor, v.CliID
HAVING SUM(d.VenCantidad*d.VenPrecio - ISNULL(d.VenTotalDescuento,0)) > 0";
        else if (needDet)
            sql = $@"
SELECT DISTINCT p.RepCodigo, p.CliID
FROM VWPedidos p WITH (NOLOCK)
INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=p.PedSecuencia AND d.RepCodigo=p.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID{grupoJoin}
WHERE p.PedFecha>=@a AND p.PedFecha<@b{wRep}{wProd}";
        else
            sql = $@"SELECT DISTINCT p.RepCodigo, p.CliID FROM VWPedidos p WITH (NOLOCK) WHERE p.PedFecha>=@a AND p.PedFecha<@b{wRep}";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 };
        cmd.Parameters.AddWithValue("@a", a.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@b", b.AddDays(1).ToString("yyyy-MM-dd"));
        foreach (var pr in parms) cmd.Parameters.AddWithValue(pr.Key, pr.Value);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
            set.Add(rd.GetValue(0).ToString()!.Trim() + "|" + Convert.ToInt32(rd.GetValue(1)));
        }
        return set;
    }

    // Asignaciones (rep,cli,tam) de RutaVisitas para repsOk, con filtro de tamaño opcional.
    private List<(string rep, int cli, int tam)> Asignaciones(SqlConnection cn, List<string> repsOk, List<int> tamanos, List<string>? gruposCliente = null)
    {
        var res = new List<(string, int, int)>();
        var parms = new List<KeyValuePair<string, object>>();
        string In(string col, IEnumerable<object> vals, string pre)
        {
            var l = vals.ToList(); if (l.Count == 0) return "";
            var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        var wRep = repsOk.Count > 0 ? In("rv.RepCodigo", repsOk.Cast<object>(), "rp") : "";
        var wTam = In("ISNULL(cl.ClaID,0)", tamanos.Cast<object>(), "tm");
        var lGC = gruposCliente ?? new List<string>();
        var wGC = lGC.Count > 0
            ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=cl.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})"
            : "";
        using var cmd = new SqlCommand($@"
SELECT rv.RepCodigo, rv.CliID, ISNULL(cl.ClaID,0) Tam
FROM RutaVisitas rv WITH (NOLOCK)
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.CliID=rv.CliID
WHERE 1=1{wRep}{wTam}{wGC}", cn) { CommandTimeout = 120 };
        foreach (var pr in parms) cmd.Parameters.AddWithValue(pr.Key, pr.Value);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
            res.Add((rd.GetString(0).Trim(), Convert.ToInt32(rd.GetValue(1)), rd.IsDBNull(2) ? 0 : Convert.ToInt32(rd.GetValue(2))));
        }
        return res;
    }

    private static readonly Dictionary<int, string> TamNom = new()
    { {0,"Sin clasif."},{1,"A Grande"},{2,"B Mediano"},{3,"C Pequeño"},{4,"D1 Muy Peq."},{5,"D2 Muy Peq."},{6,"D3 Muy Peq."},{7,"D4 Muy Peq."} };

    // Reps que pasan los filtros de centro/sup/vend/division -> lista + info por rep.
    private (List<string> repsOk, Dictionary<string, RepG> info) RepsFiltrados(
        Dictionary<string, RepInfo> reps, string? centros, string? supervisores, string? vendedores, string? divisiones, string? canales = null)
    {
        var fCen = ParseList(centros); var fSup = ParseList(supervisores);
        var fVen = ParseList(vendedores); var fDiv = ParseList(divisiones);
        var fCan = ParseInts(canales);
        var info = new Dictionary<string, RepG>(StringComparer.OrdinalIgnoreCase);
        var ok = new List<string>();
        foreach (var kv in reps)
        {
            var r = kv.Value;
            if ((fCen.Count == 0 || fCen.Contains(r.CedCodigo)) && (fSup.Count == 0 || fSup.Contains(r.SupCod)) &&
                (fVen.Count == 0 || fVen.Contains(kv.Key)) && (fDiv.Count == 0 || fDiv.Contains(r.Division)) &&
                (fCan.Count == 0 || fCan.Contains(r.CanID)))
            {
                ok.Add(kv.Key);
                info[kv.Key] = new RepG { Ced = r.CedCodigo, Centro = r.Centro, Sup = r.SupCod, SupNom = r.Supervisor, Div = r.Division, Nom = r.Nombre };
            }
        }
        return (ok, info);
    }

    // groupKey + nombre + columnas padre segun el tipo de reporte
    private static (Func<RepG, string> key, Func<RepG, string> nom, Func<RepG, string[]> padres, string[] padHead)
        GrupoDe(string tipo) => tipo switch
    {
        "centro"     => (g => "C|" + g.Ced, g => g.Centro, g => Array.Empty<string>(), Array.Empty<string>()),
        "division"   => (g => "V|" + g.Div, g => "División " + (g.Div == "" ? "(s/d)" : g.Div), g => Array.Empty<string>(), Array.Empty<string>()),
        "supervisor" => (g => "S|" + g.Sup, g => g.SupNom == "" ? g.Sup : g.SupNom, g => new[] { g.Centro }, new[] { "Centro" }),
        "vendedor"   => (g => "R|" + g.Nom, g => g.Nom, g => new[] { g.Centro, g.SupNom, g.Div }, new[] { "Centro", "Supervisor", "División" }),
        _            => (g => "ALL", g => "Compañía", g => Array.Empty<string>(), Array.Empty<string>())
    };

    // ══════════════════════════════════════════════════════════════════════════
    //  COMPARATIVOS — modo "Vs Maestra Clientes" (pedido explícito 2026-09-12)
    //  A diferencia de los otros 2 modos de Comparativo() (que comparan monto/
    //  facturas contra OTRO PERIODO), este compara contra el UNIVERSO de clientes
    //  asignados (RutaVisitas, vía Asignaciones() — mismo patrón ya usado en
    //  Perdidos()/Recuperados()): cuántos de esos clientes asignados compraron en
    //  el rango y productos elegidos (activación), no cuánto vendieron vs. el mes
    //  pasado. Restringido a "Analizar por: Supervisor → Vendedor" (confirmado con
    //  el usuario) — "clientes asignados" es un concepto de RutaVisitas ligado al
    //  vendedor, sin análogo natural por Suplidor/Canal/Cliente. Endpoint aparte de
    //  Comparativo() (no un 3er modo ahí adentro) para no complicar más esa lógica
    //  de buckets A/B1..Bn, que no aplica aquí.
    // ══════════════════════════════════════════════════════════════════════════
    private static readonly string[] DiasSemanaEs = { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo" };
    // Puerto mínimo de RutaVisitasSmart/server/src/diaResolver.ts (firstDia) — código de
    // 7 posiciones Lun..Dom, primer dígito no-cero = día activo esa semana.
    private static string? PrimerDiaActivo(string? codigo7)
    {
        if (string.IsNullOrEmpty(codigo7)) return null;
        for (int i = 0; i < codigo7.Length && i < 7; i++) if (codigo7[i] != '0') return DiasSemanaEs[i];
        return null;
    }
    // Solo semana1/semana2 (pedido explícito del usuario) — separador "/" (confirmado
    // con AskUserQuestion 2026-09-12, no barra invertida).
    private static string DiaVisitaTexto(string? sem1, string? sem2)
    {
        var d1 = PrimerDiaActivo(sem1); var d2 = PrimerDiaActivo(sem2);
        if (d1 == null && d2 == null) return "—";
        if (d1 == null) return d2!;
        if (d2 == null) return d1;
        return d1 == d2 ? d1 : $"{d1}/{d2}";
    }

    private sealed class NodoMaestra
    {
        public string Niv = "", Id = "", Nombre = "", PadreId = "", PadreNom = "";
        public HashSet<int> Asignados = new(); public HashSet<int> Activados = new(); public double Monto;
    }

    [HttpGet("comparativo-maestra")]
    public IActionResult ComparativoMaestra(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin, [FromQuery] string? fuente,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? canales, [FromQuery] string? gruposCliente, [FromQuery] string? tamanos, [FromQuery] string? vista)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            fuente = fuente == "pedidos" ? "pedidos" : "ventas";
            vista = vista is "resumido" or "clientes" ? vista : "detallado";

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, gi) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);
            object VacioResp() => new { ok = true, fuente, vista,
                rango = new { ini = ini.ToString("yyyy-MM-dd"), fin = fin.ToString("yyyy-MM-dd") },
                cards = new { asignados = 0, activados = 0, pct = (double?)null, monto = 0 }, filas = Array.Empty<object>() };
            if (repsOk.Count == 0) return Ok(VacioResp());

            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos);
            var lGru = ParseList(grupos); var lCan = ParseInts(canales); var lGC = ParseList(gruposCliente); var lTam = ParseInts(tamanos);

            // 1) Universo maestro: clientes asignados (RutaVisitas) al Grupo Cliente/tamaño elegido.
            var asig = Asignaciones(cn, repsOk, lTam, lGC.Count > 0 ? lGC : null);

            // Filtro de Canal (Clientes.CanID — DISTINTO del canal del rep que usa
            // RepsFiltrados) — se aplica aparte, post-consulta, para no tocar Asignaciones()
            // (compartida con Perdidos/Recuperados).
            if (lCan.Count > 0 && asig.Count > 0)
            {
                var idsAsig = asig.Select(a => a.cli).Distinct().ToList();
                var canPorCli = new Dictionary<int, int>();
                foreach (var chunk in idsAsig.Chunk(2000))
                {
                    using var cmdC = new SqlCommand($"SELECT CliID, ISNULL(CanID,0) FROM Clientes WITH (NOLOCK) WHERE CliID IN ({string.Join(",", chunk)})", cn) { CommandTimeout = 60 };
                    using var rdC = cmdC.ExecuteReader();
                    while (rdC.Read()) canPorCli[rdC.GetInt32(0)] = rdC.IsDBNull(1) ? 0 : rdC.GetInt32(1);
                }
                asig = asig.Where(a => canPorCli.TryGetValue(a.cli, out var c) && lCan.Contains(c)).ToList();
            }
            if (asig.Count == 0) return Ok(VacioResp());

            // 2) Monto + "compró" por (rep,cli) en el rango y productos elegidos — mismo
            // esqueleto fuente=ventas|pedidos ya usado en Comparativo()/Compradores().
            string tabla = fuente == "pedidos" ? "VWPedidos" : "VWVentas";
            string fecha = fuente == "pedidos" ? "v.PedFecha" : "v.VenFecha";
            string rep = fuente == "pedidos" ? "v.RepCodigo" : "v.RepVendedor";
            string estat = fuente == "pedidos" ? "" : " AND v.VenEstatus<>0";
            string detJoin = fuente == "pedidos"
                ? "INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=v.PedSecuencia AND d.RepCodigo=v.RepCodigo"
                : "INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo";
            string bruto = fuente == "pedidos"
                ? "d.PedCantidad*(d.PedPrecio-ISNULL(d.PedDescuento,0))"
                : "d.VenCantidad*d.VenPrecio-ISNULL(d.VenTotalDescuento,0)";
            var grupoJoin = lGru.Count > 0 ? " INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=d.ProID" : "";

            var parms = new List<KeyValuePair<string, object>>();
            string In(string col, IEnumerable<object> vals, string pre)
            {
                var lst = vals.ToList(); if (lst.Count == 0) return "";
                var names = lst.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
                return $" AND {col} IN ({string.Join(",", names)})";
            }
            var wRep = In(rep, repsOk.Cast<object>(), "rp");
            var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                      + In("d.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
            // ventas netea devoluciones (HAVING SUM>0, igual que Compradores()); pedidos no.
            string having = fuente == "ventas" ? $" HAVING SUM({bruto}) > 0" : "";

            var montoPorRepCli = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand($@"
SELECT {rep} Rep, v.CliID Cli, SUM({bruto}) Monto
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID{grupoJoin}
WHERE {fecha}>=@ini AND {fecha}<@fin{estat}{wRep}{wProd}
GROUP BY {rep}, v.CliID{having}", cn) { CommandTimeout = 200 })
            {
                cmd.Parameters.AddWithValue("@ini", ini.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@fin", fin.AddDays(1).ToString("yyyy-MM-dd"));
                foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
                    var repCod = rd.GetValue(0).ToString()!.Trim();
                    var cliId = Convert.ToInt32(rd.GetValue(1));
                    var monto = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
                    montoPorRepCli[repCod + "|" + cliId] = monto;
                }
            }

            // 3) Acumular por Supervisor (rollup) y Vendedor — mismo patrón AddTbl() de
            // Comparativo() para dim=supervisor (id de "sup" = SupCod, "rep" cuelga de ese
            // mismo id como padreId — así el frontend reusa el mismo filtro padres/hijos).
            var nodos = new Dictionary<string, NodoMaestra>();
            void Acum(string niv, string id, string nombre, string padreId, string padreNom, int cli, bool activo, double monto)
            {
                var key = niv + "|" + id;
                if (!nodos.TryGetValue(key, out var nd)) { nd = new NodoMaestra { Niv = niv, Id = id, Nombre = nombre, PadreId = padreId, PadreNom = padreNom }; nodos[key] = nd; }
                nd.Asignados.Add(cli);
                if (activo) nd.Activados.Add(cli);
                nd.Monto += monto;
            }
            var clientesDetalle = new List<(string rep, int cli)>();
            foreach (var (repCod, cli, tam) in asig)
            {
                if (!gi.TryGetValue(repCod, out var g)) continue;
                var key = repCod + "|" + cli;
                bool activo = montoPorRepCli.TryGetValue(key, out var monto);
                var supNom = g.SupNom == "" ? g.Sup : g.SupNom;
                Acum("sup", g.Sup, supNom, "", "", cli, activo, monto);
                Acum("rep", repCod, g.Nom, g.Sup, supNom, cli, activo, monto);
                if (vista == "clientes") clientesDetalle.Add((repCod, cli));
            }

            var filas = nodos.Values
                .OrderByDescending(n => n.Monto)
                .Select(n => (object)new
                {
                    nivel = n.Niv, id = n.Id, nombre = n.Nombre, padreId = n.PadreId, padreNom = n.PadreNom,
                    clientesAsignados = n.Asignados.Count, clientesActivados = n.Activados.Count,
                    pctActivacion = n.Asignados.Count > 0 ? Math.Round(n.Activados.Count * 100.0 / n.Asignados.Count, 1) : (double?)null,
                    montoTotal = Math.Round(n.Monto)
                }).ToList();

            // 4) Vista "Detallado/Clientes": 3er nivel con código/nombre + día de visita.
            if (vista == "clientes" && clientesDetalle.Count > 0)
            {
                var idsCli = clientesDetalle.Select(x => x.cli).Distinct().ToList();
                var nombresCli = new Dictionary<int, (string cod, string nom)>();
                foreach (var chunk in idsCli.Chunk(2000))
                {
                    using var cmd = new SqlCommand($"SELECT CliID, ISNULL(CliCodigo,CAST(CliID AS varchar(20))), ISNULL(CliNombre,'') FROM Clientes WITH (NOLOCK) WHERE CliID IN ({string.Join(",", chunk)})", cn) { CommandTimeout = 60 };
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read()) nombresCli[rd.GetInt32(0)] = (rd.GetString(1), rd.IsDBNull(2) ? "" : rd.GetString(2).Trim());
                }
                var diasVisita = new Dictionary<string, (string? s1, string? s2)>(StringComparer.OrdinalIgnoreCase);
                foreach (var chunk in idsCli.Chunk(2000))
                {
                    using var cmd = new SqlCommand($"SELECT RepCodigo, CliID, RutSemana1, RutSemana2 FROM RutaVisitas WITH (NOLOCK) WHERE CliID IN ({string.Join(",", chunk)})", cn) { CommandTimeout = 60 };
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                    {
                        if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
                        diasVisita[rd.GetString(0).Trim() + "|" + rd.GetInt32(1)] = (rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3));
                    }
                }
                var filasCli = clientesDetalle.Select(x =>
                {
                    var (cod, nom) = nombresCli.TryGetValue(x.cli, out var nn) ? nn : (x.cli.ToString(), "");
                    var (s1, s2) = diasVisita.TryGetValue(x.rep + "|" + x.cli, out var dv) ? dv : (null, null);
                    bool activo = montoPorRepCli.TryGetValue(x.rep + "|" + x.cli, out var m);
                    return (object)new
                    {
                        nivel = "cliDetalle", padreId = x.rep, cliId = x.cli, cliCodigo = cod, cliNombre = nom,
                        activado = activo, monto = Math.Round(activo ? m : 0),
                        diaVisita = DiaVisitaTexto(s1, s2)
                    };
                }).ToList();
                filas.AddRange(filasCli);
            }

            int totAsig = asig.Select(a => a.cli).Distinct().Count();
            int totAct = asig.Where(a => montoPorRepCli.ContainsKey(a.rep + "|" + a.cli)).Select(a => a.cli).Distinct().Count();
            double totMonto = asig.Sum(a => montoPorRepCli.TryGetValue(a.rep + "|" + a.cli, out var m) ? m : 0);
            var cards = new
            {
                asignados = totAsig, activados = totAct,
                pct = totAsig > 0 ? Math.Round(totAct * 100.0 / totAsig, 1) : (double?)null,
                monto = Math.Round(totMonto)
            };

            return Ok(new
            {
                ok = true, fuente, vista,
                rango = new { ini = ini.ToString("yyyy-MM-dd"), fin = fin.ToString("yyyy-MM-dd") },
                cards, filas
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  COMPARATIVO CTES. (sub-pestaña "Comparativo Ctes.", pedido explícito 2026-09-16;
    //  fix de atribución 2026-09-17)
    //
    //  Matriz por cliente: Mes1..MesN (pivote real, "Fecha Base") + Promedio Mes +
    //  Mes Actual ("Fecha de Comparación", default mes en curso) + Diferencia. Clona 3
    //  mecanismos ya probados en este mismo archivo en vez de inventar uno nuevo:
    //    - El pivote Mes1..MesN es EXACTAMENTE el patrón `datos[key][anio+"-"+mes]` de
    //      VendCte (línea ~1503) — acá se reutiliza el mismo diccionario para acumular
    //      TAMBIÉN "Fecha de Comparación", con la clave sentinela "COMP" (nunca choca
    //      con una clave real "anio-mes").
    //    - **Atribución SIEMPRE por el vendedor ASIGNADO HOY** (fix 2026-09-17, pedido
    //      explícito: "las ventas... debe ser que la asignes al vendedor que lo tiene
    //      asignado en Clientes" — antes el modo "rango" atribuía cada mes a quien hizo
    //      ESA venta históricamente, mezclando el historial de un cliente entre todos
    //      los vendedores que alguna vez le vendieron). Los 2 modos comparten el mismo
    //      universo/atribución (`Asignaciones()` + fallback `Clientes.RepCodigo`, clon
    //      de `VendCte` alcance="todos") — la única diferencia es que "maestra" pre-
    //      siembra en 0 los clientes asignados sin compra y "rango" solo muestra los que
    //      efectivamente transaron.
    //    - "Día de Visita" clona el lookup de `ComparativoMaestra` (`RutaVisitas.
    //      RutSemana1/2` + `PrimerDiaActivo`/`DiaVisitaTexto`), indexado por CliID.
    //  "Fecha Base" se acota (clamp) al último día del mes completo anterior al actual,
    //  para que "hasta el último mes anterior al actual" se cumpla siempre.
    // ══════════════════════════════════════════════════════════════════════════
    [HttpGet("comparativo-ctes")]
    public IActionResult ComparativoCtes(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? fechaCompIni, [FromQuery] string? fechaCompFin,
        [FromQuery] string? modo, [FromQuery] string? fuente,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? canales, [FromQuery] string? gruposCliente, [FromQuery] string? tamanos, [FromQuery] string? diaVisita)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (iniReq, finReq) = ParseRange(fechaIni, fechaFin);
            var hoy = DateOnly.FromDateTime(DateTime.Today);
            var finMax = new DateOnly(hoy.Year, hoy.Month, 1).AddDays(-1); // último día del mes completo anterior
            var fin = finReq > finMax ? finMax : finReq;
            var ini = iniReq > fin ? new DateOnly(fin.Year, fin.Month, 1) : iniReq;

            DateOnly compIni, compFin;
            if (DateOnly.TryParse(fechaCompIni, out var ci) && DateOnly.TryParse(fechaCompFin, out var cf)) { compIni = ci; compFin = cf; }
            else { compIni = new DateOnly(hoy.Year, hoy.Month, 1); compFin = hoy; }
            if (compFin < compIni) compFin = compIni;

            modo = modo == "maestra" ? "maestra" : "rango";
            fuente = fuente == "pedidos" ? "pedidos" : "ventas";

            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos);
            var lGru = ParseList(grupos); var lCan = ParseInts(canales); var lGC = ParseList(gruposCliente); var lTam = ParseInts(tamanos);
            var lDia = ParseList(diaVisita);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);

            string tabla = fuente == "pedidos" ? "VWPedidos" : "VWVentas";
            string fecha = fuente == "pedidos" ? "v.PedFecha" : "v.VenFecha";
            string estat = fuente == "pedidos" ? "" : " AND v.VenEstatus<>0";
            string detJoin = fuente == "pedidos"
                ? "INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=v.PedSecuencia AND d.RepCodigo=v.RepCodigo"
                : "INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo";
            string bruto = fuente == "pedidos"
                ? "d.PedCantidad*(d.PedPrecio-ISNULL(d.PedDescuento,0))"
                : "d.VenCantidad*d.VenPrecio-ISNULL(d.VenTotalDescuento,0)";

            var parms = new List<KeyValuePair<string, object>>();
            string In(string col, IEnumerable<object> vals, string pre)
            {
                var l = vals.ToList(); if (l.Count == 0) return "";
                var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
                return $" AND {col} IN ({string.Join(",", names)})";
            }
            var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                      + In("d.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
            var wCanal = lCan.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM Clientes clc WITH (NOLOCK) WHERE clc.CliID=v.CliID{In("ISNULL(clc.CanID,0)", lCan.Cast<object>(), "cn")})"
                : "";
            var wGC = lGC.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=v.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})"
                : "";
            var wTam = lTam.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM Clientes clt WITH (NOLOCK) WHERE clt.CliID=v.CliID{In("ISNULL(clt.ClaID,0)", lTam.Cast<object>(), "tm")})"
                : "";
            var grupoJoin = lGru.Count > 0 ? "INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=d.ProID" : "";

            // clave "RepCodigo|CliID" -> periodo ("Anio-Mes" real, o "COMP" para la Fecha
            // de Comparación) -> Monto. Mismo mecanismo que VendCte, con "COMP" como
            // pseudo-período adicional en el mismo diccionario.
            var datos = new Dictionary<string, Dictionary<string, double>>();
            var clientesVistos = new HashSet<int>();
            void Acumular(string repCod, int cliId, string periodo, double monto)
            {
                var key = repCod + "|" + cliId;
                if (!datos.TryGetValue(key, out var pp)) { pp = new(); datos[key] = pp; }
                pp[periodo] = pp.GetValueOrDefault(periodo) + monto;
                clientesVistos.Add(cliId);
            }

            // Atribución: SIEMPRE por el vendedor ASIGNADO HOY (RutaVisitas + fallback
            // Clientes.RepCodigo, vía Asignaciones() — mismo patrón que VendCte
            // alcance="todos"), NUNCA por quien hizo la venta históricamente. Pedido
            // explícito del usuario 2026-09-17: si un cliente le compró a 185 en ene-mar
            // y a 106 en abr-may pero hoy está asignado a 66, TODO ese historial debe
            // salir bajo 66 — así el vendedor actual puede ver si le falta generar venta
            // a clientes que ya tiene asignados. Esto aplica a los 2 modos por igual; la
            // única diferencia entre "rango" y "maestra" es si se pre-siembran en 0 los
            // clientes asignados que NO compraron (ver más abajo).
            var universo = Asignaciones(cn, repsOk, lTam, lGC.Count > 0 ? lGC : null);
            var repsConRuta = universo.Select(u => u.rep).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var baseReps = repsOk.Count > 0 ? repsOk : reps.Keys.ToList();
            var repsSinRuta = baseReps.Where(r => !repsConRuta.Contains(r)).ToList();
            var pares = universo.Select(u => (u.rep, u.cli)).ToList();
            if (repsSinRuta.Count > 0)
            {
                var parmsF = new List<KeyValuePair<string, object>>();
                string InF(string col, IEnumerable<object> vals, string pre)
                {
                    var l = vals.ToList(); if (l.Count == 0) return "";
                    var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parmsF.Add(new(n, v)); return n; });
                    return $" AND {col} IN ({string.Join(",", names)})";
                }
                var wRepF = InF("cl.RepCodigo", repsSinRuta.Cast<object>(), "rf");
                var wTamF = lTam.Count > 0 ? InF("ISNULL(cl.ClaID,0)", lTam.Cast<object>(), "tf") : "";
                var wGCF = lGC.Count > 0
                    ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=cl.CliID{InF("gcd.GrcCodigo", lGC.Cast<object>(), "gf")})"
                    : "";
                using var cmdF = new SqlCommand($"SELECT cl.RepCodigo, cl.CliID FROM Clientes cl WITH (NOLOCK) WHERE cl.RepCodigo IS NOT NULL{wRepF}{wTamF}{wGCF}", cn) { CommandTimeout = 60 };
                foreach (var p in parmsF) cmdF.Parameters.AddWithValue(p.Key, p.Value);
                using var rdF = cmdF.ExecuteReader();
                while (rdF.Read())
                    if (!rdF.IsDBNull(0) && !rdF.IsDBNull(1))
                        pares.Add((rdF.GetValue(0).ToString()!.Trim(), Convert.ToInt32(rdF.GetValue(1))));
            }

            var repDeCliente = new Dictionary<int, string>();
            foreach (var (rp, cli) in pares) if (!repDeCliente.ContainsKey(cli)) repDeCliente[cli] = rp;

            if (modo == "maestra")
            {
                // "Vs Maestra Clientes": pre-siembra en 0 TODO el universo asignado,
                // incluidos los clientes sin compra — "rango" solo muestra los que
                // efectivamente transaron (ver Fetch más abajo).
                foreach (var (cli, rp) in repDeCliente)
                {
                    var key = rp + "|" + cli;
                    if (!datos.ContainsKey(key)) datos[key] = new();
                    clientesVistos.Add(cli);
                }
            }

            void Fetch(DateOnly rIni, DateOnly rFin, bool pivotarMes)
            {
                var iniS = rIni.ToString("yyyy-MM-dd"); var finS = rFin.AddDays(1).ToString("yyyy-MM-dd");
                string selMes = pivotarMes ? $"YEAR({fecha}) Anio, MONTH({fecha}) Mes," : "";
                foreach (var chunk in repDeCliente.Keys.Chunk(2000))
                {
                    var parmsH = new List<KeyValuePair<string, object>>();
                    string InH(string col, IEnumerable<object> vals, string pre)
                    {
                        var l = vals.ToList(); if (l.Count == 0) return "";
                        var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parmsH.Add(new(n, v)); return n; });
                        return $" AND {col} IN ({string.Join(",", names)})";
                    }
                    var wCliH = InH("v.CliID", chunk.Cast<object>(), "hc");
                    var sqlH = $@"
SELECT v.CliID, {selMes} SUM({bruto}) Monto
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
{grupoJoin}
WHERE {fecha}>=@ini AND {fecha}<@fin{estat}{wCliH}{wProd}{wCanal}{wGC}{wTam}
GROUP BY v.CliID{(pivotarMes ? $", YEAR({fecha}), MONTH({fecha})" : "")}";
                    using var cmdH = new SqlCommand(sqlH, cn) { CommandTimeout = 180 };
                    cmdH.Parameters.AddWithValue("@ini", iniS); cmdH.Parameters.AddWithValue("@fin", finS);
                    foreach (var p in parms) cmdH.Parameters.AddWithValue(p.Key, p.Value);
                    foreach (var p in parmsH) cmdH.Parameters.AddWithValue(p.Key, p.Value);
                    using var rdH = cmdH.ExecuteReader();
                    while (rdH.Read())
                    {
                        if (rdH.IsDBNull(0)) continue;
                        int cliId = Convert.ToInt32(rdH.GetValue(0));
                        if (!repDeCliente.TryGetValue(cliId, out var repCod)) continue;
                        if (pivotarMes)
                        {
                            int anio = rdH.GetInt32(1), mes = rdH.GetInt32(2);
                            double monto = rdH.IsDBNull(3) ? 0 : Convert.ToDouble(rdH.GetValue(3));
                            Acumular(repCod, cliId, anio + "-" + mes, monto);
                        }
                        else
                        {
                            double monto = rdH.IsDBNull(1) ? 0 : Convert.ToDouble(rdH.GetValue(1));
                            Acumular(repCod, cliId, "COMP", monto);
                        }
                    }
                }
            }
            Fetch(ini, fin, true);
            Fetch(compIni, compFin, false);

            // Nombres/codigos/tamaño de clientes en 1 sola pasada.
            var nombresCli = new Dictionary<int, (string cod, string nom, int tam)>();
            foreach (var chunk in clientesVistos.Chunk(2000))
            {
                using var cmd = new SqlCommand($"SELECT CliID, ISNULL(CliCodigo,CAST(CliID AS varchar(20))), ISNULL(CliNombre,''), ISNULL(ClaID,0) FROM Clientes WITH (NOLOCK) WHERE CliID IN ({string.Join(",", chunk)})", cn) { CommandTimeout = 60 };
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) nombresCli[rd.GetInt32(0)] = (rd.GetString(1), rd.GetString(2), rd.GetInt32(3));
            }

            // Día de visita: indexado por CliID; MIN() resuelve determinísticamente si un
            // cliente aparece bajo más de un rep en RutaVisitas.
            var diasVisita = new Dictionary<int, (string? s1, string? s2)>();
            foreach (var chunk in clientesVistos.Chunk(2000))
            {
                using var cmd = new SqlCommand($"SELECT CliID, MIN(RutSemana1) RutSemana1, MIN(RutSemana2) RutSemana2 FROM RutaVisitas WITH (NOLOCK) WHERE CliID IN ({string.Join(",", chunk)}) GROUP BY CliID", cn) { CommandTimeout = 60 };
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    if (!rd.IsDBNull(0))
                        diasVisita[rd.GetInt32(0)] = (rd.IsDBNull(1) ? null : rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2));
            }

            var periodos = ListaPeriodos(ini, fin);
            int nPer = periodos.Count;
            var filas = new List<object>();
            foreach (var (key, porPeriodo) in datos)
            {
                var parts = key.Split('|'); string repCod = parts[0]; int cliId = int.Parse(parts[1]);
                if (repsOk.Count > 0 && !repsOk.Contains(repCod)) continue;
                if (!reps.TryGetValue(repCod, out var ri)) ri = new RepInfo { Nombre = repCod };
                var (cod, nom, tam) = nombresCli.TryGetValue(cliId, out var nn) ? nn : (cliId.ToString(), "", 0);
                var (s1, s2) = diasVisita.TryGetValue(cliId, out var dv) ? dv : (null, null);
                if (lDia.Count > 0)
                {
                    var d1 = PrimerDiaActivo(s1); var d2 = PrimerDiaActivo(s2);
                    if ((d1 == null || !lDia.Contains(d1)) && (d2 == null || !lDia.Contains(d2))) continue;
                }
                double total = porPeriodo.Where(kv => kv.Key != "COMP").Sum(kv => kv.Value);
                double mesActual = porPeriodo.GetValueOrDefault("COMP");
                double promedio = nPer > 0 ? total / nPer : 0;
                double diferencia = promedio - mesActual;
                double? pct = promedio > 0 ? (mesActual - promedio) / promedio * 100 : (double?)null;
                var porPeriodoOut = porPeriodo.Where(kv => kv.Key != "COMP").ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value));
                filas.Add(new
                {
                    RepCodigo = repCod, ri.Nombre, ri.Centro, ri.CedCodigo, ri.SupCod, ri.Supervisor,
                    CliID = cliId, CliCodigo = cod, CliNombre = nom, Tam = tam, TamNombre = TamNom.GetValueOrDefault(tam, tam.ToString()),
                    DiaVisita = DiaVisitaTexto(s1, s2),
                    porPeriodo = porPeriodoOut,
                    Total = Math.Round(total), Promedio = Math.Round(promedio), MesActual = Math.Round(mesActual),
                    Diferencia = Math.Round(diferencia), PctVsPromedio = pct.HasValue ? Math.Round(pct.Value, 1) : (double?)null
                });
            }

            return Ok(new
            {
                ok = true, modo, fuente,
                periodos, rangoComp = new { ini = compIni.ToString("yyyy-MM-dd"), fin = compFin.ToString("yyyy-MM-dd") },
                filas
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  VENDEDOR / CLIENTE  (pestaña "Vend./Cte.")
    //  Grano real = (RepCodigo, CliID, Año, Mes). El front arma el drill client-side
    //  (Centro→Supervisor→Vendedor→Cliente) y el pivote (columnas = Año/Mes del rango
    //  + promedio + total) desde 1 sola respuesta, mismo patrón que "Ventas
    //  (Sup./Vend.)". Origen=ventas|pedidos (mismo bruto/tabla/fecha/rep que
    //  Comparativos). Alcance=conventas (default) | todos — "todos" completa con
    //  clientes asignados (RutaVisitas, vía Asignaciones()) que no compraron, en cero.
    // ══════════════════════════════════════════════════════════════════════════
    private static List<object> ListaPeriodos(DateOnly ini, DateOnly fin)
    {
        var lista = new List<object>();
        int a = ini.Year, m = ini.Month;
        while (a < fin.Year || (a == fin.Year && m <= fin.Month))
        {
            lista.Add(new { anio = a, mes = m });
            m++; if (m > 12) { m = 1; a++; }
        }
        return lista;
    }

    [HttpGet("vendcte")]
    public IActionResult VendCte(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? origen, [FromQuery] string? alcance,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? clientes,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? gruposCliente, [FromQuery] string? tamanos)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            // "Devoluciones" (pedido explícito 2026-09-13): misma fuente que "ventas" — el
            // criterio ya usado en todo el controller para aislar una devolución es el SIGNO del
            // monto bruto (no hay ningún filtro por VenEstatus=81 en el proyecto), aplicado como
            // HAVING SUM(bruto)<0 más abajo. "pedidos" no tiene equivalente de reversa.
            origen = origen is "pedidos" or "devoluciones" ? origen : "ventas";
            bool todos = alcance == "todos";

            var lCli = ParseInts(clientes);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos);
            var lGru = ParseList(grupos); var lGC = ParseList(gruposCliente); var lTam = ParseInts(tamanos);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);

            string tabla = origen == "pedidos" ? "VWPedidos" : "VWVentas";
            string fecha = origen == "pedidos" ? "v.PedFecha" : "v.VenFecha";
            string rep = origen == "pedidos" ? "v.RepCodigo" : "v.RepVendedor";
            string estat = origen == "pedidos" ? "" : " AND v.VenEstatus<>0";
            string fac = origen == "pedidos" ? "v.PedSecuencia" : "v.VenSecuencia";
            string detJoin = origen == "pedidos"
                ? "INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=v.PedSecuencia AND d.RepCodigo=v.RepCodigo"
                : "INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo";
            string bruto = origen == "pedidos"
                ? "d.PedCantidad*(d.PedPrecio-ISNULL(d.PedDescuento,0))"
                : "d.VenCantidad*d.VenPrecio-ISNULL(d.VenTotalDescuento,0)";
            // Devoluciones = líneas de reversa reales (bruto<0), NO "neto negativo del
            // cliente/mes" — netear con las ventas del mismo bucket escondía devoluciones
            // cuando el cliente compró más de lo que devolvió, y mostraba el neto en vez
            // del monto real devuelto. Se filtra al grano de la línea, sin HAVING.
            string wDevol = origen == "devoluciones" ? $" AND {bruto}<0" : "";

            var parms = new List<KeyValuePair<string, object>>();
            string In(string col, IEnumerable<object> vals, string pre)
            {
                var l = vals.ToList(); if (l.Count == 0) return "";
                var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
                return $" AND {col} IN ({string.Join(",", names)})";
            }
            var wRep = repsOk.Count > 0 ? In(rep, repsOk.Cast<object>(), "rp") : "";
            var wCli = lCli.Count > 0 ? In("v.CliID", lCli.Cast<object>(), "cl") : "";
            var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                      + In("d.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
            var wGC = lGC.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=v.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})"
                : "";
            var wTam = lTam.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM Clientes clt WITH (NOLOCK) WHERE clt.CliID=v.CliID{In("ISNULL(clt.ClaID,0)", lTam.Cast<object>(), "tm")})"
                : "";
            var grupoJoin = lGru.Count > 0 ? "INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=d.ProID" : "";

            var iniS = ini.ToString("yyyy-MM-dd"); var finS = fin.AddDays(1).ToString("yyyy-MM-dd");
            string cantCol = origen == "pedidos" ? "d.PedCantidad" : "d.VenCantidad";
            string cajasExpr = $"SUM({cantCol}/NULLIF(pr.ProUnidades,0))";

            // clave "RepCodigo|CliID" -> "Anio-Mes" -> (Monto,Cajas,Fac)
            var datos = new Dictionary<string, Dictionary<string, (double Monto, double Cajas, int Fac)>>();
            var clientesVistos = new HashSet<int>();
            void Acumular(string repCod, int cliId, int anio, int mes, double monto, double cajas, int facN)
            {
                var key = repCod + "|" + cliId;
                if (!datos.TryGetValue(key, out var porPeriodo)) { porPeriodo = new(); datos[key] = porPeriodo; }
                porPeriodo[anio + "-" + mes] = (monto, cajas, facN);
                clientesVistos.Add(cliId);
            }

            if (!todos)
            {
                // "Solo Ctes. con Ventas": grano = venta real, atribuida al vendedor que
                // la hizo en su momento (RepCodigo histórico de la transacción).
                var sql = $@"
SELECT {rep} RepCodigo, v.CliID, YEAR({fecha}) Anio, MONTH({fecha}) Mes, SUM({bruto}) Monto, {cajasExpr} Cajas, COUNT(DISTINCT {fac}) Fac
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
{grupoJoin}
WHERE {fecha}>=@ini AND {fecha}<@fin{estat}{wRep}{wCli}{wProd}{wGC}{wTam}{wDevol}
GROUP BY {rep}, v.CliID, YEAR({fecha}), MONTH({fecha})";
                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 };
                cmd.Parameters.AddWithValue("@ini", iniS);
                cmd.Parameters.AddWithValue("@fin", finS);
                foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
                    string repCod = rd.GetValue(0).ToString()!.Trim();
                    int cliId = Convert.ToInt32(rd.GetValue(1));
                    int anio = rd.GetInt32(2), mes = rd.GetInt32(3);
                    double monto = rd.IsDBNull(4) ? 0 : Convert.ToDouble(rd.GetValue(4));
                    double cajas = rd.IsDBNull(5) ? 0 : Convert.ToDouble(rd.GetValue(5));
                    int facN = rd.IsDBNull(6) ? 0 : Convert.ToInt32(rd.GetValue(6));
                    Acumular(repCod, cliId, anio, mes, monto, cajas, facN);
                }
            }
            else
            {
                // "Todos Ctes.": pedido explícito del usuario 2026-08-26 — el universo de
                // clientes se define por la asignación ACTUAL del vendedor (RutaVisitas +
                // fallback Clientes.RepCodigo, vía Asignaciones(), igual cobertura que ya
                // se usa en Perdidos/Recuperados), pero el HISTÓRICO que se trae es TODO
                // el de esos clientes sin importar quién los vendió en su momento — si un
                // cliente hoy asignado al rep 185 antes compraba con otro vendedor, esas
                // compras viejas deben verse igual bajo 185. Es un análisis por Cliente,
                // solo que el universo de Clientes se filtra por el rep asignado HOY.
                var universo = Asignaciones(cn, repsOk, lTam, lGC.Count > 0 ? lGC : null);
                var repsConRuta = universo.Select(u => u.rep).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var baseReps = repsOk.Count > 0 ? repsOk : reps.Keys.ToList();
                var repsSinRuta = baseReps.Where(r => !repsConRuta.Contains(r)).ToList();

                var pares = universo.Select(u => (u.rep, u.cli)).ToList();
                if (repsSinRuta.Count > 0)
                {
                    var parmsF = new List<KeyValuePair<string, object>>();
                    string InF(string col, IEnumerable<object> vals, string pre)
                    {
                        var l = vals.ToList(); if (l.Count == 0) return "";
                        var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parmsF.Add(new(n, v)); return n; });
                        return $" AND {col} IN ({string.Join(",", names)})";
                    }
                    var wRepF = InF("cl.RepCodigo", repsSinRuta.Cast<object>(), "rf");
                    var wTamF = lTam.Count > 0 ? InF("ISNULL(cl.ClaID,0)", lTam.Cast<object>(), "tf") : "";
                    var wGCF = lGC.Count > 0
                        ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=cl.CliID{InF("gcd.GrcCodigo", lGC.Cast<object>(), "gf")})"
                        : "";
                    using var cmdF = new SqlCommand($"SELECT cl.RepCodigo, cl.CliID FROM Clientes cl WITH (NOLOCK) WHERE cl.RepCodigo IS NOT NULL{wRepF}{wTamF}{wGCF}", cn) { CommandTimeout = 60 };
                    foreach (var p in parmsF) cmdF.Parameters.AddWithValue(p.Key, p.Value);
                    using var rdF = cmdF.ExecuteReader();
                    while (rdF.Read())
                        if (!rdF.IsDBNull(0) && !rdF.IsDBNull(1))
                            pares.Add((rdF.GetValue(0).ToString()!.Trim(), Convert.ToInt32(rdF.GetValue(1))));
                }

                if (lCli.Count > 0) pares = pares.Where(p => lCli.Contains(p.cli)).ToList();

                // cliente -> rep asignado HOY (1:1; si una ruta lo repite, se queda con el primero)
                var repDeCliente = new Dictionary<int, string>();
                foreach (var (rp, cli) in pares) if (!repDeCliente.ContainsKey(cli)) repDeCliente[cli] = rp;

                // entradas en cero para todo el universo (clientes sin compra en el rango)
                foreach (var (cli, rp) in repDeCliente)
                {
                    var key = rp + "|" + cli;
                    if (!datos.ContainsKey(key)) datos[key] = new();
                    clientesVistos.Add(cli);
                }

                // histórico completo de esos clientes, SIN filtrar por vendedor (wRep
                // deliberadamente omitido), atribuido al rep de repDeCliente.
                foreach (var chunk in repDeCliente.Keys.Chunk(2000))
                {
                    var parmsH = new List<KeyValuePair<string, object>>();
                    string InH(string col, IEnumerable<object> vals, string pre)
                    {
                        var l = vals.ToList(); if (l.Count == 0) return "";
                        var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parmsH.Add(new(n, v)); return n; });
                        return $" AND {col} IN ({string.Join(",", names)})";
                    }
                    var wCliH = InH("v.CliID", chunk.Cast<object>(), "hc");
                    var sqlH = $@"
SELECT v.CliID, YEAR({fecha}) Anio, MONTH({fecha}) Mes, SUM({bruto}) Monto, {cajasExpr} Cajas, COUNT(DISTINCT {fac}) Fac
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
{grupoJoin}
WHERE {fecha}>=@ini AND {fecha}<@fin{estat}{wCliH}{wProd}{wDevol}
GROUP BY v.CliID, YEAR({fecha}), MONTH({fecha})";
                    using var cmdH = new SqlCommand(sqlH, cn) { CommandTimeout = 180 };
                    cmdH.Parameters.AddWithValue("@ini", iniS);
                    cmdH.Parameters.AddWithValue("@fin", finS);
                    foreach (var p in parms) cmdH.Parameters.AddWithValue(p.Key, p.Value);
                    foreach (var p in parmsH) cmdH.Parameters.AddWithValue(p.Key, p.Value);
                    using var rdH = cmdH.ExecuteReader();
                    while (rdH.Read())
                    {
                        if (rdH.IsDBNull(0)) continue;
                        int cliId = Convert.ToInt32(rdH.GetValue(0));
                        int anio = rdH.GetInt32(1), mes = rdH.GetInt32(2);
                        double monto = rdH.IsDBNull(3) ? 0 : Convert.ToDouble(rdH.GetValue(3));
                        double cajas = rdH.IsDBNull(4) ? 0 : Convert.ToDouble(rdH.GetValue(4));
                        int facN = rdH.IsDBNull(5) ? 0 : Convert.ToInt32(rdH.GetValue(5));
                        if (!repDeCliente.TryGetValue(cliId, out var repCod)) continue;
                        Acumular(repCod, cliId, anio, mes, monto, cajas, facN);
                    }
                }
            }

            // Nombres/codigos/tamaño de clientes en 1 sola pasada (evita 1 query por cliente)
            var nombresCli = new Dictionary<int, (string cod, string nom, int tam)>();
            foreach (var chunk in clientesVistos.Chunk(2000))
            {
                using var cmd = new SqlCommand($"SELECT CliID, ISNULL(CliCodigo,CAST(CliID AS varchar(20))), ISNULL(CliNombre,''), ISNULL(ClaID,0) FROM Clientes WITH (NOLOCK) WHERE CliID IN ({string.Join(",", chunk)})", cn) { CommandTimeout = 60 };
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) nombresCli[rd.GetInt32(0)] = (rd.GetString(1), rd.GetString(2), rd.GetInt32(3));
            }

            var filas = new List<object>();
            foreach (var (key, porPeriodo) in datos)
            {
                var parts = key.Split('|'); string repCod = parts[0]; int cliId = int.Parse(parts[1]);
                if (repsOk.Count > 0 && !repsOk.Contains(repCod)) continue; // rep de RutaVisitas fuera del filtro org
                if (!reps.TryGetValue(repCod, out var ri)) ri = new RepInfo { Nombre = repCod };
                var (cod, nom, tam) = nombresCli.TryGetValue(cliId, out var nn) ? nn : (cliId.ToString(), "", 0);
                double totalMonto = porPeriodo.Values.Sum(v => v.Monto);
                double totalCajas = porPeriodo.Values.Sum(v => v.Cajas);
                int totalFac = porPeriodo.Values.Sum(v => v.Fac);
                var porPeriodoOut = porPeriodo.ToDictionary(kv => kv.Key, kv => (object)new { Monto = Math.Round(kv.Value.Monto, 2), Cajas = Math.Round(kv.Value.Cajas, 2) });
                filas.Add(new
                {
                    RepCodigo = repCod, ri.Nombre, ri.Centro, ri.CedCodigo, ri.SupCod, ri.Supervisor, ri.Division,
                    CliID = cliId, CliCodigo = cod, CliNombre = nom, Tam = tam, TamNombre = TamNom.GetValueOrDefault(tam, tam.ToString()),
                    porPeriodo = porPeriodoOut,
                    Total = Math.Round(totalMonto),
                    TotalCajas = Math.Round(totalCajas, 1),
                    TotalFac = totalFac
                });
            }

            return Ok(new { ok = true, periodos = ListaPeriodos(ini, fin), filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  VENDEDOR / CLIENTE — vista "Suplidor/Categoría" (pestaña alterna a "General")
    //  Grano real = (Cat3ID, LinID, ProID). Sin pivote por Año/Mes (1 solo total del
    //  rango, pedido del usuario). El front arma el drill client-side
    //  (Suplidor→Categoría→Producto), mismo patrón de nivelbar/crumbs/columnas padre
    //  fijas que la vista "General" pero con 3 niveles (producto) en vez de 4 (org).
    // ══════════════════════════════════════════════════════════════════════════
    [HttpGet("vendcte-supcat")]
    public IActionResult VendCteSupCat(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? origen,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? clientes,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? gruposCliente, [FromQuery] string? tamanos)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            origen = origen is "pedidos" or "devoluciones" ? origen : "ventas";

            var lCli = ParseInts(clientes);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos);
            var lGru = ParseList(grupos); var lGC = ParseList(gruposCliente); var lTam = ParseInts(tamanos);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);

            string tabla = origen == "pedidos" ? "VWPedidos" : "VWVentas";
            string fecha = origen == "pedidos" ? "v.PedFecha" : "v.VenFecha";
            string rep = origen == "pedidos" ? "v.RepCodigo" : "v.RepVendedor";
            string fac = origen == "pedidos" ? "v.PedSecuencia" : "v.VenSecuencia";
            string estat = origen == "pedidos" ? "" : " AND v.VenEstatus<>0";
            string detJoin = origen == "pedidos"
                ? "INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=v.PedSecuencia AND d.RepCodigo=v.RepCodigo"
                : "INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo";
            string bruto = origen == "pedidos"
                ? "d.PedCantidad*(d.PedPrecio-ISNULL(d.PedDescuento,0))"
                : "d.VenCantidad*d.VenPrecio-ISNULL(d.VenTotalDescuento,0)";
            string cantCol = origen == "pedidos" ? "d.PedCantidad" : "d.VenCantidad";
            string cajasExpr = $"SUM({cantCol}/NULLIF(pr.ProUnidades,0))";
            // Devoluciones = líneas de reversa reales (bruto<0), filtradas al grano de la
            // línea (WHERE, no HAVING) — netear ventas+devoluciones del mismo bucket
            // escondía devoluciones y mostraba el neto en vez del monto real devuelto.
            string wDevol = origen == "devoluciones" ? $" AND {bruto}<0" : "";

            var parms = new List<KeyValuePair<string, object>>();
            string In(string col, IEnumerable<object> vals, string pre)
            {
                var l = vals.ToList(); if (l.Count == 0) return "";
                var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
                return $" AND {col} IN ({string.Join(",", names)})";
            }
            var wRep = repsOk.Count > 0 ? In(rep, repsOk.Cast<object>(), "rp") : "";
            var wCli = lCli.Count > 0 ? In("v.CliID", lCli.Cast<object>(), "cl") : "";
            var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                      + In("d.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
            var wGC = lGC.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=v.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})"
                : "";
            var wTam = lTam.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM Clientes clt WITH (NOLOCK) WHERE clt.CliID=v.CliID{In("ISNULL(clt.ClaID,0)", lTam.Cast<object>(), "tm")})"
                : "";
            var grupoJoin = lGru.Count > 0 ? "INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=d.ProID" : "";

            var iniS = ini.ToString("yyyy-MM-dd"); var finS = fin.AddDays(1).ToString("yyyy-MM-dd");

            // GROUPING SETS: 4 grupos (gran total / Suplidor / Suplidor+Categoría /
            // Suplidor+Categoría+Producto) en 1 sola pasada — necesario para que Clientes
            // y Ped./Fact. (COUNT DISTINCT) salgan EXACTOS en cada nivel Y en cada fila
            // TOTAL; sumar los conteos de un nivel hijo hacia arriba en JS los infla (un
            // mismo cliente/factura que tocó 2 productos del mismo suplidor se contaría
            // 2 veces). Mismo patrón GROUPING SETS ya usado en Comparativos más arriba.
            var sql = $@"
SELECT ISNULL(pr.Cat3ID,0) SupID, MAX(ISNULL(s.Cat3Descripcion,'(Sin suplidor)')) Suplidor,
       pr.LinID CatID, MAX(ISNULL(ln.LinDescripcion,'(Sin categoría)')) Categoria,
       pr.ProID ProID, MAX(pr.ProDescripcion) Producto,
       SUM({cantCol}) Unidades, {cajasExpr} Cajas,
       COUNT(DISTINCT {fac}) PedFact, COUNT(DISTINCT v.CliID) Clientes, SUM({bruto}) Total,
       GROUPING(pr.Cat3ID) gSup, GROUPING(pr.LinID) gCat, GROUPING(pr.ProID) gPro
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
LEFT JOIN Categoria3 s WITH (NOLOCK) ON s.Cat3ID=pr.Cat3ID
LEFT JOIN Lineas ln WITH (NOLOCK) ON ln.LinID=pr.LinID
{grupoJoin}
WHERE {fecha}>=@ini AND {fecha}<@fin{estat}{wRep}{wCli}{wProd}{wGC}{wTam}{wDevol}
GROUP BY GROUPING SETS ((pr.Cat3ID), (pr.Cat3ID, pr.LinID), (pr.Cat3ID, pr.LinID, pr.ProID), ())";
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 };
            cmd.Parameters.AddWithValue("@ini", iniS);
            cmd.Parameters.AddWithValue("@fin", finS);
            foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);

            var filas = new List<object>();
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    bool gSup = Convert.ToInt32(rd.GetValue(11)) == 1;
                    bool gCat = Convert.ToInt32(rd.GetValue(12)) == 1;
                    bool gPro = Convert.ToInt32(rd.GetValue(13)) == 1;
                    int supId = gSup ? 0 : rd.GetInt32(0); string sup = gSup ? "" : rd.GetString(1);
                    int catId = gCat || rd.IsDBNull(2) ? 0 : rd.GetInt32(2);
                    string cat = gCat ? "" : rd.GetString(3);
                    int proId = gPro || rd.IsDBNull(4) ? 0 : rd.GetInt32(4);
                    string pro = gPro || rd.IsDBNull(5) ? "" : rd.GetString(5);
                    double unidades = rd.IsDBNull(6) ? 0 : Convert.ToDouble(rd.GetValue(6));
                    double cajas = rd.IsDBNull(7) ? 0 : Convert.ToDouble(rd.GetValue(7));
                    int pedFact = rd.IsDBNull(8) ? 0 : Convert.ToInt32(rd.GetValue(8));
                    int clientesN = rd.IsDBNull(9) ? 0 : Convert.ToInt32(rd.GetValue(9));
                    double total = rd.IsDBNull(10) ? 0 : Convert.ToDouble(rd.GetValue(10));
                    string nivel = gSup ? "total" : gCat ? "suplidor" : gPro ? "categoria" : "producto";
                    filas.Add(new
                    {
                        Nivel = nivel, SupID = supId, Suplidor = sup, CatID = catId, Categoria = cat, ProID = proId, Producto = pro,
                        Unidades = Math.Round(unidades), Cajas = Math.Round(cajas, 1),
                        PedFact = pedFact, Clientes = clientesN, Total = Math.Round(total)
                    });
                }
            }

            return Ok(new { ok = true, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SUPLIDOR / CATEGORÍA — pivote mensual (Cajas/Ctes./Monto por mes)
    //  Pedido explícito del usuario 2026-09-24: activar la pestaña "Suplidor /
    //  Categoría" (hasta ahora un botón deshabilitado, sin nada detrás — no había
    //  "estructura" que reactivar). Mismos filtros que "Activaciones" (Centro/
    //  Supervisor/Vendedor/División/Suplidor/Categoría/Producto/Grupo Producto/
    //  Canal/Grupo Clientes/Fuente/Esquema) + Cliente (nuevo) + "Reporte" con 3
    //  opciones que deciden la FILA (Suplidor | Categoría | Suplidor→Categoría),
    //  no el filtro de datos.
    //
    //  Motor = el mismo GROUPING SETS de VendCteSupCat (arriba, líneas ~1945-2064)
    //  — ya resuelve "Suplidor/Categoría/Suplidor+Categoría con Cajas+Clientes+
    //  Monto exactos por nivel" — extendido para incluir el período (mes/semana/
    //  día, mismo perExpr que ya usa Activaciones más arriba) en cada tupla del
    //  GROUPING SETS, así Clientes (COUNT DISTINCT) sale exacto TANTO por celda
    //  mensual COMO en la columna/fila Total — sumar los conteos mensuales en C#
    //  infla el total (un mismo cliente que compró en 2 meses se contaría 2 veces),
    //  mismo motivo ya documentado en VendCteSupCat.
    private sealed record FilaSupCat(int SupID, string Suplidor, int CatID, string Categoria, string Per, double Cajas, int Clientes, double Monto, int GSup, int GCat, int GPer);

    [HttpGet("supcat-mensual")]
    public IActionResult SupCatMensual(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? fuente, [FromQuery] string? grano, [FromQuery] string? reporte,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? clientes,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? canales, [FromQuery] string? gruposCliente)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            fuente = fuente == "pedidos" ? "pedidos" : "ventas";
            grano = grano is "mes" or "semana" or "dia" ? grano : "mes";
            reporte = reporte is "categoria" or "supcat" ? reporte : "suplidor";

            var lCli = ParseInts(clientes);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos);
            var lGru = ParseList(grupos); var lCan = ParseInts(canales); var lGC = ParseList(gruposCliente);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var fCen = ParseList(centros); var fSup = ParseList(supervisores);
            var fVen = ParseList(vendedores); var fDiv = ParseList(divisiones);
            bool filtroRep = fCen.Count > 0 || fSup.Count > 0 || fVen.Count > 0 || fDiv.Count > 0;
            var repsOk = reps.Where(kv =>
                    (fCen.Count == 0 || fCen.Contains(kv.Value.CedCodigo)) &&
                    (fSup.Count == 0 || fSup.Contains(kv.Value.SupCod)) &&
                    (fVen.Count == 0 || fVen.Contains(kv.Key)) &&
                    (fDiv.Count == 0 || fDiv.Contains(kv.Value.Division)))
                .Select(kv => kv.Key).ToList();
            if (filtroRep && repsOk.Count == 0)
                return Ok(new { ok = true, periodos = new List<object>(), filas = new List<object>(), totalGeneral = (object?)null });

            string tabla = fuente == "pedidos" ? "VWPedidos" : "VWVentas";
            string fecha = fuente == "pedidos" ? "v.PedFecha" : "v.VenFecha";
            string rep = fuente == "pedidos" ? "v.RepCodigo" : "v.RepVendedor";
            string estat = fuente == "pedidos" ? "" : " AND v.VenEstatus<>0";
            string detJoin = fuente == "pedidos"
                ? "INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=v.PedSecuencia AND d.RepCodigo=v.RepCodigo"
                : "INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo";
            string bruto = fuente == "pedidos"
                ? "d.PedCantidad*(d.PedPrecio-ISNULL(d.PedDescuento,0))"
                : "d.VenCantidad*d.VenPrecio-ISNULL(d.VenTotalDescuento,0)";
            string cantCol = fuente == "pedidos" ? "d.PedCantidad" : "d.VenCantidad";
            string cajasExpr = $"SUM({cantCol}/NULLIF(pr.ProUnidades,0))";

            // Mismo perExpr que ya usa "Activaciones" más arriba (líneas ~429-435).
            string perExpr = grano switch
            {
                "semana" => $"CONVERT(varchar(10), DATEADD(DAY, 1-DATEPART(WEEKDAY,{fecha}), CAST({fecha} AS date)), 23)",
                "dia" => $"CONVERT(varchar(10), {fecha}, 23)",
                _ => $"LEFT(CONVERT(varchar(10), {fecha}, 23), 7)" // mes (default)
            };

            var parms = new List<KeyValuePair<string, object>>();
            string In(string col, IEnumerable<object> vals, string pre)
            {
                var l = vals.ToList(); if (l.Count == 0) return "";
                var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
                return $" AND {col} IN ({string.Join(",", names)})";
            }
            var wRep = repsOk.Count > 0 ? In(rep, repsOk.Cast<object>(), "rp") : "";
            var wCli = lCli.Count > 0 ? In("v.CliID", lCli.Cast<object>(), "cl") : "";
            var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                      + In("d.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
            var wCanal = lCan.Count > 0 ? In("cl2.CanID", lCan.Cast<object>(), "cn") : "";
            var wGC = lGC.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=v.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})"
                : "";
            var grupoJoin = lGru.Count > 0 ? "INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=d.ProID" : "";
            var clienteJoin = lCan.Count > 0 ? "INNER JOIN Clientes cl2 WITH (NOLOCK) ON cl2.CliID=v.CliID" : "";

            var iniS = ini.ToString("yyyy-MM-dd"); var finS = fin.AddDays(1).ToString("yyyy-MM-dd");

            // GROUPING SETS según "reporte": 1 sola dimensión (Suplidor o Categoría) o
            // jerarquía Suplidor→Categoría — siempre cruzada con el período, más las
            // filas/columnas de Total SIN período (rango completo) — ver comentario de
            // cabecera. IMPORTANTE: la columna ID/Nombre que sale como "SupID"/"Suplidor"
            // cambia de fuente según el modo (LinID/LinDescripcion en modo "categoria",
            // Cat3ID/Cat3Descripcion en los otros 2) — SQL Server exige que cualquier
            // columna en el SELECT esté agrupada o agregada; seleccionar Cat3ID "pelado"
            // cuando NO está en el GROUP BY (modo categoria, que solo agrupa por LinID)
            // rompería la consulta.
            string gsets; string dimIdExpr; string dimNomExpr; string dimGroupingCol;
            if (reporte == "supcat")
            {
                dimIdExpr = "ISNULL(pr.Cat3ID,0)"; dimNomExpr = "MAX(ISNULL(s.Cat3Descripcion,'(Sin suplidor)'))"; dimGroupingCol = "pr.Cat3ID";
                gsets = $"((pr.Cat3ID, pr.LinID, {perExpr}), (pr.Cat3ID, pr.LinID), (pr.Cat3ID, {perExpr}), (pr.Cat3ID), ({perExpr}), ())";
            }
            else if (reporte == "categoria")
            {
                dimIdExpr = "ISNULL(pr.LinID,0)"; dimNomExpr = "MAX(ISNULL(ln.LinDescripcion,'(Sin categoría)'))"; dimGroupingCol = "pr.LinID";
                gsets = $"((pr.LinID, {perExpr}), (pr.LinID), ({perExpr}), ())";
            }
            else // suplidor
            {
                dimIdExpr = "ISNULL(pr.Cat3ID,0)"; dimNomExpr = "MAX(ISNULL(s.Cat3Descripcion,'(Sin suplidor)'))"; dimGroupingCol = "pr.Cat3ID";
                gsets = $"((pr.Cat3ID, {perExpr}), (pr.Cat3ID), ({perExpr}), ())";
            }
            string catIdExpr = reporte == "supcat" ? "pr.LinID" : "CAST(NULL AS int)";
            string catNomExpr = reporte == "supcat" ? "MAX(ISNULL(ln.LinDescripcion,'(Sin categoría)'))" : "CAST('' AS varchar(200))";
            string gCatExpr = reporte == "supcat" ? "pr.LinID" : dimGroupingCol; // fuera de supcat, gCat espeja gSup (mismo GROUP BY)

            var sql = $@"
SELECT {dimIdExpr} SupID, {dimNomExpr} Suplidor,
       {catIdExpr} CatID, {catNomExpr} Categoria,
       CAST({perExpr} AS varchar(10)) Per,
       {cajasExpr} Cajas, COUNT(DISTINCT v.CliID) Clientes, SUM({bruto}) Monto,
       GROUPING({dimGroupingCol}) gSup, GROUPING({gCatExpr}) gCat, GROUPING({perExpr}) gPer
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
LEFT JOIN Categoria3 s WITH (NOLOCK) ON s.Cat3ID=pr.Cat3ID
LEFT JOIN Lineas ln WITH (NOLOCK) ON ln.LinID=pr.LinID
{grupoJoin}
{clienteJoin}
WHERE {fecha}>=@ini AND {fecha}<@fin{estat}{wRep}{wCli}{wProd}{wCanal}{wGC}
GROUP BY GROUPING SETS {gsets}";
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 };
            cmd.Parameters.AddWithValue("@ini", iniS);
            cmd.Parameters.AddWithValue("@fin", finS);
            foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);

            var filas = new List<FilaSupCat>();
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    int supId = rd.IsDBNull(0) ? 0 : rd.GetInt32(0);
                    string sup = rd.IsDBNull(1) ? "" : rd.GetString(1);
                    int catId = rd.IsDBNull(2) ? 0 : rd.GetInt32(2);
                    string cat = rd.IsDBNull(3) ? "" : rd.GetString(3);
                    string per = rd.IsDBNull(4) ? "" : rd.GetString(4);
                    double cajas = rd.IsDBNull(5) ? 0 : Convert.ToDouble(rd.GetValue(5));
                    int cli = rd.IsDBNull(6) ? 0 : Convert.ToInt32(rd.GetValue(6));
                    double monto = rd.IsDBNull(7) ? 0 : Convert.ToDouble(rd.GetValue(7));
                    int gSup = Convert.ToInt32(rd.GetValue(8)), gCat = Convert.ToInt32(rd.GetValue(9)), gPer = Convert.ToInt32(rd.GetValue(10));
                    filas.Add(new FilaSupCat(supId, sup, catId, cat, per, cajas, cli, monto, gSup, gCat, gPer));
                }
            }

            // ── Arma la salida: filas por nivel (suplidor / categoria) con su
            // diccionario porPeriodo + total (SIEMPRE tomado de la fila sin período del
            // mismo GROUPING SETS, nunca sumado en C# — ver comentario de cabecera).
            object Metricas(FilaSupCat f) => new { cajas = Math.Round(f.Cajas, 1), clientes = f.Clientes, monto = Math.Round(f.Monto) };
            var pers = new SortedSet<string>(StringComparer.Ordinal);
            var nodos = new Dictionary<string, (string nivel, int supId, string sup, int catId, string cat, Dictionary<string, FilaSupCat> porPeriodo, FilaSupCat? total)>();
            FilaSupCat? totalGeneralFila = null;
            var totalPorPeriodo = new Dictionary<string, FilaSupCat>();

            foreach (var f in filas)
            {
                bool esCategoriaLeaf = reporte == "supcat" && f.GCat == 0;
                bool esNivelTotal = reporte == "supcat" ? (f.GSup == 1) : (f.GCat == 1); // gCat espeja gSup cuando NO es supcat (mismo GROUPING)
                if (esNivelTotal)
                {
                    if (f.GPer == 1) totalGeneralFila = f;
                    else { totalPorPeriodo[f.Per] = f; pers.Add(f.Per); }
                    continue;
                }
                string nivel = esCategoriaLeaf ? "categoria" : "suplidor";
                string key = esCategoriaLeaf ? $"C|{f.SupID}|{f.CatID}" : $"S|{f.SupID}";
                if (!nodos.TryGetValue(key, out var nd))
                    nd = (nivel, f.SupID, f.Suplidor, esCategoriaLeaf ? f.CatID : 0, esCategoriaLeaf ? f.Categoria : "", new Dictionary<string, FilaSupCat>(), null);
                if (f.GPer == 1) nd.total = f;
                else { nd.porPeriodo[f.Per] = f; pers.Add(f.Per); }
                nodos[key] = nd;
            }

            string Lbl(string k)
            {
                if (grano == "mes" && k.Length >= 7 && int.TryParse(k[..4], out var yy) && int.TryParse(k.Substring(5, 2), out var mm))
                    return $"{MesAbr[Math.Clamp(mm - 1, 0, 11)]} {yy}";
                if (grano == "semana" && DateOnly.TryParse(k, out var lun))
                {
                    var dom = lun.AddDays(6);
                    return $"{lun.Day}–{dom.Day} {MesAbr[dom.Month - 1]}";
                }
                if (grano == "dia" && DateOnly.TryParse(k, out var d2)) return $"{d2.Day} {MesAbr[d2.Month - 1]}";
                return k;
            }
            var periodos = pers.Select(k => new { k, label = Lbl(k) }).ToList();

            var filasOut = nodos.Values
                .OrderByDescending(n => n.total?.Monto ?? 0)
                .Select(n => new
                {
                    nivel = n.nivel, supId = n.supId, suplidor = n.sup, catId = n.catId, categoria = n.cat,
                    porPeriodo = n.porPeriodo.ToDictionary(kv => kv.Key, kv => Metricas(kv.Value)),
                    total = n.total != null ? Metricas(n.total) : new { cajas = 0.0, clientes = 0, monto = 0.0 },
                })
                .ToList();

            object totalGeneral = new
            {
                porPeriodo = totalPorPeriodo.ToDictionary(kv => kv.Key, kv => Metricas(kv.Value)),
                total = totalGeneralFila != null ? Metricas(totalGeneralFila) : new { cajas = 0.0, clientes = 0, monto = 0.0 },
            };

            return Ok(new { ok = true, periodos, filas = filasOut, totalGeneral });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DETALLE DE UN CLIENTE (drill desde "Vend./Cte." → Por Cliente)
    //  Grano real = (Cat3ID Suplidor, LinID Categoría, ProID Producto, Año, Mes)
    //  para UN cliente puntual, mismo esquema de pivote (columnas Año/Mes) que
    //  VendCte, respetando los mismos filtros de producto ya elegidos en pantalla
    //  (para que la suma del detalle cuadre con el total ya mostrado de ese cliente).
    // ══════════════════════════════════════════════════════════════════════════
    [HttpGet("vendcte-detalle")]
    public IActionResult VendCteDetalle(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? origen, [FromQuery] int cliId,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        if (cliId <= 0) return BadRequest(new { ok = false, error = "cliId requerido" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            origen = origen is "pedidos" or "devoluciones" ? origen : "ventas";

            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos);
            var lGru = ParseList(grupos);

            using var cn = NewConn();
            cn.Open();

            string tabla = origen == "pedidos" ? "VWPedidos" : "VWVentas";
            string fecha = origen == "pedidos" ? "v.PedFecha" : "v.VenFecha";
            string estat = origen == "pedidos" ? "" : " AND v.VenEstatus<>0";
            string detJoin = origen == "pedidos"
                ? "INNER JOIN VWPedidosDetalle d WITH (NOLOCK) ON d.PedSecuencia=v.PedSecuencia AND d.RepCodigo=v.RepCodigo"
                : "INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo";
            string bruto = origen == "pedidos"
                ? "d.PedCantidad*(d.PedPrecio-ISNULL(d.PedDescuento,0))"
                : "d.VenCantidad*d.VenPrecio-ISNULL(d.VenTotalDescuento,0)";
            string cantCol = origen == "pedidos" ? "d.PedCantidad" : "d.VenCantidad";
            string cajasExpr = $"SUM({cantCol}/NULLIF(pr.ProUnidades,0))";
            string wDevol = origen == "devoluciones" ? $" AND {bruto}<0" : "";

            var parms = new List<KeyValuePair<string, object>>();
            string In(string col, IEnumerable<object> vals, string pre)
            {
                var l = vals.ToList(); if (l.Count == 0) return "";
                var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
                return $" AND {col} IN ({string.Join(",", names)})";
            }
            var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                      + In("d.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
            var grupoJoin = lGru.Count > 0 ? "INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=d.ProID" : "";

            var iniS = ini.ToString("yyyy-MM-dd"); var finS = fin.AddDays(1).ToString("yyyy-MM-dd");

            var sql = $@"
SELECT ISNULL(pr.Cat3ID,0) SupID, ISNULL(s.Cat3Descripcion,'(Sin suplidor)') Suplidor,
       ISNULL(pr.LinID,0) CatID, ISNULL(ln.LinDescripcion,'(Sin categoría)') Categoria,
       d.ProID, pr.ProDescripcion Producto,
       YEAR({fecha}) Anio, MONTH({fecha}) Mes, SUM({bruto}) Monto, {cajasExpr} Cajas
FROM {tabla} v WITH (NOLOCK)
{detJoin}
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
LEFT JOIN Categoria3 s WITH (NOLOCK) ON s.Cat3ID=pr.Cat3ID
LEFT JOIN Lineas ln WITH (NOLOCK) ON ln.LinID=pr.LinID
{grupoJoin}
WHERE {fecha}>=@ini AND {fecha}<@fin{estat} AND v.CliID=@cli{wProd}{wDevol}
GROUP BY pr.Cat3ID, s.Cat3Descripcion, pr.LinID, ln.LinDescripcion, d.ProID, pr.ProDescripcion, YEAR({fecha}), MONTH({fecha})";
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
            cmd.Parameters.AddWithValue("@ini", iniS);
            cmd.Parameters.AddWithValue("@fin", finS);
            cmd.Parameters.AddWithValue("@cli", cliId);
            foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);

            // clave "SupID|CatID|ProID" -> "Anio-Mes" -> (Monto,Cajas)
            var datos = new Dictionary<string, Dictionary<string, (double Monto, double Cajas)>>();
            var nombres = new Dictionary<string, (int SupID, string Suplidor, int CatID, string Categoria, int ProID, string Producto)>();
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    int supId = rd.GetInt32(0); string sup = rd.GetString(1);
                    int catId = rd.GetInt32(2); string cat = rd.GetString(3);
                    int proId = rd.GetInt32(4); string pro = rd.IsDBNull(5) ? "" : rd.GetString(5);
                    int anio = rd.GetInt32(6), mes = rd.GetInt32(7);
                    double monto = rd.IsDBNull(8) ? 0 : Convert.ToDouble(rd.GetValue(8));
                    double cajas = rd.IsDBNull(9) ? 0 : Convert.ToDouble(rd.GetValue(9));
                    var key = supId + "|" + catId + "|" + proId;
                    if (!datos.TryGetValue(key, out var porPeriodo)) { porPeriodo = new(); datos[key] = porPeriodo; nombres[key] = (supId, sup, catId, cat, proId, pro); }
                    porPeriodo[anio + "-" + mes] = (monto, cajas);
                }
            }

            var filas = new List<object>();
            foreach (var (key, porPeriodo) in datos)
            {
                var (supId, sup, catId, cat, proId, pro) = nombres[key];
                double totalMonto = porPeriodo.Values.Sum(v => v.Monto);
                double totalCajas = porPeriodo.Values.Sum(v => v.Cajas);
                var porPeriodoOut = porPeriodo.ToDictionary(kv => kv.Key, kv => (object)new { Monto = Math.Round(kv.Value.Monto, 2), Cajas = Math.Round(kv.Value.Cajas, 2) });
                filas.Add(new
                {
                    SupID = supId, Suplidor = sup, CatID = catId, Categoria = cat, ProID = proId, Producto = pro,
                    porPeriodo = porPeriodoOut,
                    Total = Math.Round(totalMonto),
                    TotalCajas = Math.Round(totalCajas, 1)
                });
            }

            return Ok(new { ok = true, periodos = ListaPeriodos(ini, fin), filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  NOTAS DE CRÉDITO (pestaña "Notas de Crédito", pedido explícito 2026-09-13)
    //
    //  A diferencia de "Devoluciones" en Vend./Cte. (que INFIERE una devolución por
    //  el signo del monto en VWVentas), acá se usa el DOCUMENTO REAL de SAP: Orin
    //  (cabecera de Nota de Crédito) + RIN1 (detalle por producto). Verificado en
    //  vivo (solo lectura) antes de construir esto:
    //    - Orin.CardCode == Clientes.CliCodigo (100% match en la muestra).
    //    - Orin.U_Repcodigo == Representantes.RepCodigo, y es el VENDEDOR real
    //      (RepCargo='PreVendedor', ~97.5% match) — U_REP_CODIGO en cambio es el
    //      Repartidor, NO se usa acá.
    //    - RIN1.ItemCode == Productos.ProCodigo (~98.4% match, mismo prefijo 'V...'
    //      que el resto del sistema usa para ItemCode/ProCodigo).
    //    - Orin.U_CAUSADEV == MotivosDevolucion.MotID (columnas de collation
    //      distinta — mismo COLLATE DATABASE_DEFAULT que ya usa el resto de la app
    //      para cruzar Orin/RIN1 con tablas MovilBusiness).
    //    - RIN1.LineTotal es SIEMPRE positivo (nunca negativo, verificado en +12k
    //      líneas) y NO incluye ITBIS — mismo "monto bruto" que usa todo este
    //      controlador. Orin.DocTotal en cambio SÍ incluye ITBIS (no cuadra 1:1 con
    //      SUM(LineTotal)) — por eso "Monto NC" siempre sale de SUM(RIN1.LineTotal),
    //      nunca de Orin.DocTotal.
    //  Grano SQL = RepCodigo (siempre — para resolver Centro/Supervisor vía
    //  CargarReps, igual que Ventas()/VendCte) + CliID (solo si nivel="cliente") +
    //  Fecha (solo si porDia) + Suplidor/Categoría/Producto vía GROUPING SETS (solo
    //  si vista="detallado", aplicado al nivel más profundo elegido — el mismo
    //  patrón de VendCteSupCat, generalizado para que conviva con el grano org). El
    //  árbol de N niveles y los subtotales por grupo se arman 100% en el front
    //  (mismo espíritu que vcAgg, generalizado a más niveles).
    // ══════════════════════════════════════════════════════════════════════════
    public class NcRow
    {
        public string? Fecha; public string RepCodigo = ""; public int? CliID; public string CliCodigo = ""; public string CliNombre = "";
        public int SupID; public string Suplidor = ""; public int CatID; public string Categoria = ""; public int ProID; public string Producto = "";
        public string Nivel = "total"; // total|suplidor|categoria|producto (solo relevante si vista=detallado)
        public long Cantidad; public double Unidades, Cajas, Monto;
    }

    private static int HasCol(SqlDataReader rd, string name)
    { try { return rd.GetOrdinal(name); } catch (IndexOutOfRangeException) { return -1; } }

    private static (List<int> lCli, List<int> lSup, List<int> lCat, List<int> lPro, List<string> lGru, List<string> lGC, List<int> lTam) NcParseFiltrosProd(
        string? clientes, string? suplidores, string? categorias, string? productos, string? grupos, string? gruposCliente, string? tamanos) =>
        (ParseInts(clientes), ParseInts(suplidores), ParseInts(categorias), ParseInts(productos), ParseList(grupos), ParseList(gruposCliente), ParseInts(tamanos));

    // Consulta cruda a Orin/RIN1 — usada tanto por /notascredito (árbol org) como por
    // /notascredito-tarjetas (nivel="centro", sin detalle, sin por-dia, 2 ventanas).
    private List<NcRow> NcConsultar(SqlConnection cn, DateOnly ini, DateOnly finExcl, bool porDia, string nivel, bool detallado,
        List<string> repsOk, List<int> lCli, List<int> lSup, List<int> lCat, List<int> lPro, List<string> lGru, List<string> lGC, List<int> lTam)
    {
        var parms = new List<KeyValuePair<string, object>>();
        string In(string col, IEnumerable<object> vals, string pre)
        {
            var l = vals.ToList(); if (l.Count == 0) return "";
            var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        var wRep = repsOk.Count > 0 ? In("LTRIM(RTRIM(o.U_Repcodigo))", repsOk.Cast<object>(), "rp") : "";
        var wCli = lCli.Count > 0 ? In("cl.CliID", lCli.Cast<object>(), "cl") : "";
        var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                  + In("pr.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
        var wGC = lGC.Count > 0
            ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=cl.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})"
            : "";
        var wTam = lTam.Count > 0 ? In("ISNULL(cl.ClaID,0)", lTam.Cast<object>(), "tm") : "";
        var grupoJoin = lGru.Count > 0 ? "INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=pr.ProID" : "";

        bool porCliente = nivel == "cliente";
        var siempre = new List<string>();
        if (porDia) siempre.Add("CAST(o.DocDate AS DATE)");
        siempre.Add("LTRIM(RTRIM(o.U_Repcodigo))");
        if (porCliente) siempre.Add("cl.CliID");

        string selFecha = porDia ? "CAST(o.DocDate AS DATE) Fecha," : "";
        string selCli = porCliente ? "cl.CliID CliID, MAX(cl.CliCodigo) CliCodigo, MAX(ISNULL(cl.CliNombre,'')) CliNombre," : "";

        string sql;
        if (!detallado)
        {
            sql = $@"
SELECT {selFecha} LTRIM(RTRIM(o.U_Repcodigo)) RepCodigo, {selCli}
       COUNT(DISTINCT o.DocEntry) Cantidad, SUM(r.Quantity) Unidades,
       SUM(r.Quantity/NULLIF(pr.ProUnidades,0)) Cajas, SUM(r.LineTotal) Monto
FROM Orin o WITH (NOLOCK)
INNER JOIN RIN1 r WITH (NOLOCK) ON r.DocEntry=o.DocEntry
LEFT JOIN Productos pr WITH (NOLOCK) ON pr.ProCodigo = r.ItemCode COLLATE DATABASE_DEFAULT
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.CliCodigo = o.CardCode COLLATE DATABASE_DEFAULT
{grupoJoin}
WHERE o.DocDate>=@ini AND o.DocDate<@fin{wRep}{wCli}{wProd}{wGC}{wTam}
GROUP BY {string.Join(",", siempre)}";
        }
        else
        {
            var baseCols = string.Join(",", siempre);
            sql = $@"
SELECT {selFecha} LTRIM(RTRIM(o.U_Repcodigo)) RepCodigo, {selCli}
       ISNULL(pr.Cat3ID,0) SupID, MAX(ISNULL(s.Cat3Descripcion,'(Sin suplidor)')) Suplidor,
       pr.LinID CatID, MAX(ISNULL(ln.LinDescripcion,'(Sin categoría)')) Categoria,
       pr.ProID ProID, MAX(pr.ProDescripcion) Producto,
       COUNT(DISTINCT o.DocEntry) Cantidad, SUM(r.Quantity) Unidades,
       SUM(r.Quantity/NULLIF(pr.ProUnidades,0)) Cajas, SUM(r.LineTotal) Monto,
       GROUPING(pr.Cat3ID) gSup, GROUPING(pr.LinID) gCat, GROUPING(pr.ProID) gPro
FROM Orin o WITH (NOLOCK)
INNER JOIN RIN1 r WITH (NOLOCK) ON r.DocEntry=o.DocEntry
LEFT JOIN Productos pr WITH (NOLOCK) ON pr.ProCodigo = r.ItemCode COLLATE DATABASE_DEFAULT
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.CliCodigo = o.CardCode COLLATE DATABASE_DEFAULT
LEFT JOIN Categoria3 s WITH (NOLOCK) ON s.Cat3ID=pr.Cat3ID
LEFT JOIN Lineas ln WITH (NOLOCK) ON ln.LinID=pr.LinID
{grupoJoin}
WHERE o.DocDate>=@ini AND o.DocDate<@fin{wRep}{wCli}{wProd}{wGC}{wTam}
GROUP BY GROUPING SETS (
  ({baseCols}),
  ({baseCols}, pr.Cat3ID),
  ({baseCols}, pr.Cat3ID, pr.LinID),
  ({baseCols}, pr.Cat3ID, pr.LinID, pr.ProID)
)";
        }

        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 };
        cmd.Parameters.AddWithValue("@ini", ini.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@fin", finExcl.ToString("yyyy-MM-dd"));
        foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);

        var salida = new List<NcRow>();
        using var rd = cmd.ExecuteReader();
        int oFecha = HasCol(rd, "Fecha"), oRep = rd.GetOrdinal("RepCodigo");
        int oCli = HasCol(rd, "CliID"), oCliCod = HasCol(rd, "CliCodigo"), oCliNom = HasCol(rd, "CliNombre");
        int oSup = HasCol(rd, "SupID"), oSupN = HasCol(rd, "Suplidor"), oCat = HasCol(rd, "CatID"), oCatN = HasCol(rd, "Categoria");
        int oPro = HasCol(rd, "ProID"), oProN = HasCol(rd, "Producto");
        int oCant = rd.GetOrdinal("Cantidad"), oUni = rd.GetOrdinal("Unidades"), oCaj = rd.GetOrdinal("Cajas"), oMonto = rd.GetOrdinal("Monto");
        int oGSup = HasCol(rd, "gSup"), oGCat = HasCol(rd, "gCat"), oGPro = HasCol(rd, "gPro");
        while (rd.Read())
        {
            var row = new NcRow();
            if (oFecha >= 0 && !rd.IsDBNull(oFecha)) row.Fecha = Convert.ToDateTime(rd.GetValue(oFecha)).ToString("yyyy-MM-dd");
            row.RepCodigo = rd.IsDBNull(oRep) ? "" : rd.GetValue(oRep).ToString()!.Trim();
            if (oCli >= 0 && !rd.IsDBNull(oCli)) row.CliID = Convert.ToInt32(rd.GetValue(oCli));
            if (oCliCod >= 0 && !rd.IsDBNull(oCliCod)) row.CliCodigo = rd.GetString(oCliCod);
            if (oCliNom >= 0 && !rd.IsDBNull(oCliNom)) row.CliNombre = rd.GetString(oCliNom);
            bool gSup = oGSup >= 0 && Convert.ToInt32(rd.GetValue(oGSup)) == 1;
            bool gCat = oGCat >= 0 && Convert.ToInt32(rd.GetValue(oGCat)) == 1;
            bool gPro = oGPro >= 0 && Convert.ToInt32(rd.GetValue(oGPro)) == 1;
            if (oSup >= 0 && !gSup && !rd.IsDBNull(oSup)) row.SupID = Convert.ToInt32(rd.GetValue(oSup));
            if (oSupN >= 0 && !gSup && !rd.IsDBNull(oSupN)) row.Suplidor = rd.GetString(oSupN);
            if (oCat >= 0 && !gCat && !rd.IsDBNull(oCat)) row.CatID = Convert.ToInt32(rd.GetValue(oCat));
            if (oCatN >= 0 && !gCat && !rd.IsDBNull(oCatN)) row.Categoria = rd.GetString(oCatN);
            if (oPro >= 0 && !gPro && !rd.IsDBNull(oPro)) row.ProID = Convert.ToInt32(rd.GetValue(oPro));
            if (oProN >= 0 && !gPro && !rd.IsDBNull(oProN)) row.Producto = rd.GetString(oProN);
            row.Nivel = !detallado ? "total" : gSup ? "total" : gCat ? "suplidor" : gPro ? "categoria" : "producto";
            row.Cantidad = rd.IsDBNull(oCant) ? 0 : Convert.ToInt64(rd.GetValue(oCant));
            row.Unidades = rd.IsDBNull(oUni) ? 0 : Convert.ToDouble(rd.GetValue(oUni));
            row.Cajas = rd.IsDBNull(oCaj) ? 0 : Convert.ToDouble(rd.GetValue(oCaj));
            row.Monto = rd.IsDBNull(oMonto) ? 0 : Convert.ToDouble(rd.GetValue(oMonto));
            salida.Add(row);
        }
        return salida;
    }

    [HttpGet("notascredito")]
    public IActionResult NotasCredito(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? nivel, [FromQuery] string? vista, [FromQuery] bool porDia,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? clientes,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? gruposCliente, [FromQuery] string? tamanos)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            nivel = nivel is "centro" or "supervisor" or "vendedor" or "cliente" ? nivel : "vendedor";
            bool detallado = vista == "detallado";
            var (lCli, lSup, lCat, lPro, lGru, lGC, lTam) = NcParseFiltrosProd(clientes, suplidores, categorias, productos, grupos, gruposCliente, tamanos);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);

            var crudo = NcConsultar(cn, ini, fin.AddDays(1), porDia, nivel, detallado, repsOk, lCli, lSup, lCat, lPro, lGru, lGC, lTam);

            var filas = crudo.Select(r =>
            {
                if (!reps.TryGetValue(r.RepCodigo, out var ri)) ri = new RepInfo { Nombre = r.RepCodigo == "" ? "(Sin vendedor)" : r.RepCodigo };
                return new
                {
                    r.Fecha, RepCodigo = r.RepCodigo, ri.Nombre, ri.Centro, ri.CedCodigo, ri.SupCod, ri.Supervisor, ri.Division,
                    r.CliID, r.CliCodigo, r.CliNombre,
                    r.SupID, r.Suplidor, r.CatID, r.Categoria, r.ProID, r.Producto, r.Nivel,
                    r.Cantidad, Unidades = Math.Round(r.Unidades), Cajas = Math.Round(r.Cajas, 1), Monto = Math.Round(r.Monto)
                };
            }).ToList();

            return Ok(new { ok = true, nivel, vista = detallado ? "detallado" : "resumido", porDia, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Tarjetas KPI: siempre 2 ventanas fijas (Mes-a-la-fecha y Ayer), independientes
    // del rango elegido en los filtros de fecha de la pantalla (solo respetan los
    // filtros org/producto/cliente ya elegidos) — mismo espíritu que "Días
    // transcurridos" en Presupuestos: indicador de contexto, no del reporte ad-hoc.
    [HttpGet("notascredito-tarjetas")]
    public IActionResult NotasCreditoTarjetas(
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? clientes,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? gruposCliente, [FromQuery] string? tamanos)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (lCli, lSup, lCat, lPro, lGru, lGC, lTam) = NcParseFiltrosProd(clientes, suplidores, categorias, productos, grupos, gruposCliente, tamanos);
            var hoy = DateOnly.FromDateTime(DateTime.Today);
            var inicioMes = new DateOnly(hoy.Year, hoy.Month, 1);
            var ayer = hoy.AddDays(-1);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);
            // Ventas de comparación: mismo Tx/AggPorVendedor ya usado por Ventas() —
            // Tx no soporta filtro por Grupo Producto/Tamaño (tampoco lo soporta hoy
            // Ventas(), limitación preexistente, no nueva de este endpoint).
            var tx = ParseTx(null, suplidores, categorias, productos, gruposCliente);

            object Ventana(DateOnly ini, DateOnly finExcl)
            {
                var nc = NcConsultar(cn, ini, finExcl, false, "centro", false, repsOk, lCli, lSup, lCat, lPro, lGru, lGC, lTam);
                long cantidad = nc.Sum(r => r.Cantidad);
                double monto = nc.Sum(r => r.Monto);

                var porCentroNc = nc.GroupBy(r => reps.TryGetValue(r.RepCodigo, out var ri) ? (ri.CedCodigo, ri.Centro) : ("", "(Sin centro)"))
                    .Select(g => new { CedCodigo = g.Key.Item1, Centro = g.Key.Item2, Monto = g.Sum(x => x.Monto), Cantidad = g.Sum(x => x.Cantidad) })
                    .ToList();

                var ventasPorRep = AggPorVendedor(cn, ini, finExcl.AddDays(-1), tx);
                bool PasaRep(string cod) => repsOk.Count == 0 || repsOk.Contains(cod);
                double totalVentas = ventasPorRep.Where(kv => PasaRep(kv.Key)).Sum(kv => kv.Value.Monto);
                var porCentroVentas = ventasPorRep.Where(kv => PasaRep(kv.Key))
                    .GroupBy(kv => reps.TryGetValue(kv.Key, out var ri) ? ri.CedCodigo : "")
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Value.Monto));

                var porCentro = porCentroNc.Select(c =>
                {
                    double ventasCentro = porCentroVentas.GetValueOrDefault(c.CedCodigo);
                    return new
                    {
                        c.Centro,
                        nc = new { cantidad = c.Cantidad, monto = Math.Round(c.Monto) },
                        ventas = Math.Round(ventasCentro),
                        incidencia = ventasCentro > 0 ? Math.Round(100.0 * c.Monto / ventasCentro, 1) : (double?)null
                    };
                }).OrderByDescending(c => c.nc.monto).ToList();

                return new
                {
                    nc = new { cantidad, monto = Math.Round(monto) },
                    ventas = Math.Round(totalVentas),
                    incidencia = totalVentas > 0 ? Math.Round(100.0 * monto / totalVentas, 1) : (double?)null,
                    porCentro
                };
            }

            var mes = Ventana(inicioMes, hoy.AddDays(1));
            var ayerV = Ventana(ayer, hoy);

            return Ok(new { ok = true, mes, ayer = ayerV });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Entorno "Por Causa" (pedido explícito del usuario) — ranking por
    // MotivosDevolucion vía Orin.U_CAUSADEV, con el mismo rango/filtros de la
    // pantalla. % siempre calculado contra el total general del resultado (no
    // varía por día aunque porDia esté activo, para no tener un denominador que
    // cambia fila a fila sin pedirlo explícitamente).
    [HttpGet("notascredito-causas")]
    public IActionResult NotasCreditoCausas(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin, [FromQuery] bool porDia,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? clientes,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? gruposCliente, [FromQuery] string? tamanos)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            var (lCli, lSup, lCat, lPro, lGru, lGC, lTam) = NcParseFiltrosProd(clientes, suplidores, categorias, productos, grupos, gruposCliente, tamanos);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);

            var parms = new List<KeyValuePair<string, object>>();
            string In(string col, IEnumerable<object> vals, string pre)
            {
                var l = vals.ToList(); if (l.Count == 0) return "";
                var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
                return $" AND {col} IN ({string.Join(",", names)})";
            }
            var wRep = repsOk.Count > 0 ? In("LTRIM(RTRIM(o.U_Repcodigo))", repsOk.Cast<object>(), "rp") : "";
            var wCli = lCli.Count > 0 ? In("cl.CliID", lCli.Cast<object>(), "cl") : "";
            var wProd = In("pr.Cat3ID", lSup.Cast<object>(), "sp") + In("pr.LinID", lCat.Cast<object>(), "ct")
                      + In("pr.ProID", lPro.Cast<object>(), "pd") + In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
            var wGC = lGC.Count > 0
                ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=cl.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})"
                : "";
            var wTam = lTam.Count > 0 ? In("ISNULL(cl.ClaID,0)", lTam.Cast<object>(), "tm") : "";
            var grupoJoin = lGru.Count > 0 ? "INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=pr.ProID" : "";

            string selFecha = porDia ? "CAST(o.DocDate AS DATE) Fecha," : "";
            string grpFecha = porDia ? "CAST(o.DocDate AS DATE)," : "";

            var sql = $@"
SELECT {selFecha} o.U_CAUSADEV MotID,
       COUNT(DISTINCT o.DocEntry) Cantidad, SUM(r.Quantity) Unidades,
       SUM(r.Quantity/NULLIF(pr.ProUnidades,0)) Cajas, SUM(r.LineTotal) Monto
FROM Orin o WITH (NOLOCK)
INNER JOIN RIN1 r WITH (NOLOCK) ON r.DocEntry=o.DocEntry
LEFT JOIN Productos pr WITH (NOLOCK) ON pr.ProCodigo = r.ItemCode COLLATE DATABASE_DEFAULT
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.CliCodigo = o.CardCode COLLATE DATABASE_DEFAULT
{grupoJoin}
WHERE o.DocDate>=@ini AND o.DocDate<@fin{wRep}{wCli}{wProd}{wGC}{wTam}
GROUP BY {grpFecha} o.U_CAUSADEV";
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
            cmd.Parameters.AddWithValue("@ini", ini.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@fin", fin.AddDays(1).ToString("yyyy-MM-dd"));
            foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);

            var motivos = new Dictionary<int, string>();
            using (var cmdM = new SqlCommand("SELECT MotID, MotDescripcion FROM MotivosDevolucion WITH (NOLOCK)", cn) { CommandTimeout = 30 })
            using (var rdM = cmdM.ExecuteReader())
                while (rdM.Read()) if (!rdM.IsDBNull(0)) motivos[rdM.GetInt32(0)] = rdM.IsDBNull(1) ? "" : rdM.GetString(1);

            var crudo = new List<(string? fecha, string motId, long cantidad, double unidades, double cajas, double monto)>();
            using (var rd = cmd.ExecuteReader())
            {
                int oFecha = HasCol(rd, "Fecha");
                int oMot = rd.GetOrdinal("MotID");
                int oCant = rd.GetOrdinal("Cantidad"), oUni = rd.GetOrdinal("Unidades"), oCaj = rd.GetOrdinal("Cajas"), oMonto = rd.GetOrdinal("Monto");
                while (rd.Read())
                {
                    string? fecha = oFecha >= 0 && !rd.IsDBNull(oFecha) ? Convert.ToDateTime(rd.GetValue(oFecha)).ToString("yyyy-MM-dd") : null;
                    string motRaw = rd.IsDBNull(oMot) ? "" : rd.GetValue(oMot).ToString()!.Trim();
                    long cantidad = rd.IsDBNull(oCant) ? 0 : Convert.ToInt64(rd.GetValue(oCant));
                    double unidades = rd.IsDBNull(oUni) ? 0 : Convert.ToDouble(rd.GetValue(oUni));
                    double cajas = rd.IsDBNull(oCaj) ? 0 : Convert.ToDouble(rd.GetValue(oCaj));
                    double monto = rd.IsDBNull(oMonto) ? 0 : Convert.ToDouble(rd.GetValue(oMonto));
                    crudo.Add((fecha, motRaw, cantidad, unidades, cajas, monto));
                }
            }

            double totalGeneral = crudo.Sum(c => c.monto);
            var filas = crudo.Select(c =>
            {
                int.TryParse(c.motId, out var motId);
                string motNom = c.motId == "" ? "(Sin causa registrada)" : motivos.GetValueOrDefault(motId, "(Causa " + c.motId + ")");
                return new
                {
                    Fecha = c.fecha, MotID = c.motId, Motivo = motNom, Cantidad = c.cantidad,
                    Unidades = Math.Round(c.unidades), Cajas = Math.Round(c.cajas, 1), Monto = Math.Round(c.monto),
                    Pct = totalGeneral > 0 ? Math.Round(100.0 * c.monto / totalGeneral, 1) : (double?)null
                };
            }).OrderByDescending(f => f.Monto).ToList();

            return Ok(new { ok = true, total = Math.Round(totalGeneral), filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PED-FACT-NC (pestaña "Ped-Fact-NC")
    //
    //  Historia (para no repetir el mismo error): el primer intento (2026-08-27,
    //  2ª parte) amarraba la NC vía EntregasRepartidorDetalle.NCNumeroERPDocum =
    //  VWVentas.VenReferencia — se verificó FALSO en vivo (el usuario notó
    //  productos de la NC que no estaban en el pedido; confirmado sistemático:
    //  0 de 110 NC de una muestra de un día completo tenían el mismo CliID que su
    //  pedido, todas resolvían por coincidencia numérica a transacciones no
    //  relacionadas de sept-2023). Se descartaron también `Devoluciones`/
    //  `DevolucionesDetalle` (solo 8 filas, sin usar) y
    //  `PedidosVsVentasyNCULTIMOS60DIAS` (congelada desde 2019 pese al nombre).
    //  Solución (indicada por el usuario, 2026-08-27 4ª parte): partir 100% de la
    //  vista `VwPedidosVsFacutadosMenosDevolucionRM` — ya trae Pedido/Facturado/
    //  Devuelto/Neto reconciliados por TI a nivel de línea de producto (ver
    //  GatherPedFactNcV2 abajo). Esa vista NO expone documentos individuales (ni
    //  # de facturas ni # de NC como tal) — el grano es (EnrSecuencia,
    //  PedSecuencia, ProId), así que #Fact/#NC/#FactNETAS significan "# de
    //  PEDIDOS facturados/con devolución/netos", no "# de documentos". Tampoco
    //  expone fecha de pedido, solo EnrFecha (fecha de ENTREGA) — Ped-Fact-NC
    //  filtra por fecha de entrega, no de pedido (pedido del usuario: "parte
    //  desde las entregas"). El mecanismo viejo (GatherPedFactNc, Pedido→Factura
    //  vía PedNumeroERP/VenReferencia) y su bridge NC se ELIMINARON por completo
    //  (2026-08-28) — Fill Rate ya no los necesita, ver GatherFillRate abajo, que
    //  usa VWPedidosDetalle directo (PedCantidad/PedCantidadDetalle) y es mucho
    //  más barato en recursos.
    // ══════════════════════════════════════════════════════════════════════════
    // Grano real: (EnrSecuencia/RepCodigo/PedSecuencia/ProId) — 1 fila por línea de
    // producto de un pedido dentro de una entrega. MontoPed/MontoFact/MontoDev/
    // MontoNeto ya vienen reconciliados por TI (MontoBrutoNetoEntregado = Facturado
    // − Devuelto, verificado en vivo con casos reales). Sin ITBIS (mismo patrón
    // "Monto Bruto" del resto del módulo — la vista tiene aparte
    // MontoConITBSNetoEntregado, que no se usa aquí).
    private record VpfLinea(string RepCodigo, string PedSecuencia, int SupID, string Suplidor, int CatID, string Categoria, int ProID, string Producto,
        int CliID, string CliCodigo, string CliNombre,
        double MontoPed, double MontoFact, double MontoDev, double MontoNeto);

    private List<VpfLinea> GatherPedFactNcV2(SqlConnection cn, DateOnly ini, DateOnly fin, List<string> repsOk, List<int> lSup, List<int> lCat, List<int> lPro, List<int>? lEnr = null)
    {
        lEnr ??= new List<int>();
        var iniS = ini.ToString("yyyy-MM-dd"); var finS = fin.AddDays(1).ToString("yyyy-MM-dd");

        var parms = new List<KeyValuePair<string, object>>();
        string In(string col, IEnumerable<object> vals, string pre)
        {
            var l = vals.ToList(); if (l.Count == 0) return "";
            var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        var wRep = repsOk.Count > 0 ? In("v.CodVendedor", repsOk.Cast<object>(), "rp") : "";
        var wSup = In("pr.Cat3ID", lSup.Cast<object>(), "sp");
        var wCat = In("pr.LinID", lCat.Cast<object>(), "ct");
        var wPro = In("v.ProId", lPro.Cast<object>(), "pd");
        var wEnr = lEnr.Count > 0 ? In("v.EnrSecuencia", lEnr.Cast<object>(), "en") : "";

        // CliID/CliCodigo ya vienen en la vista; CliNombre hace falta traerlo de Clientes
        // (join por CliID — NUNCA por nombre, para no solapar clientes homónimos).
        var sql = $@"
SELECT v.CodVendedor, v.PedSecuencia,
       ISNULL(pr.Cat3ID,0) SupID, ISNULL(v.Suplidor,'(Sin suplidor)') Suplidor,
       ISNULL(pr.LinID,0) CatID, ISNULL(ln.LinDescripcion,'(Sin categoría)') Categoria,
       v.ProId, v.ProDescripcion,
       v.CliID, ISNULL(v.CliCodigo,CAST(v.CliID AS varchar(20))) CliCodigo, ISNULL(cl.CliNombre,'(Cliente '+CAST(v.CliID AS varchar(20))+')') CliNombre,
       SUM(v.MontoBrutoPedido) MontoPed, SUM(v.MontoBrutoFacturado) MontoFact,
       SUM(v.MontoBrutoDevuelto) MontoDev, SUM(v.MontoBrutoNetoEntregado) MontoNeto
FROM VwPedidosVsFacutadosMenosDevolucionRM v WITH (NOLOCK)
LEFT JOIN Productos pr WITH (NOLOCK) ON pr.ProID=v.ProId
LEFT JOIN Lineas ln WITH (NOLOCK) ON ln.LinID=pr.LinID
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.CliID=v.CliID
WHERE v.EnrFecha>=@ini AND v.EnrFecha<@fin AND v.PedSecuencia IS NOT NULL{wRep}{wSup}{wCat}{wPro}{wEnr}
GROUP BY v.CodVendedor, v.PedSecuencia, pr.Cat3ID, v.Suplidor, pr.LinID, ln.LinDescripcion, v.ProId, v.ProDescripcion,
         v.CliID, v.CliCodigo, cl.CliNombre";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 };
        cmd.Parameters.AddWithValue("@ini", iniS); cmd.Parameters.AddWithValue("@fin", finS);
        foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);

        var lineas = new List<VpfLinea>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
            lineas.Add(new VpfLinea(
                rd.GetValue(0).ToString()!.Trim(), rd.GetValue(1).ToString()!.Trim(),
                rd.GetInt32(2), rd.GetString(3), rd.GetInt32(4), rd.GetString(5),
                rd.IsDBNull(6) ? 0 : rd.GetInt32(6), rd.IsDBNull(7) ? "" : rd.GetString(7),
                rd.IsDBNull(8) ? 0 : rd.GetInt32(8), rd.IsDBNull(9) ? "" : rd.GetString(9), rd.IsDBNull(10) ? "" : rd.GetString(10),
                rd.IsDBNull(11) ? 0 : Convert.ToDouble(rd.GetValue(11)),
                rd.IsDBNull(12) ? 0 : Convert.ToDouble(rd.GetValue(12)),
                rd.IsDBNull(13) ? 0 : Convert.ToDouble(rd.GetValue(13)),
                rd.IsDBNull(14) ? 0 : Convert.ToDouble(rd.GetValue(14))));
        }
        return lineas;
    }

    // "Facts"/"Ncs"/"Netas" son ahora conteos de PEDIDOS (facturados/con devolución/
    // netos), no de documentos — la vista fuente no expone facturas ni NC individuales.
    private class PfnAgg
    {
        public HashSet<string> Peds = new();
        public double MontoPed;
        public HashSet<string> Facts = new();
        public double MontoFact;
        public HashSet<string> Ncs = new();
        public double MontoNc;
        public HashSet<string> Netas = new();
        public double MontoNeto;
    }


    [HttpGet("pedfactnc")]
    public IActionResult PedFactNc(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? entregas)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos); var lEnr = ParseInts(entregas);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);

            var lineas = GatherPedFactNcV2(cn, ini, fin, repsOk, lSup, lCat, lPro, lEnr);

            // Pre-agregar a nivel de PEDIDO completo (puede tener varias líneas de
            // producto) para decidir si ese pedido, en conjunto, quedó facturado/con
            // devolución/neto — evita que 2 líneas del mismo pedido lo cuenten 2 veces.
            var porPedido = lineas.GroupBy(x => x.RepCodigo + "|" + x.PedSecuencia).Select(g => new
            {
                Rep = g.First().RepCodigo, Ped = g.First().PedSecuencia,
                MontoPed = g.Sum(x => x.MontoPed), MontoFact = g.Sum(x => x.MontoFact),
                MontoDev = g.Sum(x => x.MontoDev), MontoNeto = g.Sum(x => x.MontoNeto)
            });

            var porRep = new Dictionary<string, PfnAgg>();
            PfnAgg Agg(string key) { if (!porRep.TryGetValue(key, out var a)) { a = new(); porRep[key] = a; } return a; }
            foreach (var p in porPedido)
            {
                var a = Agg(p.Rep);
                a.Peds.Add(p.Ped); a.MontoPed += p.MontoPed;
                a.MontoFact += p.MontoFact; a.MontoNc += p.MontoDev; a.MontoNeto += p.MontoNeto;
                if (p.MontoFact != 0) a.Facts.Add(p.Ped);
                if (p.MontoDev != 0) a.Ncs.Add(p.Ped);
                if (p.MontoNeto > 0) a.Netas.Add(p.Ped);
            }

            var filas = porRep.Select(kv =>
            {
                var rep = kv.Key; var a = kv.Value;
                reps.TryGetValue(rep, out var ri); ri ??= new RepInfo { Nombre = rep };
                return new
                {
                    RepCodigo = rep, ri.Nombre, ri.Centro, ri.CedCodigo, ri.SupCod, ri.Supervisor, ri.Division,
                    NumPed = a.Peds.Count, ValorPed = Math.Round(a.MontoPed),
                    NumFact = a.Facts.Count, ValorFact = Math.Round(a.MontoFact),
                    NumNC = a.Ncs.Count, ValorNC = Math.Round(a.MontoNc),
                    NumFactNetas = a.Netas.Count, ValorNeto = Math.Round(a.MontoNeto)
                };
            }).ToList();

            return Ok(new { ok = true, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apihist/pedfactnc-cliente ──────────────────────────────────────
    // Nivel Cliente (pedido explícito del usuario 2026-09-04): lista, uno por uno, los
    // clientes que pidieron — grano (RepCodigo, CliID), NUNCA agrupado por nombre de
    // cliente (2 clientes homónimos con distinto CliID nunca deben solaparse). Mismos
    // filtros que /pedfactnc; se usa tanto para "Por Cliente" a nivel compañía/centro/
    // supervisor como al perforar (drill) desde un vendedor específico.
    [HttpGet("pedfactnc-cliente")]
    public IActionResult PedFactNcCliente(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? entregas)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos); var lEnr = ParseInts(entregas);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);

            var lineas = GatherPedFactNcV2(cn, ini, fin, repsOk, lSup, lCat, lPro, lEnr);

            // Totales por PEDIDO completo (todas sus líneas de producto) para decidir
            // facturado/con devolución/neto una vez por pedido, igual que /pedfactnc.
            var porPedido = lineas.GroupBy(x => x.RepCodigo + "|" + x.PedSecuencia).Select(g => new
            {
                Rep = g.First().RepCodigo, Ped = g.First().PedSecuencia,
                CliID = g.First().CliID, CliCodigo = g.First().CliCodigo, CliNombre = g.First().CliNombre,
                MontoPed = g.Sum(x => x.MontoPed), MontoFact = g.Sum(x => x.MontoFact),
                MontoDev = g.Sum(x => x.MontoDev), MontoNeto = g.Sum(x => x.MontoNeto)
            });

            var nombres = new Dictionary<string, (int CliID, string CliCodigo, string CliNombre)>();
            var porCli = new Dictionary<string, PfnAgg>();
            PfnAgg Agg(string key) { if (!porCli.TryGetValue(key, out var a)) { a = new(); porCli[key] = a; } return a; }
            foreach (var p in porPedido)
            {
                var key = p.Rep + "|" + p.CliID;
                if (!nombres.ContainsKey(key)) nombres[key] = (p.CliID, p.CliCodigo, p.CliNombre);
                var a = Agg(key);
                a.Peds.Add(p.Ped); a.MontoPed += p.MontoPed;
                a.MontoFact += p.MontoFact; a.MontoNc += p.MontoDev; a.MontoNeto += p.MontoNeto;
                if (p.MontoFact != 0) a.Facts.Add(p.Ped);
                if (p.MontoDev != 0) a.Ncs.Add(p.Ped);
                if (p.MontoNeto > 0) a.Netas.Add(p.Ped);
            }

            var filas = porCli.Select(kv =>
            {
                var partes = kv.Key.Split('|', 2);
                var rep = partes[0];
                var (cliId, cliCodigo, cliNombre) = nombres[kv.Key];
                var a = kv.Value;
                reps.TryGetValue(rep, out var ri); ri ??= new RepInfo { Nombre = rep };
                return new
                {
                    RepCodigo = rep, ri.Nombre, ri.Centro, ri.CedCodigo, ri.SupCod, ri.Supervisor, ri.Division,
                    CliID = cliId, CliCodigo = cliCodigo, CliNombre = cliNombre,
                    NumPed = a.Peds.Count, ValorPed = Math.Round(a.MontoPed),
                    NumFact = a.Facts.Count, ValorFact = Math.Round(a.MontoFact),
                    NumNC = a.Ncs.Count, ValorNC = Math.Round(a.MontoNc),
                    NumFactNetas = a.Netas.Count, ValorNeto = Math.Round(a.MontoNeto)
                };
            }).ToList();

            return Ok(new { ok = true, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    [HttpGet("pedfactnc-supcat")]
    public IActionResult PedFactNcSupCat(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? entregas)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos); var lEnr = ParseInts(entregas);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);

            var lineas = GatherPedFactNcV2(cn, ini, fin, repsOk, lSup, lCat, lPro, lEnr);

            // Totales por PEDIDO completo (todas sus líneas), para decidir facturado/
            // con devolución/neto UNA vez por pedido aunque tenga varias líneas.
            var porPedidoTotal = lineas.GroupBy(x => x.RepCodigo + "|" + x.PedSecuencia)
                .ToDictionary(g => g.Key, g => (MontoFact: g.Sum(x => x.MontoFact), MontoDev: g.Sum(x => x.MontoDev), MontoNeto: g.Sum(x => x.MontoNeto)));

            var nombres = new Dictionary<string, (int SupID, string Suplidor, int CatID, string Categoria, int ProID, string Producto)>();
            var porSup = new Dictionary<string, PfnAgg>();
            var porCat = new Dictionary<string, PfnAgg>();
            var porPro = new Dictionary<string, PfnAgg>();
            var porTotal = new PfnAgg(); // gran total (footer exacto cuando no hay scope de Suplidor)
            string KSup(int s) => s.ToString();
            string KCat(int s, int c) => s + "|" + c;
            string KPro(int s, int c, int p) => s + "|" + c + "|" + p;
            void Nombra(string k, int s, string sn, int c, string cn2, int p, string pn)
            {
                if (!nombres.ContainsKey(k)) nombres[k] = (s, sn, c, cn2, p, pn);
            }
            PfnAgg AggIn(Dictionary<string, PfnAgg> map, string key) { if (!map.TryGetValue(key, out var a)) { a = new(); map[key] = a; } return a; }

            foreach (var ln in lineas)
            {
                var pedKey = ln.RepCodigo + "|" + ln.PedSecuencia;
                var tot = porPedidoTotal[pedKey];
                var ks = KSup(ln.SupID); var kc = KCat(ln.SupID, ln.CatID); var kp = KPro(ln.SupID, ln.CatID, ln.ProID);
                Nombra(ks, ln.SupID, ln.Suplidor, 0, "", 0, ""); Nombra(kc, ln.SupID, ln.Suplidor, ln.CatID, ln.Categoria, 0, ""); Nombra(kp, ln.SupID, ln.Suplidor, ln.CatID, ln.Categoria, ln.ProID, ln.Producto);
                foreach (var (map, k) in new[] { (porSup, ks), (porCat, kc), (porPro, kp) })
                {
                    var a = AggIn(map, k);
                    a.MontoPed += ln.MontoPed; a.MontoFact += ln.MontoFact; a.MontoNc += ln.MontoDev; a.MontoNeto += ln.MontoNeto;
                    if (a.Peds.Add(pedKey))
                    {
                        if (tot.MontoFact != 0) a.Facts.Add(pedKey);
                        if (tot.MontoDev != 0) a.Ncs.Add(pedKey);
                        if (tot.MontoNeto > 0) a.Netas.Add(pedKey);
                    }
                }
                porTotal.MontoPed += ln.MontoPed; porTotal.MontoFact += ln.MontoFact; porTotal.MontoNc += ln.MontoDev; porTotal.MontoNeto += ln.MontoNeto;
                if (porTotal.Peds.Add(pedKey))
                {
                    if (tot.MontoFact != 0) porTotal.Facts.Add(pedKey);
                    if (tot.MontoDev != 0) porTotal.Ncs.Add(pedKey);
                    if (tot.MontoNeto > 0) porTotal.Netas.Add(pedKey);
                }
            }

            var filas = new List<object>();
            void Volcar(Dictionary<string, PfnAgg> map, string nivel)
            {
                foreach (var (k, a) in map)
                {
                    var (s, sn, c, cn2, p, pn) = nombres[k];
                    filas.Add(new
                    {
                        Nivel = nivel, SupID = s, Suplidor = sn, CatID = c, Categoria = cn2, ProID = p, Producto = pn,
                        NumPed = a.Peds.Count, ValorPed = Math.Round(a.MontoPed),
                        NumFact = a.Facts.Count, ValorFact = Math.Round(a.MontoFact),
                        NumNC = a.Ncs.Count, ValorNC = Math.Round(a.MontoNc),
                        NumFactNetas = a.Netas.Count, ValorNeto = Math.Round(a.MontoNeto)
                    });
                }
            }
            Volcar(porSup, "suplidor"); Volcar(porCat, "categoria"); Volcar(porPro, "producto");
            filas.Add(new
            {
                Nivel = "total", SupID = 0, Suplidor = "", CatID = 0, Categoria = "", ProID = 0, Producto = "",
                NumPed = porTotal.Peds.Count, ValorPed = Math.Round(porTotal.MontoPed),
                NumFact = porTotal.Facts.Count, ValorFact = Math.Round(porTotal.MontoFact),
                NumNC = porTotal.Ncs.Count, ValorNC = Math.Round(porTotal.MontoNc),
                NumFactNetas = porTotal.Netas.Count, ValorNeto = Math.Round(porTotal.MontoNeto)
            });

            return Ok(new { ok = true, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  FILL RATE (pestaña "Fill Rate")
    //  % de lo pedido que se facturó (Unidades y Valor). Simplificado a pedido del
    //  usuario (2026-08-28): TODO sale de UNA sola tabla, VWPedidosDetalle —
    //  PedCantidad = lo que el cliente pidió, PedCantidadDetalle = lo que ya se
    //  facturó de esa línea (verificado en vivo contra el cálculo anterior vía
    //  VWVentas+bridge: 43,228 unidades pedidas coincide EXACTO; 42,033 vs 42,597
    //  facturadas, diferencia pequeña esperada por timing/sincronización — el
    //  usuario prefiere esto por ser 1 sola consulta, sin bridge, sin chunking,
    //  sin tocar VWVentas). Se quitó "Días Promedio de Facturación" (pedido
    //  explícito del usuario): esa columna necesitaba la fecha real de la
    //  factura, que no vive en VWPedidosDetalle — hubiera obligado a mantener el
    //  camino caro que se quiso evitar.
    // ══════════════════════════════════════════════════════════════════════════
    private record FrLinea(string RepCodigo, string PedSecuencia, int SupID, string Suplidor, int CatID, string Categoria, int ProID, string Producto,
        double UnidPed, double UnidFact, double MontoPed, double MontoFact);

    private class FrAgg
    {
        public HashSet<string> Peds = new();
        public double UnidPed, UnidFact, MontoPed, MontoFact;
    }

    private List<FrLinea> GatherFillRate(SqlConnection cn, DateOnly ini, DateOnly fin, List<string> repsOk, List<int> lSup, List<int> lCat, List<int> lPro, List<int>? lEnr = null)
    {
        lEnr ??= new List<int>();
        var iniS = ini.ToString("yyyy-MM-dd"); var finS = fin.AddDays(1).ToString("yyyy-MM-dd");

        var parms = new List<KeyValuePair<string, object>>();
        string In(string col, IEnumerable<object> vals, string pre)
        {
            var l = vals.ToList(); if (l.Count == 0) return "";
            var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        var wRep = repsOk.Count > 0 ? In("p.RepCodigo", repsOk.Cast<object>(), "rp") : "";
        var wSup = In("pr.Cat3ID", lSup.Cast<object>(), "sp");
        var wCat = In("pr.LinID", lCat.Cast<object>(), "ct");
        var wPro = In("pd.ProID", lPro.Cast<object>(), "pd");
        var wEnr = lEnr.Count > 0
            ? $" AND EXISTS (SELECT 1 FROM EntregasRepartidorDetalle erdx WITH (NOLOCK) WHERE erdx.venNumeroERPDocum=p.PedNumeroERP{In("erdx.EnrSecuencia", lEnr.Cast<object>(), "en")})"
            : "";

        // Pedido explícito del usuario 2026-08-29: los pedidos en PedEstatus 0/2/3/7/8
        // (anulado/rechazado/etc. — no llegan a despacharse) no se consideran para
        // Fill Rate, ni en el numerador (Facturado) ni en el denominador (Pedido).
        var sql = $@"
SELECT p.RepCodigo, p.PedSecuencia,
       ISNULL(pr.Cat3ID,0) SupID, ISNULL(s.Cat3Descripcion,'(Sin suplidor)') Suplidor,
       ISNULL(pr.LinID,0) CatID, ISNULL(ln.LinDescripcion,'(Sin categoría)') Categoria,
       pd.ProID, pr.ProDescripcion Producto,
       SUM(pd.PedCantidad) UnidPed, SUM(pd.PedCantidadDetalle) UnidFact,
       SUM(pd.PedCantidad*(pd.PedPrecio-ISNULL(pd.PedDescuento,0))) MontoPed,
       SUM(pd.PedCantidadDetalle*(pd.PedPrecio-ISNULL(pd.PedDescuento,0))) MontoFact
FROM VWPedidos p WITH (NOLOCK)
INNER JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia=p.PedSecuencia AND pd.RepCodigo=p.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=pd.ProID
LEFT JOIN Categoria3 s WITH (NOLOCK) ON s.Cat3ID=pr.Cat3ID
LEFT JOIN Lineas ln WITH (NOLOCK) ON ln.LinID=pr.LinID
WHERE p.PedFecha>=@ini AND p.PedFecha<@fin AND p.PedEstatus NOT IN (0,2,3,7,8){wRep}{wSup}{wCat}{wPro}{wEnr}
GROUP BY p.RepCodigo, p.PedSecuencia, pr.Cat3ID, s.Cat3Descripcion, pr.LinID, ln.LinDescripcion, pd.ProID, pr.ProDescripcion";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@ini", iniS); cmd.Parameters.AddWithValue("@fin", finS);
        foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);

        var lineas = new List<FrLinea>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
            lineas.Add(new FrLinea(
                rd.GetValue(0).ToString()!.Trim(), rd.GetValue(1).ToString()!.Trim(),
                rd.GetInt32(2), rd.GetString(3), rd.GetInt32(4), rd.GetString(5),
                rd.IsDBNull(6) ? 0 : rd.GetInt32(6), rd.IsDBNull(7) ? "" : rd.GetString(7),
                rd.IsDBNull(8) ? 0 : Convert.ToDouble(rd.GetValue(8)),
                rd.IsDBNull(9) ? 0 : Convert.ToDouble(rd.GetValue(9)),
                rd.IsDBNull(10) ? 0 : Convert.ToDouble(rd.GetValue(10)),
                rd.IsDBNull(11) ? 0 : Convert.ToDouble(rd.GetValue(11))));
        }
        return lineas;
    }

    [HttpGet("fillrate")]
    public IActionResult FillRate(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? canales,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? entregas)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos); var lEnr = ParseInts(entregas);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones, canales);

            var lineas = GatherFillRate(cn, ini, fin, repsOk, lSup, lCat, lPro, lEnr);

            var porRep = new Dictionary<string, FrAgg>();
            FrAgg Agg(string key) { if (!porRep.TryGetValue(key, out var a)) { a = new(); porRep[key] = a; } return a; }
            foreach (var ln in lineas)
            {
                var a = Agg(ln.RepCodigo);
                a.Peds.Add(ln.PedSecuencia);
                a.UnidPed += ln.UnidPed; a.UnidFact += ln.UnidFact; a.MontoPed += ln.MontoPed; a.MontoFact += ln.MontoFact;
            }

            var filas = porRep.Select(kv =>
            {
                var rep = kv.Key; var a = kv.Value;
                reps.TryGetValue(rep, out var ri); ri ??= new RepInfo { Nombre = rep };
                return new
                {
                    RepCodigo = rep, ri.Nombre, ri.Centro, ri.CedCodigo, ri.SupCod, ri.Supervisor, ri.Division, ri.CanID,
                    NumPed = a.Peds.Count,
                    UnidPed = Math.Round(a.UnidPed, 1), UnidFact = Math.Round(a.UnidFact, 1),
                    NoDespachado = Math.Round(a.UnidPed - a.UnidFact, 1),
                    FillRateUnid = a.UnidPed > 0 ? Math.Round(a.UnidFact / a.UnidPed * 100, 1) : 0,
                    ValorPed = Math.Round(a.MontoPed), ValorFact = Math.Round(a.MontoFact),
                    FillRateValor = a.MontoPed > 0 ? Math.Round(a.MontoFact / a.MontoPed * 100, 1) : 0
                };
            }).ToList();

            return Ok(new { ok = true, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    [HttpGet("fillrate-supcat")]
    public IActionResult FillRateSupCat(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? canales,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? entregas)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos); var lEnr = ParseInts(entregas);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones, canales);

            var lineas = GatherFillRate(cn, ini, fin, repsOk, lSup, lCat, lPro, lEnr);

            var nombres = new Dictionary<string, (int SupID, string Suplidor, int CatID, string Categoria, int ProID, string Producto)>();
            var porSup = new Dictionary<string, FrAgg>();
            var porCat = new Dictionary<string, FrAgg>();
            var porPro = new Dictionary<string, FrAgg>();
            var porTotal = new FrAgg();
            string KSup(int s) => s.ToString();
            string KCat(int s, int c) => s + "|" + c;
            string KPro(int s, int c, int p) => s + "|" + c + "|" + p;
            void Nombra(string k, int s, string sn, int c, string cn2, int p, string pn) { if (!nombres.ContainsKey(k)) nombres[k] = (s, sn, c, cn2, p, pn); }
            FrAgg AggIn(Dictionary<string, FrAgg> map, string key) { if (!map.TryGetValue(key, out var a)) { a = new(); map[key] = a; } return a; }

            foreach (var ln in lineas)
            {
                var pedKey = ln.RepCodigo + "|" + ln.PedSecuencia;
                var ks = KSup(ln.SupID); var kc = KCat(ln.SupID, ln.CatID); var kp = KPro(ln.SupID, ln.CatID, ln.ProID);
                Nombra(ks, ln.SupID, ln.Suplidor, 0, "", 0, ""); Nombra(kc, ln.SupID, ln.Suplidor, ln.CatID, ln.Categoria, 0, ""); Nombra(kp, ln.SupID, ln.Suplidor, ln.CatID, ln.Categoria, ln.ProID, ln.Producto);
                foreach (var (map, k) in new[] { (porSup, ks), (porCat, kc), (porPro, kp) })
                {
                    var a = AggIn(map, k);
                    a.Peds.Add(pedKey);
                    a.UnidPed += ln.UnidPed; a.UnidFact += ln.UnidFact; a.MontoPed += ln.MontoPed; a.MontoFact += ln.MontoFact;
                }
                porTotal.Peds.Add(pedKey);
                porTotal.UnidPed += ln.UnidPed; porTotal.UnidFact += ln.UnidFact; porTotal.MontoPed += ln.MontoPed; porTotal.MontoFact += ln.MontoFact;
            }

            var filas = new List<object>();
            void Volcar(Dictionary<string, FrAgg> map, string nivel)
            {
                foreach (var (k, a) in map)
                {
                    var (s, sn, c, cn2, p, pn) = nombres[k];
                    filas.Add(new
                    {
                        Nivel = nivel, SupID = s, Suplidor = sn, CatID = c, Categoria = cn2, ProID = p, Producto = pn,
                        NumPed = a.Peds.Count,
                        UnidPed = Math.Round(a.UnidPed, 1), UnidFact = Math.Round(a.UnidFact, 1),
                        NoDespachado = Math.Round(a.UnidPed - a.UnidFact, 1),
                        FillRateUnid = a.UnidPed > 0 ? Math.Round(a.UnidFact / a.UnidPed * 100, 1) : 0,
                        ValorPed = Math.Round(a.MontoPed), ValorFact = Math.Round(a.MontoFact),
                        FillRateValor = a.MontoPed > 0 ? Math.Round(a.MontoFact / a.MontoPed * 100, 1) : 0
                    });
                }
            }
            Volcar(porSup, "suplidor"); Volcar(porCat, "categoria"); Volcar(porPro, "producto");
            filas.Add(new
            {
                Nivel = "total", SupID = 0, Suplidor = "", CatID = 0, Categoria = "", ProID = 0, Producto = "",
                NumPed = porTotal.Peds.Count,
                UnidPed = Math.Round(porTotal.UnidPed, 1), UnidFact = Math.Round(porTotal.UnidFact, 1),
                NoDespachado = Math.Round(porTotal.UnidPed - porTotal.UnidFact, 1),
                FillRateUnid = porTotal.UnidPed > 0 ? Math.Round(porTotal.UnidFact / porTotal.UnidPed * 100, 1) : 0,
                ValorPed = Math.Round(porTotal.MontoPed), ValorFact = Math.Round(porTotal.MontoFact),
                FillRateValor = porTotal.MontoPed > 0 ? Math.Round(porTotal.MontoFact / porTotal.MontoPed * 100, 1) : 0
            });

            return Ok(new { ok = true, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PEDIDOS (pestaña "Pedidos", pedido explícito 2026-09-15)
    //
    //  Fuente: VWPedidos+VWPedidosDetalle, MISMA fórmula que "Fill Rate"
    //  (MontoPed=SUM(PedCantidad*(PedPrecio-Descuento)), MontoFact=SUM(PedCantidadDetalle*(...))),
    //  pero SIN el filtro de estatus hardcodeado de Fill Rate — acá el estatus es un
    //  filtro elegido en pantalla (catálogo real `Estados` WHERE EstTabla='Pedidos',
    //  verificado en vivo: 2=Transmitido, 7=Pendiente Autorización, 11=Facturado y
    //  Enviado al ERP — default confirmado con el usuario). Se agrega CanID (canal del
    //  CLIENTE vía Clientes, no el canal del rep) y PedEstatus al grano de línea para
    //  poder resolver los 4 modos de "Analizar por" sin 4 queries distintas.
    //  "Incidencia %" = (MontoPedido-MontoFacturado)/MontoPedido*100, SIEMPRE calculada
    //  DESPUÉS de sumar Pedido/Facturado a cada nivel — nunca sumada/promediada directo.
    // ══════════════════════════════════════════════════════════════════════════
    // Fecha/CliID/CliCodigo/CliNombre agregados 2026-09-27 (pedido explícito: drill-down
    // Fecha→Cliente en la pestaña Pedidos) — p.PedFecha ya se filtraba en el WHERE y 'cl'
    // ya estaba joineado (para CanID); no hizo falta ningún JOIN nuevo, solo pedir más
    // columnas del mismo alias.
    private record PdLinea(string RepCodigo, string PedSecuencia, int CanID, int PedEstatus,
        int SupID, string Suplidor, int CatID, string Categoria, int ProID, string Producto,
        double MontoPed, double MontoFact, string Fecha, int CliID, string CliCodigo, string CliNombre);

    private class PdAgg { public HashSet<string> Peds = new(); public double MontoPed, MontoFact; }

    private static List<int> ParseEstatus(string? csv)
    {
        var l = ParseInts(csv);
        return l.Count > 0 ? l : new List<int> { 2, 7, 11 };
    }

    private static Dictionary<int, string> CargarCanales(SqlConnection cn)
    {
        var d = new Dictionary<int, string>();
        using var cmd = new SqlCommand("SELECT CanID, CanDescripcion FROM CanalDistribucion WITH (NOLOCK)", cn) { CommandTimeout = 30 };
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) if (!rd.IsDBNull(0)) d[Convert.ToInt32(rd.GetValue(0))] = rd.IsDBNull(1) ? "" : rd.GetString(1);
        return d;
    }

    private List<PdLinea> GatherPedidosLineas(SqlConnection cn, DateOnly ini, DateOnly fin, List<int> lEstatus,
        List<string> repsOk, List<int> lCanal, List<int> lSup, List<int> lCat, List<int> lPro, List<string> lGru, List<string> lGC, List<int> lTam)
    {
        var iniS = ini.ToString("yyyy-MM-dd"); var finS = fin.AddDays(1).ToString("yyyy-MM-dd");
        var parms = new List<KeyValuePair<string, object>>();
        string In(string col, IEnumerable<object> vals, string pre)
        {
            var l = vals.ToList(); if (l.Count == 0) return "";
            var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        var wEst = In("p.PedEstatus", lEstatus.Cast<object>(), "es");
        var wRep = repsOk.Count > 0 ? In("p.RepCodigo", repsOk.Cast<object>(), "rp") : "";
        var wCanal = In("ISNULL(cl.CanID,0)", lCanal.Cast<object>(), "cn");
        var wSup = In("pr.Cat3ID", lSup.Cast<object>(), "sp");
        var wCat = In("pr.LinID", lCat.Cast<object>(), "ct");
        var wPro = In("pd.ProID", lPro.Cast<object>(), "pd");
        var wGru = In("gd.GrpCodigo", lGru.Cast<object>(), "gr");
        var wGC = lGC.Count > 0
            ? $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=p.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})"
            : "";
        var wTam = lTam.Count > 0 ? In("ISNULL(cl.ClaID,0)", lTam.Cast<object>(), "tm") : "";
        var grupoJoin = lGru.Count > 0 ? "INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=pd.ProID" : "";

        var sql = $@"
SELECT p.RepCodigo, p.PedSecuencia, ISNULL(cl.CanID,0) CanID, p.PedEstatus,
       ISNULL(pr.Cat3ID,0) SupID, ISNULL(s.Cat3Descripcion,'(Sin suplidor)') Suplidor,
       ISNULL(pr.LinID,0) CatID, ISNULL(ln.LinDescripcion,'(Sin categoría)') Categoria,
       pd.ProID, pr.ProDescripcion Producto,
       SUM(pd.PedCantidad*(pd.PedPrecio-ISNULL(pd.PedDescuento,0))) MontoPed,
       SUM(pd.PedCantidadDetalle*(pd.PedPrecio-ISNULL(pd.PedDescuento,0))) MontoFact,
       CAST(p.PedFecha AS DATE) Fecha, ISNULL(cl.CliID,0) CliID, ISNULL(cl.CliCodigo,'') CliCodigo, ISNULL(cl.CliNombre,'') CliNombre
FROM VWPedidos p WITH (NOLOCK)
INNER JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia=p.PedSecuencia AND pd.RepCodigo=p.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=pd.ProID
LEFT JOIN Categoria3 s WITH (NOLOCK) ON s.Cat3ID=pr.Cat3ID
LEFT JOIN Lineas ln WITH (NOLOCK) ON ln.LinID=pr.LinID
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.CliID=p.CliID
{grupoJoin}
WHERE p.PedFecha>=@ini AND p.PedFecha<@fin{wEst}{wRep}{wCanal}{wSup}{wCat}{wPro}{wGru}{wGC}{wTam}
GROUP BY p.RepCodigo, p.PedSecuencia, cl.CanID, p.PedEstatus, pr.Cat3ID, s.Cat3Descripcion, pr.LinID, ln.LinDescripcion, pd.ProID, pr.ProDescripcion,
         CAST(p.PedFecha AS DATE), cl.CliID, cl.CliCodigo, cl.CliNombre";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 };
        cmd.Parameters.AddWithValue("@ini", iniS); cmd.Parameters.AddWithValue("@fin", finS);
        foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);

        var lineas = new List<PdLinea>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
            lineas.Add(new PdLinea(
                rd.GetValue(0).ToString()!.Trim(), rd.GetValue(1).ToString()!.Trim(),
                rd.IsDBNull(2) ? 0 : Convert.ToInt32(rd.GetValue(2)),
                rd.IsDBNull(3) ? 0 : Convert.ToInt32(rd.GetValue(3)),
                rd.GetInt32(4), rd.GetString(5), rd.GetInt32(6), rd.GetString(7),
                rd.IsDBNull(8) ? 0 : rd.GetInt32(8), rd.IsDBNull(9) ? "" : rd.GetString(9),
                rd.IsDBNull(10) ? 0 : Convert.ToDouble(rd.GetValue(10)),
                rd.IsDBNull(11) ? 0 : Convert.ToDouble(rd.GetValue(11)),
                rd.IsDBNull(12) ? "" : Convert.ToDateTime(rd.GetValue(12)).ToString("yyyy-MM-dd"),
                rd.IsDBNull(13) ? 0 : Convert.ToInt32(rd.GetValue(13)),
                rd.IsDBNull(14) ? "" : rd.GetString(14),
                rd.IsDBNull(15) ? "" : rd.GetString(15)));
        }
        return lineas;
    }

    [HttpGet("pedidos")]
    public IActionResult Pedidos(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin, [FromQuery] string? modo,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? canales,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? gruposCliente, [FromQuery] string? tamanos, [FromQuery] string? estatus)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            modo = modo is "supervisor" or "suplidor" or "suplidorprod" or "estatus" or "dia" or "cliente" ? modo : "centro";
            var lEstatus = ParseEstatus(estatus);
            var lCanal = ParseInts(canales);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos);
            var lGru = ParseList(grupos); var lGC = ParseList(gruposCliente); var lTam = ParseInts(tamanos);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);

            var lineas = GatherPedidosLineas(cn, ini, fin, lEstatus, repsOk, lCanal, lSup, lCat, lPro, lGru, lGC, lTam);

            if (modo == "suplidor")
            {
                // Mismo patrón manual de GROUPING SETS (por línea, no por SQL) que usa
                // FillRateSupCat/PedFactNcSupCat — acá solo 2 niveles (Suplidor/Categoría,
                // sin Producto, tal como pidió el usuario).
                var nombres = new Dictionary<string, (int SupID, string Suplidor, int CatID, string Categoria)>();
                var porSup = new Dictionary<string, PdAgg>(); var porCat = new Dictionary<string, PdAgg>(); var porTotal = new PdAgg();
                string KSup(int s) => s.ToString(); string KCat(int s, int c) => s + "|" + c;
                PdAgg AggIn(Dictionary<string, PdAgg> map, string key) { if (!map.TryGetValue(key, out var a)) { a = new(); map[key] = a; } return a; }
                foreach (var ln in lineas)
                {
                    var pedKey = ln.RepCodigo + "|" + ln.PedSecuencia;
                    var ks = KSup(ln.SupID); var kc = KCat(ln.SupID, ln.CatID);
                    if (!nombres.ContainsKey(ks)) nombres[ks] = (ln.SupID, ln.Suplidor, 0, "");
                    if (!nombres.ContainsKey(kc)) nombres[kc] = (ln.SupID, ln.Suplidor, ln.CatID, ln.Categoria);
                    foreach (var (map, k) in new[] { (porSup, ks), (porCat, kc) })
                    {
                        var a = AggIn(map, k); a.Peds.Add(pedKey); a.MontoPed += ln.MontoPed; a.MontoFact += ln.MontoFact;
                    }
                    porTotal.Peds.Add(pedKey); porTotal.MontoPed += ln.MontoPed; porTotal.MontoFact += ln.MontoFact;
                }
                var filasSC = new List<object>();
                void Volcar(Dictionary<string, PdAgg> map, string nivel)
                {
                    foreach (var (k, a) in map)
                    {
                        var (s, sn, c, cn2) = nombres[k];
                        filasSC.Add(new
                        {
                            Nivel = nivel, SupID = s, Suplidor = sn, CatID = c, Categoria = cn2,
                            Cantidad = a.Peds.Count, MontoPed = Math.Round(a.MontoPed), MontoFact = Math.Round(a.MontoFact)
                        });
                    }
                }
                Volcar(porSup, "suplidor"); Volcar(porCat, "categoria");
                filasSC.Add(new { Nivel = "total", SupID = 0, Suplidor = "", CatID = 0, Categoria = "",
                    Cantidad = porTotal.Peds.Count, MontoPed = Math.Round(porTotal.MontoPed), MontoFact = Math.Round(porTotal.MontoFact) });
                return Ok(new { ok = true, modo, filas = filasSC });
            }

            if (modo == "suplidorprod")
            {
                // Extensión de modo="suplidor" con un 3er nivel (Producto) — pedido
                // explícito 2026-09-29: "incluye uno que diga suplidor - categoria -
                // Producto... sub totalice por categoria, suplidor y total general por
                // compañia". Se deja como opción NUEVA y separada (modo="suplidor" YA
                // había quedado deliberadamente sin Producto por pedido explícito de una
                // sesión anterior — no se cambia ese comportamiento existente). Mismo
                // patrón manual de "GROUPING SETS por línea" que el resto de este
                // archivo, solo con un nivel más.
                var nombresP = new Dictionary<string, (int SupID, string Suplidor, int CatID, string Categoria, int ProID, string Producto)>();
                var porSupP = new Dictionary<string, PdAgg>(); var porCatP = new Dictionary<string, PdAgg>(); var porPro = new Dictionary<string, PdAgg>(); var porTotalP = new PdAgg();
                string KSupP(int s) => s.ToString(); string KCatP(int s, int c) => s + "|" + c; string KPro(int s, int c, int p) => s + "|" + c + "|" + p;
                PdAgg AggInP(Dictionary<string, PdAgg> map, string key) { if (!map.TryGetValue(key, out var a)) { a = new(); map[key] = a; } return a; }
                foreach (var ln in lineas)
                {
                    var pedKey = ln.RepCodigo + "|" + ln.PedSecuencia;
                    var ks = KSupP(ln.SupID); var kc = KCatP(ln.SupID, ln.CatID); var kp = KPro(ln.SupID, ln.CatID, ln.ProID);
                    if (!nombresP.ContainsKey(ks)) nombresP[ks] = (ln.SupID, ln.Suplidor, 0, "", 0, "");
                    if (!nombresP.ContainsKey(kc)) nombresP[kc] = (ln.SupID, ln.Suplidor, ln.CatID, ln.Categoria, 0, "");
                    if (!nombresP.ContainsKey(kp)) nombresP[kp] = (ln.SupID, ln.Suplidor, ln.CatID, ln.Categoria, ln.ProID, ln.Producto);
                    foreach (var (map, k) in new[] { (porSupP, ks), (porCatP, kc), (porPro, kp) })
                    {
                        var a = AggInP(map, k); a.Peds.Add(pedKey); a.MontoPed += ln.MontoPed; a.MontoFact += ln.MontoFact;
                    }
                    porTotalP.Peds.Add(pedKey); porTotalP.MontoPed += ln.MontoPed; porTotalP.MontoFact += ln.MontoFact;
                }
                var filasSCP = new List<object>();
                void VolcarP(Dictionary<string, PdAgg> map, string nivel)
                {
                    foreach (var (k, a) in map)
                    {
                        var (s, sn, c, cn2, p, pn) = nombresP[k];
                        filasSCP.Add(new
                        {
                            Nivel = nivel, SupID = s, Suplidor = sn, CatID = c, Categoria = cn2, ProID = p, Producto = pn,
                            Cantidad = a.Peds.Count, MontoPed = Math.Round(a.MontoPed), MontoFact = Math.Round(a.MontoFact)
                        });
                    }
                }
                VolcarP(porSupP, "suplidor"); VolcarP(porCatP, "categoria"); VolcarP(porPro, "producto");
                filasSCP.Add(new { Nivel = "total", SupID = 0, Suplidor = "", CatID = 0, Categoria = "", ProID = 0, Producto = "",
                    Cantidad = porTotalP.Peds.Count, MontoPed = Math.Round(porTotalP.MontoPed), MontoFact = Math.Round(porTotalP.MontoFact) });
                return Ok(new { ok = true, modo, filas = filasSCP });
            }

            if (modo == "estatus")
            {
                var porPedidoEst = lineas.GroupBy(x => x.RepCodigo + "|" + x.PedSecuencia)
                    .Select(g => new { Estatus = g.First().PedEstatus, MontoPed = g.Sum(x => x.MontoPed), MontoFact = g.Sum(x => x.MontoFact) })
                    .ToList();
                var descEst = new Dictionary<int, string>();
                using (var cmdE = new SqlCommand("SELECT EstEstado, EstDescripcion FROM Estados WITH (NOLOCK) WHERE EstTabla='Pedidos'", cn) { CommandTimeout = 30 })
                using (var rdE = cmdE.ExecuteReader())
                    while (rdE.Read()) if (!rdE.IsDBNull(0)) descEst[Convert.ToInt32(rdE.GetValue(0))] = rdE.IsDBNull(1) ? "" : rdE.GetString(1);
                var filasEst = porPedidoEst.GroupBy(p => p.Estatus).Select(g => new
                {
                    Estatus = g.Key, Descripcion = descEst.GetValueOrDefault(g.Key, "(Estatus " + g.Key + ")"),
                    Cantidad = g.Count(), MontoPed = Math.Round(g.Sum(x => x.MontoPed)), MontoFact = Math.Round(g.Sum(x => x.MontoFact))
                }).ToList();
                return Ok(new { ok = true, modo, filas = filasEst });
            }

            // modo=dia / modo=cliente (agregados 2026-09-27, pedido explícito: drill-down
            // Fecha→Cliente en la pestaña Pedidos) — mismo patrón que modo=estatus arriba:
            // dedup a 1 fila por pedido primero (Fecha/CliID son constantes dentro de un
            // mismo pedido, g.First() es seguro), y ahí sí agrupar por el eje pedido. El
            // caller (front) es quien acota 'lineas' a "la fila exacta que se clickeó" —
            // mandando los MISMOS filtros que ya soporta este endpoint (estatus/suplidores/
            // categorias/centros/canales/supervisores/vendedores), sin parámetro nuevo de
            // "fila clickeada". Para el 2º nivel (por cliente) el caller además acota
            // fechaIni=fechaFin al día exacto — así 'lineas' YA viene restringida a 1 día.
            if (modo == "dia")
            {
                var porPedidoDia = lineas.GroupBy(x => x.RepCodigo + "|" + x.PedSecuencia)
                    .Select(g => new { g.First().Fecha, MontoPed = g.Sum(x => x.MontoPed), MontoFact = g.Sum(x => x.MontoFact) })
                    .ToList();
                var filasDia = porPedidoDia.GroupBy(p => p.Fecha).Select(g => new
                {
                    Fecha = g.Key, Cantidad = g.Count(), MontoPed = Math.Round(g.Sum(x => x.MontoPed)), MontoFact = Math.Round(g.Sum(x => x.MontoFact))
                }).OrderBy(f => f.Fecha).ToList();
                return Ok(new { ok = true, modo, filas = filasDia });
            }

            if (modo == "cliente")
            {
                var porPedidoCli = lineas.GroupBy(x => x.RepCodigo + "|" + x.PedSecuencia)
                    .Select(g => new { g.First().CliID, g.First().CliCodigo, g.First().CliNombre, MontoPed = g.Sum(x => x.MontoPed), MontoFact = g.Sum(x => x.MontoFact) })
                    .ToList();
                var filasCli = porPedidoCli.GroupBy(p => p.CliID).Select(g => new
                {
                    CliID = g.Key, CliCodigo = g.First().CliCodigo, CliNombre = g.First().CliNombre,
                    Cantidad = g.Count(), MontoPed = Math.Round(g.Sum(x => x.MontoPed)), MontoFact = Math.Round(g.Sum(x => x.MontoFact))
                }).OrderByDescending(f => f.MontoPed).ToList();
                return Ok(new { ok = true, modo, filas = filasCli });
            }

            // modo centro / supervisor: 1 fila por pedido (dedup RepCodigo|PedSecuencia),
            // Centro/Supervisor resueltos vía CargarReps, Canal vía CargarCanales — el
            // árbol (Centro→Canal→Supervisor, o Supervisor→Vendedor) y sus subtotales se
            // arman 100% en JS a partir de este único arreglo plano.
            var porPed = lineas.GroupBy(x => x.RepCodigo + "|" + x.PedSecuencia)
                .Select(g => new { RepCodigo = g.First().RepCodigo, CanID = g.First().CanID, MontoPed = g.Sum(x => x.MontoPed), MontoFact = g.Sum(x => x.MontoFact) })
                .ToList();
            var canalesDict = CargarCanales(cn);
            var filasOrg = porPed.Select(p =>
            {
                reps.TryGetValue(p.RepCodigo, out var ri); ri ??= new RepInfo { Nombre = p.RepCodigo };
                return new
                {
                    RepCodigo = p.RepCodigo, ri.Nombre, ri.Centro, ri.CedCodigo, ri.SupCod, ri.Supervisor,
                    CanID = p.CanID, Canal = canalesDict.GetValueOrDefault(p.CanID, "(Sin canal)"),
                    Cantidad = 1, MontoPed = Math.Round(p.MontoPed), MontoFact = Math.Round(p.MontoFact)
                };
            }).ToList();
            return Ok(new { ok = true, modo, filas = filasOrg });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Tarjetas KPI: 3 ventanas independientes del rango del reporte ad-hoc — "hoy" (1/3/4,
    // con el Estatus elegido en pantalla), "backlog actual" (2, SIEMPRE estatus 2 y 7,
    // acotado a los últimos 90 días por performance — un pedido "Transmitido"/"Pendiente
    // Autorización" con más de 90 días es un caso atípico, no el backlog real que se
    // quiere vigilar) y "rango del reporte" (5, SIEMPRE estatus 11, default mes en curso
    // vía ParseRange). Mismo patrón que NotasCreditoTarjetas (ventanas fijas + filtros
    // org/producto/canal respetados).
    [HttpGet("pedidos-tarjetas")]
    public IActionResult PedidosTarjetas(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? canales,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? gruposCliente, [FromQuery] string? tamanos, [FromQuery] string? estatus)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (iniRango, finRango) = ParseRange(fechaIni, fechaFin);
            var lEstatusPantalla = ParseEstatus(estatus);
            var lCanal = ParseInts(canales);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos);
            var lGru = ParseList(grupos); var lGC = ParseList(gruposCliente); var lTam = ParseInts(tamanos);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);
            var canalesDict = CargarCanales(cn);
            var hoy = DateOnly.FromDateTime(DateTime.Today);

            var lineasHoy = GatherPedidosLineas(cn, hoy, hoy, lEstatusPantalla, repsOk, lCanal, lSup, lCat, lPro, lGru, lGC, lTam);
            var pedidosHoy = lineasHoy.GroupBy(x => x.RepCodigo + "|" + x.PedSecuencia)
                .Select(g => new { g.First().RepCodigo, g.First().CanID, MontoPed = g.Sum(x => x.MontoPed) }).ToList();
            var porCanalHoy = pedidosHoy.GroupBy(p => p.CanID)
                .Select(g => new { canal = canalesDict.GetValueOrDefault(g.Key, "(Sin canal)"), monto = Math.Round(g.Sum(x => x.MontoPed)), cantidad = g.Count() })
                .OrderByDescending(x => x.monto).ToList();
            var porCentroHoy = pedidosHoy.GroupBy(p => reps.TryGetValue(p.RepCodigo, out var ri) ? ri.Centro : "(Sin centro)")
                .Select(g => new { centro = g.Key, monto = Math.Round(g.Sum(x => x.MontoPed)), cantidad = g.Count() })
                .OrderByDescending(x => x.monto).ToList();

            var lineasPend = GatherPedidosLineas(cn, hoy.AddDays(-90), hoy, new List<int> { 2, 7 }, repsOk, lCanal, lSup, lCat, lPro, lGru, lGC, lTam);
            var pedidosPend = lineasPend.GroupBy(x => x.RepCodigo + "|" + x.PedSecuencia).Select(g => g.Sum(x => x.MontoPed)).ToList();

            var lineasPerd = GatherPedidosLineas(cn, iniRango, finRango, new List<int> { 11 }, repsOk, lCanal, lSup, lCat, lPro, lGru, lGC, lTam);
            var pedidosPerd = lineasPerd.GroupBy(x => x.RepCodigo + "|" + x.PedSecuencia)
                .Select(g => new { MontoPed = g.Sum(x => x.MontoPed), MontoFact = g.Sum(x => x.MontoFact) }).ToList();
            double montoPedPerd = pedidosPerd.Sum(p => p.MontoPed), montoFactPerd = pedidosPerd.Sum(p => p.MontoFact);

            return Ok(new
            {
                ok = true,
                hoy = new { cantidad = pedidosHoy.Count, monto = Math.Round(pedidosHoy.Sum(p => p.MontoPed)), porCanal = porCanalHoy, porCentro = porCentroHoy },
                pendientes = new { cantidad = pedidosPend.Count, monto = Math.Round(pedidosPend.Sum()) },
                perdidos = new
                {
                    montoPedido = Math.Round(montoPedPerd), montoFacturado = Math.Round(montoFactPerd),
                    montoSinFacturar = Math.Round(montoPedPerd - montoFactPerd),
                    incidencia = montoPedPerd > 0 ? Math.Round(100.0 * (montoPedPerd - montoFactPerd) / montoPedPerd, 1) : (double?)null
                }
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apihist/entregas-filtro ────────────────────────────────────────
    // Opciones para el filtro "Entrega Repartidor" (Ped-Fact-NC) — en cascada: solo
    // devuelve EnrSecuencia que tienen al menos 1 pedido dentro del resto de
    // filtros ya aplicados (fecha/Centro/Supervisor/Vendedor/Suplidor/Categoría/
    // Producto), para no ofrecer entregas que de todas formas saldrían vacías.
    [HttpGet("entregas-filtro")]
    public IActionResult EntregasFiltro(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos);

            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);

            var parms = new List<KeyValuePair<string, object>>();
            string In(string col, IEnumerable<object> vals, string pre)
            {
                var l = vals.ToList(); if (l.Count == 0) return "";
                var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
                return $" AND {col} IN ({string.Join(",", names)})";
            }
            var wRep = repsOk.Count > 0 ? In("v.CodVendedor", repsOk.Cast<object>(), "rp") : "";
            var wSup = In("pr.Cat3ID", lSup.Cast<object>(), "sp");
            var wCat = In("pr.LinID", lCat.Cast<object>(), "ct");
            var wPro = In("v.ProId", lPro.Cast<object>(), "pd");

            var iniS = ini.ToString("yyyy-MM-dd"); var finS = fin.AddDays(1).ToString("yyyy-MM-dd");
            var sql = $@"
SELECT TOP 500 v.EnrSecuencia, MAX(v.EnrFecha) EnrFecha, COUNT(DISTINCT v.PedSecuencia) nPed
FROM VwPedidosVsFacutadosMenosDevolucionRM v WITH (NOLOCK)
LEFT JOIN Productos pr WITH (NOLOCK) ON pr.ProID=v.ProId
WHERE v.EnrFecha>=@ini AND v.EnrFecha<@fin AND v.PedSecuencia IS NOT NULL{wRep}{wSup}{wCat}{wPro}
GROUP BY v.EnrSecuencia
ORDER BY MAX(v.EnrFecha) DESC, v.EnrSecuencia DESC";
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@ini", iniS); cmd.Parameters.AddWithValue("@fin", finS);
            foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);
            var opciones = new List<object>();
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    int enr = rd.GetInt32(0);
                    string fecha = rd.IsDBNull(1) ? "" : Convert.ToDateTime(rd.GetValue(1)).ToString("yyyy-MM-dd");
                    int nPed = rd.GetInt32(2);
                    opciones.Add(new { v = enr.ToString(), t = enr + (fecha != "" ? " — " + fecha : "") + " (" + nPed + " ped.)" });
                }
            }
            return Ok(new { ok = true, opciones });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apihist/clientes-buscar ────────────────────────────────────────
    // Búsqueda liviana por Código o Nombre — el catálogo completo de Clientes es
    // demasiado grande (20,000+) para precargar en un multiselect como los demás
    // filtros; se busca "as you type" igual que un autocompletar.
    [HttpGet("clientes-buscar")]
    public IActionResult ClientesBuscar([FromQuery] string? q)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Ok(new { ok = true, opciones = Array.Empty<object>() });
        try
        {
            using var cn = NewConn();
            cn.Open();
            using var cmd = new SqlCommand(@"
SELECT TOP 50 CliID, ISNULL(CliCodigo,CAST(CliID AS varchar(20))) Cod, ISNULL(CliNombre,'') Nom
FROM Clientes WITH (NOLOCK)
WHERE CliNombre LIKE @q OR CliCodigo LIKE @q OR CAST(CliID AS varchar(20))=@qExacto
ORDER BY CliNombre", cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@q", "%" + q.Trim() + "%");
            cmd.Parameters.AddWithValue("@qExacto", q.Trim());
            var opciones = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                opciones.Add(new { v = rd.GetInt32(0).ToString(), t = rd.GetString(1) + " - " + rd.GetString(2) });
            return Ok(new { ok = true, opciones });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    [HttpGet("perdidos")]
    public IActionResult Perdidos(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin, [FromQuery] string? fuente, [FromQuery] string? tipo,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? tamanos, [FromQuery] string? gruposCliente)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            fuente = fuente == "ventas" ? "ventas" : "pedidos";
            tipo = tipo is "centro" or "division" or "supervisor" or "vendedor" ? tipo : "compania";
            using var cn = NewConn(); cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, gi) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);
            if (repsOk.Count == 0) return Ok(new { ok = true, tipo, fuente, desde = ini.ToString("yyyy-MM-dd"), hasta = fin.ToString("yyyy-MM-dd"),
                padHead = Array.Empty<string>(), filas = new List<object>(), total = new { asignados = 0, perdidos = 0, pct = 0 }, detalle = new List<object>() });

            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos); var lGru = ParseList(grupos);
            var lTam = ParseInts(tamanos); var lGC = ParseList(gruposCliente);
            var asig = Asignaciones(cn, repsOk, lTam, lGC);
            var comp = Compradores(cn, fuente, ini, fin, lSup, lCat, lPro, lGru, repsOk);

            var (keyFn, nomFn, padFn, padHead) = GrupoDe(tipo);
            var asigCli = new Dictionary<string, HashSet<int>>();     // grupo -> clientes asignados
            var compCli = new Dictionary<string, HashSet<int>>();     // grupo -> clientes que compraron (algún rep del grupo)
            var nomGrupo = new Dictionary<string, string>(); var padGrupo = new Dictionary<string, string[]>();
            var allAsig = new HashSet<int>(); var allComp = new HashSet<int>();
            foreach (var (rep, cli, tam) in asig)
            {
                if (!gi.TryGetValue(rep, out var g)) continue;
                var k = keyFn(g);
                if (!asigCli.TryGetValue(k, out var sa)) { sa = new(); asigCli[k] = sa; nomGrupo[k] = nomFn(g); padGrupo[k] = padFn(g); }
                sa.Add(cli); allAsig.Add(cli);
                if (comp.Contains(rep + "|" + cli)) { if (!compCli.TryGetValue(k, out var sc)) { sc = new(); compCli[k] = sc; } sc.Add(cli); allComp.Add(cli); }
            }
            var filas = asigCli.Select(kv =>
            {
                int a = kv.Value.Count; int c = compCli.TryGetValue(kv.Key, out var sc) ? sc.Count : 0; int perd = a - c;
                return new { grupo = nomGrupo[kv.Key], padres = padGrupo[kv.Key], asignados = a, perdidos = perd,
                             pct = a > 0 ? Math.Round(perd * 100.0 / a, 1) : 0 };
            }).OrderByDescending(x => x.perdidos).ToList();
            int totA = allAsig.Count, totP = allAsig.Count - allComp.Count;

            // Detalle = asignaciones (rep,cli) donde el cliente es perdido en su grupo. Cap 60k.
            var det = new List<object>();
            var nombres = new Dictionary<int, (string cod, string nom)>();
            var idsDet = new List<int>();
            foreach (var (rep, cli, tam) in asig)
            {
                if (!gi.TryGetValue(rep, out var g)) continue;
                var k = keyFn(g);
                bool boughtGrupo = compCli.TryGetValue(k, out var sc) && sc.Contains(cli);
                if (boughtGrupo) continue;
                if (det.Count < 60000) { idsDet.Add(cli); det.Add(new { g.Centro, Supervisor = g.SupNom, Division = g.Div, Vendedor = g.Nom, VenCod = rep, Tam = TamNom.GetValueOrDefault(tam, tam.ToString()), Cli = cli }); }
            }
            // nombres de clientes del detalle
            foreach (var chunk in idsDet.Distinct().Chunk(2000))
            {
                using var cmd = new SqlCommand($"SELECT CliID, ISNULL(CliCodigo,CAST(CliID AS varchar(20))), ISNULL(CliNombre,'') FROM Clientes WITH (NOLOCK) WHERE CliID IN ({string.Join(",", chunk)})", cn) { CommandTimeout = 60 };
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) nombres[Convert.ToInt32(rd.GetValue(0))] = (rd.IsDBNull(1) ? "" : rd.GetValue(1).ToString()!.Trim(), rd.IsDBNull(2) ? "" : rd.GetString(2).Trim());
            }
            var detalle = det.Select(o => { dynamic d = o; var nm = nombres.TryGetValue((int)d.Cli, out var n) ? n : (d.Cli.ToString(), "");
                return new { d.Centro, d.Supervisor, d.Division, d.Vendedor, d.VenCod, d.Tam, CliCod = nm.Item1, CliNom = nm.Item2 }; }).ToList();

            return Ok(new { ok = true, tipo, fuente, desde = ini.ToString("yyyy-MM-dd"), hasta = fin.ToString("yyyy-MM-dd"),
                padHead, filas, total = new { asignados = totA, perdidos = totP, pct = totA > 0 ? Math.Round(totP * 100.0 / totA, 1) : 0 }, detalle });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    [HttpGet("recuperados")]
    public IActionResult Recuperados(
        [FromQuery] string? baseIni, [FromQuery] string? baseFin, [FromQuery] string? recIni, [FromQuery] string? recFin,
        [FromQuery] string? fuente, [FromQuery] string? tipo,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? grupos,
        [FromQuery] string? tamanos, [FromQuery] string? gruposCliente)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var (bi, bf) = ParseRange(baseIni, baseFin);
            var (ri, rf) = ParseRange(recIni, recFin);
            fuente = fuente == "ventas" ? "ventas" : "pedidos";
            tipo = tipo is "centro" or "division" or "supervisor" or "vendedor" ? tipo : "compania";
            using var cn = NewConn(); cn.Open();
            var reps = CargarReps(cn);
            var (repsOk, gi) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);
            if (repsOk.Count == 0) return Ok(new { ok = true, tipo, fuente, padHead = Array.Empty<string>(), filas = new List<object>(),
                total = new { asignados = 0, sinCompra = 0, recuperados = 0, pctSin = 0, pctRec = 0 }, detalle = new List<object>() });

            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos); var lGru = ParseList(grupos);
            var lTam = ParseInts(tamanos); var lGC = ParseList(gruposCliente);
            var asig = Asignaciones(cn, repsOk, lTam, lGC);
            var compBase = Compradores(cn, fuente, bi, bf, lSup, lCat, lPro, lGru, repsOk);  // compró en rango base
            var compRec  = Compradores(cn, fuente, ri, rf, lSup, lCat, lPro, lGru, repsOk);  // compró en rango recuperación

            var (keyFn, nomFn, padFn, padHead) = GrupoDe(tipo);
            var asigCli = new Dictionary<string, HashSet<int>>();
            var baseBuy = new Dictionary<string, HashSet<int>>();   // compró base (grupo)
            var recBuy  = new Dictionary<string, HashSet<int>>();   // de los sin-base, compró rec (grupo)
            var nomGrupo = new Dictionary<string, string>(); var padGrupo = new Dictionary<string, string[]>();
            var allA = new HashSet<int>(); var allBase = new HashSet<int>(); var allRec = new HashSet<int>();
            // 1er pase: asignados + compró base por grupo
            foreach (var (rep, cli, tam) in asig)
            {
                if (!gi.TryGetValue(rep, out var g)) continue; var k = keyFn(g);
                if (!asigCli.TryGetValue(k, out var sa)) { sa = new(); asigCli[k] = sa; nomGrupo[k] = nomFn(g); padGrupo[k] = padFn(g); }
                sa.Add(cli); allA.Add(cli);
                if (compBase.Contains(rep + "|" + cli)) { if (!baseBuy.TryGetValue(k, out var sb)) { sb = new(); baseBuy[k] = sb; } sb.Add(cli); allBase.Add(cli); }
            }
            // recuperados: cliente SIN compra base en el grupo pero CON compra rec en el grupo
            foreach (var (rep, cli, tam) in asig)
            {
                if (!gi.TryGetValue(rep, out var g)) continue; var k = keyFn(g);
                bool sinBase = !(baseBuy.TryGetValue(k, out var sb) && sb.Contains(cli));
                if (sinBase && compRec.Contains(rep + "|" + cli)) { if (!recBuy.TryGetValue(k, out var sr)) { sr = new(); recBuy[k] = sr; } sr.Add(cli); if (!allBase.Contains(cli)) allRec.Add(cli); }
            }
            var filas = asigCli.Select(kv =>
            {
                int a = kv.Value.Count; int b = baseBuy.TryGetValue(kv.Key, out var sb) ? sb.Count : 0; int sin = a - b;
                int rec = recBuy.TryGetValue(kv.Key, out var sr) ? sr.Count : 0;
                return new { grupo = nomGrupo[kv.Key], padres = padGrupo[kv.Key], asignados = a, sinCompra = sin, recuperados = rec,
                             pctSin = a > 0 ? Math.Round(sin * 100.0 / a, 1) : 0, pctRec = sin > 0 ? Math.Round(rec * 100.0 / sin, 1) : 0 };
            }).OrderByDescending(x => x.recuperados).ToList();
            int tA = allA.Count, tSin = allA.Count - allBase.Count, tRec = allRec.Count;

            // detalle = clientes recuperados (asignación)
            var det = new List<object>(); var idsDet = new List<int>();
            foreach (var (rep, cli, tam) in asig)
            {
                if (!gi.TryGetValue(rep, out var g)) continue; var k = keyFn(g);
                bool sinBase = !(baseBuy.TryGetValue(k, out var sb) && sb.Contains(cli));
                bool rec = recBuy.TryGetValue(k, out var sr) && sr.Contains(cli);
                if (sinBase && rec && det.Count < 60000) { idsDet.Add(cli); det.Add(new { g.Centro, Supervisor = g.SupNom, Division = g.Div, Vendedor = g.Nom, VenCod = rep, Tam = TamNom.GetValueOrDefault(tam, tam.ToString()), Cli = cli }); }
            }
            var nombres = new Dictionary<int, (string cod, string nom)>();
            foreach (var chunk in idsDet.Distinct().Chunk(2000))
            {
                using var cmd = new SqlCommand($"SELECT CliID, ISNULL(CliCodigo,CAST(CliID AS varchar(20))), ISNULL(CliNombre,'') FROM Clientes WITH (NOLOCK) WHERE CliID IN ({string.Join(",", chunk)})", cn) { CommandTimeout = 60 };
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) nombres[Convert.ToInt32(rd.GetValue(0))] = (rd.IsDBNull(1) ? "" : rd.GetValue(1).ToString()!.Trim(), rd.IsDBNull(2) ? "" : rd.GetString(2).Trim());
            }
            var detalle = det.Select(o => { dynamic d = o; var nm = nombres.TryGetValue((int)d.Cli, out var n) ? n : (d.Cli.ToString(), "");
                return new { d.Centro, d.Supervisor, d.Division, d.Vendedor, d.VenCod, d.Tam, CliCod = nm.Item1, CliNom = nm.Item2 }; }).ToList();

            return Ok(new { ok = true, tipo, fuente,
                baseD = bi.ToString("yyyy-MM-dd"), baseH = bf.ToString("yyyy-MM-dd"), recD = ri.ToString("yyyy-MM-dd"), recH = rf.ToString("yyyy-MM-dd"),
                padHead, filas, total = new { asignados = tA, sinCompra = tSin, recuperados = tRec,
                    pctSin = tA > 0 ? Math.Round(tSin * 100.0 / tA, 1) : 0, pctRec = tSin > 0 ? Math.Round(tRec * 100.0 / tSin, 1) : 0 }, detalle });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }


    [HttpGet("filtros")]
    public IActionResult Filtros()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            var reps = CargarReps(cn).Select(kv => new
            {
                RepCodigo = kv.Key, kv.Value.Nombre, kv.Value.CedCodigo, kv.Value.Centro,
                SupervisorCod = kv.Value.SupCod, SupervisorNombre = kv.Value.Supervisor, kv.Value.Division
            }).OrderBy(x => x.Centro).ThenBy(x => x.SupervisorNombre).ThenBy(x => x.Nombre).ToList();

            List<object> Lista(string sql)
            {
                var l = new List<object>();
                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    l.Add(new { id = Convert.ToInt32(rd.GetValue(0)), nombre = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim() });
                return l;
            }
            // Grupo de Productos: el codigo puede no ser numerico -> lista con id string
            List<object> ListaStr(string sql)
            {
                var l = new List<object>();
                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    l.Add(new { id = rd.IsDBNull(0) ? "" : Convert.ToString(rd.GetValue(0))!.Trim(),
                                nombre = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim() });
                return l;
            }
            var canales = Lista("SELECT CanID, CanDescripcion FROM CanalDistribucion WITH (NOLOCK) ORDER BY CanDescripcion");
            var suplidores = Lista("SELECT Cat3ID, Cat3Descripcion FROM Categoria3 WITH (NOLOCK) ORDER BY Cat3Descripcion");
            var categorias = Lista("SELECT LinID, LinDescripcion FROM Lineas WITH (NOLOCK) ORDER BY LinDescripcion");
            var productos = Lista("SELECT ProID, ProDescripcion FROM Productos WITH (NOLOCK) ORDER BY ProDescripcion");
            var grupos = ListaStr("SELECT GrpCodigo, GrpDescripcion FROM GrupoProductos WITH (NOLOCK) ORDER BY GrpDescripcion");
            var gruposCliente = ListaStr("SELECT GrcCodigo, GrcDescripcion FROM GrupoClientes WITH (NOLOCK) ORDER BY GrcDescripcion");
            // Catálogo real de estatus de PEDIDO (pestaña "Pedidos", 2026-09-15) — verificado
            // en vivo: EstEstado 2=Transmitido, 7=Pendiente Autorización, 11=Facturado y
            // Enviado al ERP (los 3 del default). 29 filas en total.
            var estatusPedido = Lista("SELECT EstEstado, EstDescripcion FROM Estados WITH (NOLOCK) WHERE EstTabla='Pedidos' ORDER BY EstEstado");

            return Ok(new { ok = true, representantes = reps, canales, suplidores, categorias, productos, grupos, gruposCliente, estatusPedido });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }
}
