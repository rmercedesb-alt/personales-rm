using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace DashVisitas.Controllers;

// ═════════════════════════════════════════════════════════════════════
//  INVENTARIOS FÍSICOS (pestaña de Históricos — pedido explícito 2026-10-09; primera pestaña de la ampliación P2)
//  Un solo endpoint  GET /apihist/inventarios  con vista=
//    representantes → Supervisor › Vendedor: inventarios generados, clientes DISTINTOS con inventario, productos, unidades
//                     y % de su cartera (RutaVisitas) que tiene inventario en el rango
//    detalle        → Suplidor › Categoría › Producto de los inventarios del rango (clientes, inventarios, unidades)
//    clientes       → Supervisor › Vendedor › Cliente (inventarios, último, productos, unidades)
//    cliente        → drill de un cliente: sus inventarios del rango + todas sus líneas (Suplidor › Categoría › Producto)
//
//  Fuentes (esquema verificado en vivo contra MovilBusiness5STD, 2026-10-09):
//   Cabecera  InventarioFisico (+ InventarioFisicoHistoricos, hoy vacía): RepCodigo, invSecuencia, CliID, infFecha.
//   Detalle   InventarioFisicoDetalle (+ ...Historicos): RepCodigo+invSecuencia (+invPosicion), ProID, infCantidad (unidades contadas).
//             Hay cabeceras sin detalle (se cuentan como inventario/cliente pero no aportan productos).
//   Producto  Productos → Suplidor = Cat3ID/Categoria3, Categoría = LinID/Lineas (igual que el resto de Históricos).
//  "Inventario" = una cabecera (RepCodigo + invSecuencia). "Clientes" siempre DISTINTOS. Los datos del rango se guardan
//  3 min en memoria (clave = filtros que afectan el SQL) para que cambiar de vista/drill no vuelva a consultar.
// ═════════════════════════════════════════════════════════════════════
public partial class ApiHistoricosController
{
    private sealed record InvLinea(string Rep, int Sec, int Cli, string CliCod, string CliNom, DateTime Fecha,
        int ProId, string ProCod, string ProNom, int SupId, string SupNom, int CatId, string CatNom, double Cant);

    private List<InvLinea> CargarLineasInventario(SqlConnection cn, DateOnly ini, DateOnly fin, List<string>? repsFiltro,
        List<int> lSup, List<int> lCat, List<int> lPro, List<int> lCli)
    {
        var parms = new List<KeyValuePair<string, object>>();
        string In(string col, IEnumerable<object> vals, string pre)
        {
            var l = vals.ToList(); if (l.Count == 0) return "";
            var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        var w = (repsFiltro is { Count: > 0 } ? In("h.RepCodigo", repsFiltro.Cast<object>(), "rp") : "")
              + In("h.CliID", lCli.Cast<object>(), "cl") + In("pr.Cat3ID", lSup.Cast<object>(), "sp")
              + In("pr.LinID", lCat.Cast<object>(), "ct") + In("d.ProID", lPro.Cast<object>(), "pd");
        var sql = $@"
SELECT h.RepCodigo, h.invSecuencia, h.CliID, h.infFecha,
       ISNULL(c.CliCodigo,''), ISNULL(c.CliNombre,''),
       ISNULL(d.ProID,0), ISNULL(pr.ProCodigo,''), ISNULL(pr.ProDescripcion,''),
       ISNULL(pr.Cat3ID,0), ISNULL(s.Cat3Descripcion,'(Sin suplidor)'),
       ISNULL(pr.LinID,0), ISNULL(ln.LinDescripcion,'(Sin categoría)'), ISNULL(d.infCantidad,0)
FROM (SELECT RepCodigo, invSecuencia, CliID, infFecha FROM InventarioFisico WITH (NOLOCK)
      UNION SELECT RepCodigo, invSecuencia, CliID, infFecha FROM InventarioFisicoHistoricos WITH (NOLOCK)) h
LEFT JOIN (SELECT RepCodigo, invSecuencia, invPosicion, ProID, infCantidad FROM InventarioFisicoDetalle WITH (NOLOCK)
           UNION SELECT RepCodigo, invSecuencia, invPosicion, ProID, infCantidad FROM InventarioFisicoDetalleHistoricos WITH (NOLOCK)) d
       ON d.RepCodigo=h.RepCodigo AND d.invSecuencia=h.invSecuencia
LEFT JOIN Clientes c WITH (NOLOCK) ON c.CliID=h.CliID
LEFT JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
LEFT JOIN Categoria3 s WITH (NOLOCK) ON s.Cat3ID=pr.Cat3ID
LEFT JOIN Lineas ln WITH (NOLOCK) ON ln.LinID=pr.LinID
WHERE h.infFecha>=@ini AND h.infFecha<@fin{w}";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 };
        cmd.Parameters.AddWithValue("@ini", ini.ToString("yyyy-MM-dd")); cmd.Parameters.AddWithValue("@fin", fin.AddDays(1).ToString("yyyy-MM-dd"));
        foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);
        var res = new List<InvLinea>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            res.Add(new InvLinea(rd.GetString(0).Trim(), Convert.ToInt32(rd.GetValue(1)), Convert.ToInt32(rd.GetValue(2)), rd.GetString(4), rd.GetString(5),
                rd.GetDateTime(3), Convert.ToInt32(rd.GetValue(6)), rd.GetString(7), rd.GetString(8), Convert.ToInt32(rd.GetValue(9)), rd.GetString(10),
                Convert.ToInt32(rd.GetValue(11)), rd.GetString(12), Convert.ToDouble(rd.GetValue(13))));
        return res;
    }

    private static string FmtF(DateTime d) => d.ToString("yyyy-MM-dd HH:mm");

    [HttpGet("inventarios")]
    public IActionResult Inventarios(
        [FromQuery] string? vista, [FromQuery] string? fechaIni, [FromQuery] string? fechaFin,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos, [FromQuery] string? clientes,
        [FromQuery] int? cliente)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            vista = vista is "detalle" or "clientes" or "cliente" ? vista : "representantes";
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos); var lCli = ParseInts(clientes);

            using var cn = NewConn(); cn.Open();
            var reps = CargarReps(cn);
            var hayFiltroRep = ParseList(centros).Count + ParseList(supervisores).Count + ParseList(vendedores).Count + ParseList(divisiones).Count > 0;
            var (repsOkL, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);
            var repsOk = repsOkL.ToHashSet(StringComparer.OrdinalIgnoreCase);
            string SupDe(string r) => reps.TryGetValue(r, out var ri) && ri.SupCod != "" ? ri.SupCod : "(sin)";
            string SupNom(string r) => reps.TryGetValue(r, out var ri) && ri.Supervisor != "" ? ri.Supervisor : "(Sin supervisor)";
            string RepNom(string r) => reps.TryGetValue(r, out var ri) ? ri.Nombre : r;
            string CentroNom(string r) => reps.TryGetValue(r, out var ri) && ri.Centro != "" ? ri.Centro : "(Sin centro)";

            var clave = string.Join("|", "inv", ini, fin, centros, supervisores, vendedores, divisiones, suplidores, categorias, productos, clientes);
            var lineas = cache.GetOrCreate("inventarios|" + clave, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3);
                return CargarLineasInventario(cn, ini, fin, hayFiltroRep ? repsOkL : null, lSup, lCat, lPro, lCli);
            })!;

            // ── 4. CLIENTE (drill): sus inventarios del rango + todas sus líneas ──────────────
            if (vista == "cliente")
            {
                var mias = lineas.Where(l => l.Cli == (cliente ?? -1)).ToList();
                var invs = mias.GroupBy(l => (l.Rep, l.Sec)).Select(g => new
                {
                    Rep = g.Key.Rep, Vendedor = RepNom(g.Key.Rep), Sec = g.Key.Sec, Fecha = FmtF(g.First().Fecha),
                    Productos = g.Where(x => x.ProId > 0).Select(x => x.ProId).Distinct().Count(), Unidades = g.Sum(x => x.Cant)
                }).OrderByDescending(x => x.Fecha).ToList();
                var filas = mias.Where(l => l.ProId > 0).Select(l => new
                {
                    Rep = l.Rep, Sec = l.Sec, SupId = l.SupId, Suplidor = l.SupNom, CatId = l.CatId, Categoria = l.CatNom,
                    ProId = l.ProId, Codigo = l.ProCod, Producto = l.ProNom, Cantidad = l.Cant
                }).ToList();
                var c0 = mias.FirstOrDefault();
                return Ok(new { ok = true, vista, cliente = new { CliID = cliente, Codigo = c0?.CliCod ?? "", Nombre = c0?.CliNom ?? "" }, inventarios = invs, filas });
            }

            // ── 1. REPRESENTANTES: Supervisor › Vendedor ──────────────────────────────────────
            if (vista == "representantes")
            {
                var asig = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (r, cli, _) in Asignaciones(cn, hayFiltroRep ? repsOkL : new List<string>(), new List<int>()))
                { if (!asig.TryGetValue(r, out var hs)) asig[r] = hs = new(); hs.Add(cli); }
                // Vendedores ACTIVOS con cartera que no hicieron inventarios: se listan en 0 (para ver quién falta)
                var activos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = new SqlCommand("SELECT RepCodigo FROM Representantes WITH (NOLOCK) WHERE RepEstatus=1", cn))
                using (var rd = cmd.ExecuteReader()) while (rd.Read()) if (!rd.IsDBNull(0)) activos.Add(rd.GetString(0).Trim());
                var porRep = lineas.GroupBy(l => l.Rep).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                var universo = porRep.Keys.Concat(asig.Keys.Where(r => activos.Contains(r) && (!hayFiltroRep || repsOk.Contains(r)))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                object Fila(string nivel, string supCod, string sup, string repCod, string rep, string centro, List<InvLinea> ls, HashSet<int> asg)
                {
                    var cli = ls.Select(l => l.Cli).ToHashSet();
                    return new
                    {
                        Nivel = nivel, SupCod = supCod, Supervisor = sup, RepCodigo = repCod, Vendedor = rep, Centro = centro,
                        Inventarios = ls.Select(l => (l.Rep, l.Sec)).Distinct().Count(), Clientes = cli.Count,
                        Productos = ls.Where(l => l.ProId > 0).Select(l => l.ProId).Distinct().Count(), Unidades = ls.Sum(l => l.Cant),
                        Asignados = asg.Count, ConInventario = cli.Count(asg.Contains),
                        CoberturaPct = asg.Count > 0 ? Math.Round(100.0 * cli.Count(asg.Contains) / asg.Count, 1) : (double?)null
                    };
                }
                var filasR = new List<object>();
                foreach (var g in universo.GroupBy(SupDe).OrderBy(g => SupNom(g.First()), StringComparer.CurrentCultureIgnoreCase))
                {
                    var todasLs = g.SelectMany(r => porRep.TryGetValue(r, out var l) ? l : new List<InvLinea>()).ToList();
                    var asgSup = g.SelectMany(r => asig.TryGetValue(r, out var a) ? a : new HashSet<int>()).ToHashSet();
                    filasR.Add(Fila("supervisor", g.Key, SupNom(g.First()), "", "", CentroNom(g.First()), todasLs, asgSup));
                    foreach (var r in g.OrderByDescending(r => porRep.TryGetValue(r, out var l) ? l.Select(x => x.Sec).Distinct().Count() : 0).ThenBy(RepNom, StringComparer.CurrentCultureIgnoreCase))
                        filasR.Add(Fila("vendedor", g.Key, SupNom(r), r, RepNom(r), CentroNom(r), porRep.TryGetValue(r, out var ls) ? ls : new List<InvLinea>(), asig.TryGetValue(r, out var a2) ? a2 : new HashSet<int>()));
                }
                var asgTot = asig.Where(kv => universo.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).SelectMany(kv => kv.Value).ToHashSet();
                filasR.Add(Fila("total", "", "TOTAL", "", "", "", lineas, asgTot));
                return Ok(new { ok = true, vista, filas = filasR });
            }

            // ── 2. DETALLE: Suplidor › Categoría › Producto ───────────────────────────────────
            if (vista == "detalle")
            {
                var ls = lineas.Where(l => l.ProId > 0).ToList();
                object F(string nivel, int supId, string sup, int catId, string cat, int proId, string cod, string pro, List<InvLinea> x) => new
                {
                    Nivel = nivel, SupId = supId, Suplidor = sup, CatId = catId, Categoria = cat, ProId = proId, Codigo = cod, Producto = pro,
                    Inventarios = x.Select(l => (l.Rep, l.Sec)).Distinct().Count(), Clientes = x.Select(l => l.Cli).Distinct().Count(),
                    Productos = x.Select(l => l.ProId).Distinct().Count(), Unidades = x.Sum(l => l.Cant)
                };
                var filasD = new List<object>();
                foreach (var s in ls.GroupBy(l => (l.SupId, l.SupNom)).OrderByDescending(g => g.Sum(l => l.Cant)))
                {
                    filasD.Add(F("suplidor", s.Key.SupId, s.Key.SupNom, 0, "", 0, "", "", s.ToList()));
                    foreach (var c in s.GroupBy(l => (l.CatId, l.CatNom)).OrderByDescending(g => g.Sum(l => l.Cant)))
                    {
                        filasD.Add(F("categoria", s.Key.SupId, s.Key.SupNom, c.Key.CatId, c.Key.CatNom, 0, "", "", c.ToList()));
                        foreach (var p in c.GroupBy(l => (l.ProId, l.ProCod, l.ProNom)).OrderByDescending(g => g.Sum(l => l.Cant)))
                            filasD.Add(F("producto", s.Key.SupId, s.Key.SupNom, c.Key.CatId, c.Key.CatNom, p.Key.ProId, p.Key.ProCod, p.Key.ProNom, p.ToList()));
                    }
                }
                filasD.Add(F("total", 0, "TOTAL", 0, "", 0, "", "", ls));
                return Ok(new { ok = true, vista, filas = filasD });
            }

            // ── 3. CLIENTES: Supervisor › Vendedor › Cliente ──────────────────────────────────
            {
                object F(string nivel, string supCod, string sup, string repCod, string rep, int cliId, string cliCod, string cli, List<InvLinea> x) => new
                {
                    Nivel = nivel, SupCod = supCod, Supervisor = sup, RepCodigo = repCod, Vendedor = rep, CliID = cliId, CliCodigo = cliCod, Cliente = cli,
                    Inventarios = x.Select(l => (l.Rep, l.Sec)).Distinct().Count(), Clientes = x.Select(l => l.Cli).Distinct().Count(),
                    Ultimo = x.Count == 0 ? "" : FmtF(x.Max(l => l.Fecha)),
                    Productos = x.Where(l => l.ProId > 0).Select(l => l.ProId).Distinct().Count(), Unidades = x.Sum(l => l.Cant)
                };
                var filasC = new List<object>();
                foreach (var g in lineas.GroupBy(l => SupDe(l.Rep)).OrderBy(g => SupNom(g.First().Rep), StringComparer.CurrentCultureIgnoreCase))
                {
                    filasC.Add(F("supervisor", g.Key, SupNom(g.First().Rep), "", "", 0, "", "", g.ToList()));
                    foreach (var v in g.GroupBy(l => l.Rep).OrderByDescending(v => v.Select(l => l.Cli).Distinct().Count()))
                    {
                        filasC.Add(F("vendedor", g.Key, SupNom(v.Key), v.Key, RepNom(v.Key), 0, "", "", v.ToList()));
                        foreach (var c in v.GroupBy(l => l.Cli).OrderBy(c => c.First().CliNom, StringComparer.CurrentCultureIgnoreCase))
                            filasC.Add(F("cliente", g.Key, SupNom(v.Key), v.Key, RepNom(v.Key), c.Key, c.First().CliCod, c.First().CliNom, c.ToList()));
                    }
                }
                filasC.Add(F("total", "", "TOTAL", "", "", 0, "", "", lineas));
                return Ok(new { ok = true, vista, filas = filasC });
            }
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }
}
