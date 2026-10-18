using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

namespace DashVisitas.Controllers;

// ══════════════════════════════════════════════════════════════════════════════
//  OFERTAS Y DESCUENTOS (pestaña de Históricos — pedido explícito 2026-10-04 / ampliado 2026-10-05)
//  Un solo endpoint  GET /apihist/promos  con tipo=ofertas|descuentos y vista=
//    lista           → catálogo de promociones vigentes en el rango (filtrable por ids y estado)
//    dadas           → por Supervisor/Vendedor, de las promociones SELECCIONADAS
//    dias            → drill de un vendedor: días en que dio la(s) promoción(es) seleccionada(s)
//    clientes        → drill de un día: clientes y promociones de ese vendedor ese día (último nivel)
//    productividad   → ranking de promociones + tarjetas Top/Last de vendedores y promos
//    representantes  → árbol Centro › Supervisor › Vendedor con TODAS las promociones que les aplican
//                      (dadas y clientes impactados) para ver cuál usan más y cuál menos
//
//  Fuentes (esquema verificado en vivo contra MovilBusiness5STD, 2026-10-04):
//   OFERTAS    catálogo = vwOfertas (activas 'O' + históricas 'H', varias filas por OfeID);
//              vendedores elegibles = OfertasRepresentantes ∪ OfertasRepresentantesHistoricos;
//              uso = líneas de VWPedidosDetalle con OfeID>0. REGLA DEL VALOR: las líneas con
//              PedIndicadorOferta=1 son el producto REGALADO (valor 0 → unidades regaladas);
//              las de PedIndicadorOferta=0 con el mismo OfeID son el producto VENDIDO que trae
//              el valor real ("monto generado por la oferta").
//   DESCUENTOS catálogo = DescuentosRecargos (RepCodigos viene como JSON [{"repcodigo":"111"},…],
//              vacío = aplica a todos); uso = PedidosDescuentos × la línea de pedido. OJO:
//              PedidosDescuentos.PedDescuento es POR UNIDAD → monto descontado =
//              PedDescuento × PedCantidad de la línea (no sumar PedDescuento directo).
//  Mismos estatus de pedido (default 2/7/11) y filtros que la pestaña Pedidos. "Clientes
//  asignados" = RutaVisitas (mismo denominador que ya usan Activaciones y Perdidos).
//  Las líneas consultadas se guardan 3 min en memoria (clave = filtros que afectan el SQL)
//  para que los drills y cambios de vista no vuelvan a consultar la base.
// ══════════════════════════════════════════════════════════════════════════════
public partial class ApiHistoricosController
{
    private sealed class PromoCat
    {
        public string Id = "", Nombre = "";
        public DateTime Ini, Fin; public bool SinFin;
        public int CanID;
        public HashSet<string>? Elegibles;                 // null = aplica a todos los vendedores
        public Dictionary<int, string> Suplidores = new(), Categorias = new();
        public HashSet<int> Pros = new();
        public bool EnCatalogo = true;
    }
    private sealed record PromoLinea(string Pid, string Rep, int Cli, string CliCod, string CliNom, string Ped, string Fecha,
        double Monto, double MontoPromo, double Uni);
    private sealed class PromoAgg
    {
        public double Dadas, Monto, MontoPromo, Uni; public HashSet<int> Cli = new(); public HashSet<string> Reps = new();
        public void Sumar(PromoAgg o) { Dadas += o.Dadas; Monto += o.Monto; MontoPromo += o.MontoPromo; Uni += o.Uni; Cli.UnionWith(o.Cli); Reps.UnionWith(o.Reps); }
    }
    private sealed class PromoDatos
    {
        public Dictionary<string, PromoCat> Catalogo = new();
        public List<PromoLinea> Lineas = new();
        public Dictionary<string, HashSet<int>> Asig = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int>? PedRep;
    }

    private static readonly Regex RxRepCod = new("\"repcodigo\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Catálogo de promociones vigentes en [ini,fin] ────────────────────────────
    private Dictionary<string, PromoCat> CargarCatalogoPromos(SqlConnection cn, bool esOfe, DateOnly ini, DateOnly fin)
    {
        var cat = new Dictionary<string, PromoCat>();
        var iniS = ini.ToString("yyyy-MM-dd"); var finS = fin.AddDays(1).ToString("yyyy-MM-dd");
        SqlCommand Cmd(string sql) { var c = new SqlCommand(sql, cn) { CommandTimeout = 120 }; c.Parameters.AddWithValue("@ini", iniS); c.Parameters.AddWithValue("@fin", finS); return c; }

        if (esOfe)
        {
            using (var cmd = Cmd(@"
SELECT o.OfeID, MAX(o.OfeDescripcion) Nombre, MIN(o.OfeFechainicio) Ini, MAX(o.OfeFechaFin) Fin, MAX(ISNULL(o.CanID,0)) CanID
FROM vwOfertas o WITH (NOLOCK)
WHERE o.OfeFechainicio < @fin AND o.OfeFechaFin >= @ini
GROUP BY o.OfeID"))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                {
                    var id = Convert.ToInt32(rd.GetValue(0)).ToString();
                    cat[id] = new PromoCat { Id = id, Nombre = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim(),
                        Ini = rd.GetDateTime(2), Fin = rd.GetDateTime(3), CanID = rd.IsDBNull(4) ? 0 : Convert.ToInt32(rd.GetValue(4)) };
                }
            using (var cmd = Cmd(@"
SELECT OfeID, RepCodigo FROM OfertasRepresentantes WITH (NOLOCK)
 WHERE OfeID IN (SELECT OfeID FROM vwOfertas WITH (NOLOCK) WHERE OfeFechainicio < @fin AND OfeFechaFin >= @ini)
UNION
SELECT OfeID, RepCodigo FROM OfertasRepresentantesHistoricos WITH (NOLOCK)
 WHERE OfeID IN (SELECT OfeID FROM vwOfertas WITH (NOLOCK) WHERE OfeFechainicio < @fin AND OfeFechaFin >= @ini)"))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                {
                    if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
                    var id = Convert.ToInt32(rd.GetValue(0)).ToString();
                    if (!cat.TryGetValue(id, out var pc)) continue;
                    (pc.Elegibles ??= new(StringComparer.OrdinalIgnoreCase)).Add(rd.GetString(1).Trim());
                }
            using (var cmd = Cmd(@"
SELECT DISTINCT o.OfeID, pr.ProID, ISNULL(pr.Cat3ID,0) SupID, ISNULL(s.Cat3Descripcion,'(Sin suplidor)') Suplidor,
       ISNULL(pr.LinID,0) CatID, ISNULL(ln.LinDescripcion,'(Sin categoría)') Categoria
FROM vwOfertas o WITH (NOLOCK)
CROSS APPLY (SELECT o.ProID AS PID WHERE ISNULL(o.ProID,0)>0
             UNION SELECT gd.ProID FROM GrupoProductosDetalle gd WITH (NOLOCK)
                    WHERE ISNULL(o.GrpCodigo,'') NOT IN ('','0') AND gd.GrpCodigo=o.GrpCodigo) x
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=x.PID
LEFT JOIN Categoria3 s WITH (NOLOCK) ON s.Cat3ID=pr.Cat3ID
LEFT JOIN Lineas ln WITH (NOLOCK) ON ln.LinID=pr.LinID
WHERE o.OfeFechainicio < @fin AND o.OfeFechaFin >= @ini"))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read()) AgregarProducto(cat, Convert.ToInt32(rd.GetValue(0)).ToString(), rd);
        }
        else
        {
            using (var cmd = Cmd(@"
SELECT d.DesID, d.DesDescripcion, d.DesFechaInicio, d.DesFechaFin, ISNULL(d.RepCodigos,'') RepCodigos
FROM DescuentosRecargos d WITH (NOLOCK)
WHERE d.DesFechaInicio < @fin AND (d.DesFechaFin IS NULL OR d.DesFechaFin >= @ini)"))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                {
                    var id = Convert.ToInt32(rd.GetValue(0)).ToString();
                    var pc = new PromoCat { Id = id, Nombre = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim(), Ini = rd.GetDateTime(2) };
                    if (rd.IsDBNull(3)) { pc.SinFin = true; pc.Fin = DateTime.MaxValue.Date; } else pc.Fin = rd.GetDateTime(3);
                    var ms = RxRepCod.Matches(rd.GetString(4));
                    if (ms.Count > 0) pc.Elegibles = ms.Select(m => m.Groups[1].Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    cat[id] = pc;
                }
            using (var cmd = Cmd(@"
SELECT DISTINCT d.DesID, pr.ProID, ISNULL(pr.Cat3ID,0) SupID, ISNULL(s.Cat3Descripcion,'(Sin suplidor)') Suplidor,
       ISNULL(pr.LinID,0) CatID, ISNULL(ln.LinDescripcion,'(Sin categoría)') Categoria
FROM DescuentosRecargos d WITH (NOLOCK)
CROSS APPLY (SELECT d.ProID AS PID WHERE ISNULL(d.ProID,0)>0
             UNION SELECT gd.ProID FROM GrupoProductosDetalle gd WITH (NOLOCK)
                    WHERE ISNULL(d.GrpCodigo,'') NOT IN ('','0') AND gd.GrpCodigo=d.GrpCodigo) x
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=x.PID
LEFT JOIN Categoria3 s WITH (NOLOCK) ON s.Cat3ID=pr.Cat3ID
LEFT JOIN Lineas ln WITH (NOLOCK) ON ln.LinID=pr.LinID
WHERE d.DesFechaInicio < @fin AND (d.DesFechaFin IS NULL OR d.DesFechaFin >= @ini)"))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read()) AgregarProducto(cat, Convert.ToInt32(rd.GetValue(0)).ToString(), rd);
        }
        return cat;
    }

    private static void AgregarProducto(Dictionary<string, PromoCat> cat, string id, SqlDataReader rd)
    {
        if (!cat.TryGetValue(id, out var pc)) return;
        pc.Pros.Add(Convert.ToInt32(rd.GetValue(1)));
        pc.Suplidores[Convert.ToInt32(rd.GetValue(2))] = rd.IsDBNull(3) ? "" : rd.GetString(3).Trim();
        pc.Categorias[Convert.ToInt32(rd.GetValue(4))] = rd.IsDBNull(5) ? "" : rd.GetString(5).Trim();
    }

    // ── Uso real de las promociones: 1 fila por (promo, vendedor, pedido) ─────────
    private List<PromoLinea> CargarLineasPromo(SqlConnection cn, bool esOfe, DateOnly ini, DateOnly fin, List<int> lEstatus,
        List<string>? repsFiltro, List<int> lCanal, List<int> lSup, List<int> lCat, List<int> lPro, List<string> lGru,
        List<string> lGC, List<int> lTam, List<int> lCli)
    {
        var parms = new List<KeyValuePair<string, object>>();
        string In(string col, IEnumerable<object> vals, string pre)
        {
            var l = vals.ToList(); if (l.Count == 0) return "";
            var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        var w = In("p.PedEstatus", lEstatus.Cast<object>(), "es")
              + (repsFiltro is { Count: > 0 } ? In("p.RepCodigo", repsFiltro.Cast<object>(), "rp") : "")
              + In("ISNULL(cl.CanID,0)", lCanal.Cast<object>(), "cn")
              + In("pr.Cat3ID", lSup.Cast<object>(), "sp")
              + In("pr.LinID", lCat.Cast<object>(), "ct")
              + In("pd.ProID", lPro.Cast<object>(), "pp")
              + In("gd.GrpCodigo", lGru.Cast<object>(), "gr")
              + In("ISNULL(cl.ClaID,0)", lTam.Cast<object>(), "tm")
              + In("p.CliID", lCli.Cast<object>(), "cl");
        if (lGC.Count > 0)
            w += $" AND EXISTS (SELECT 1 FROM GrupoClientesDetalle gcd WITH (NOLOCK) WHERE gcd.CliID=p.CliID{In("gcd.GrcCodigo", lGC.Cast<object>(), "gc")})";
        var grupoJoin = lGru.Count > 0 ? "INNER JOIN GrupoProductosDetalle gd WITH (NOLOCK) ON gd.ProID=pd.ProID" : "";

        string sql = esOfe
            ? $@"
SELECT CAST(pd.OfeID AS varchar(20)) Pid, p.RepCodigo, p.CliID, p.PedSecuencia,
       SUM(CASE WHEN ISNULL(pd.PedIndicadorOferta,0)=0 THEN pd.PedCantidad*(pd.PedPrecio-ISNULL(pd.PedDescuento,0)) ELSE 0 END) Monto,
       CAST(0 AS float) MontoPromo,
       SUM(CASE WHEN ISNULL(pd.PedIndicadorOferta,0)=1 THEN pd.PedCantidad ELSE 0 END) Uni,
       CAST(p.PedFecha AS DATE) Fecha, ISNULL(cl.CliCodigo,'') CliCodigo, ISNULL(cl.CliNombre,'') CliNombre
FROM VWPedidos p WITH (NOLOCK)
INNER JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia=p.PedSecuencia AND pd.RepCodigo=p.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=pd.ProID
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.CliID=p.CliID
{grupoJoin}
WHERE p.PedFecha>=@ini AND p.PedFecha<@fin AND ISNULL(pd.OfeID,0)>0{w}
GROUP BY pd.OfeID, p.RepCodigo, p.CliID, p.PedSecuencia, CAST(p.PedFecha AS DATE), cl.CliCodigo, cl.CliNombre"
            : $@"
SELECT CAST(d.DesID AS varchar(20)) Pid, p.RepCodigo, p.CliID, p.PedSecuencia,
       SUM(pd.PedCantidad*(pd.PedPrecio-ISNULL(pd.PedDescuento,0))) Monto,
       SUM(ISNULL(d.PedDescuento,0)*pd.PedCantidad) MontoPromo,
       SUM(pd.PedCantidad) Uni,
       CAST(p.PedFecha AS DATE) Fecha, ISNULL(cl.CliCodigo,'') CliCodigo, ISNULL(cl.CliNombre,'') CliNombre
FROM PedidosDescuentos d WITH (NOLOCK)
INNER JOIN VWPedidos p WITH (NOLOCK) ON p.PedSecuencia=d.PedSecuencia AND p.RepCodigo=d.RepCodigo
INNER JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia=d.PedSecuencia AND pd.RepCodigo=d.RepCodigo AND pd.PedPosicion=d.PedPosicion
INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID=pd.ProID
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.CliID=p.CliID
{grupoJoin}
WHERE p.PedFecha>=@ini AND p.PedFecha<@fin{w}
GROUP BY d.DesID, p.RepCodigo, p.CliID, p.PedSecuencia, CAST(p.PedFecha AS DATE), cl.CliCodigo, cl.CliNombre";

        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 280 };
        cmd.Parameters.AddWithValue("@ini", ini.ToString("yyyy-MM-dd")); cmd.Parameters.AddWithValue("@fin", fin.AddDays(1).ToString("yyyy-MM-dd"));
        foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);
        var res = new List<PromoLinea>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
            res.Add(new PromoLinea(rd.GetString(0), rd.GetValue(1).ToString()!.Trim(),
                rd.IsDBNull(2) ? 0 : Convert.ToInt32(rd.GetValue(2)), rd.IsDBNull(8) ? "" : rd.GetString(8).Trim(), rd.IsDBNull(9) ? "" : rd.GetString(9).Trim(),
                rd.GetValue(3).ToString()!.Trim(), rd.IsDBNull(7) ? "" : Convert.ToDateTime(rd.GetValue(7)).ToString("yyyy-MM-dd"),
                rd.IsDBNull(4) ? 0 : Convert.ToDouble(rd.GetValue(4)), rd.IsDBNull(5) ? 0 : Convert.ToDouble(rd.GetValue(5)),
                rd.IsDBNull(6) ? 0 : Convert.ToDouble(rd.GetValue(6))));
        }
        return res;
    }

    // Vendedores con cartera asignada (RutaVisitas): son los que realmente "aplican" a una promo — la tabla
    // de elegibles de Ofertas arrastra vendedores dados de baja (329 de ~60 activos reales).
    private HashSet<string> RepsConCartera(SqlConnection cn)
    {
        var h = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand("SELECT DISTINCT RepCodigo FROM RutaVisitas WITH (NOLOCK)", cn) { CommandTimeout = 60 };
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) if (!rd.IsDBNull(0)) h.Add(rd.GetValue(0).ToString()!.Trim());
        return h;
    }

    // Pedidos distintos por vendedor en el rango (para saber quién "vendió" aunque no diera ninguna promo).
    private Dictionary<string, int> PedidosPorRep(SqlConnection cn, DateOnly ini, DateOnly fin, List<int> lEstatus, List<string>? repsFiltro)
    {
        var parms = new List<KeyValuePair<string, object>>();
        string In(string col, IEnumerable<object> vals, string pre)
        {
            var l = vals.ToList(); if (l.Count == 0) return "";
            var names = l.Select((v, i) => { var n = $"@{pre}{i}"; parms.Add(new(n, v)); return n; });
            return $" AND {col} IN ({string.Join(",", names)})";
        }
        var w = In("p.PedEstatus", lEstatus.Cast<object>(), "es") + (repsFiltro is { Count: > 0 } ? In("p.RepCodigo", repsFiltro.Cast<object>(), "rp") : "");
        using var cmd = new SqlCommand($@"SELECT p.RepCodigo, COUNT(DISTINCT p.PedSecuencia) FROM VWPedidos p WITH (NOLOCK)
WHERE p.PedFecha>=@ini AND p.PedFecha<@fin{w} GROUP BY p.RepCodigo", cn) { CommandTimeout = 180 };
        cmd.Parameters.AddWithValue("@ini", ini.ToString("yyyy-MM-dd")); cmd.Parameters.AddWithValue("@fin", fin.AddDays(1).ToString("yyyy-MM-dd"));
        foreach (var p in parms) cmd.Parameters.AddWithValue(p.Key, p.Value);
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) if (!rd.IsDBNull(0)) d[rd.GetValue(0).ToString()!.Trim()] = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd.GetValue(1));
        return d;
    }

    [HttpGet("promos")]
    public IActionResult Promos(
        [FromQuery] string? tipo, [FromQuery] string? vista, [FromQuery] string? fechaIni, [FromQuery] string? fechaFin, [FromQuery] string? ids,
        [FromQuery] string? estados,
        [FromQuery] string? centros, [FromQuery] string? supervisores, [FromQuery] string? vendedores, [FromQuery] string? divisiones,
        [FromQuery] string? canales, [FromQuery] string? suplidores, [FromQuery] string? categorias, [FromQuery] string? productos,
        [FromQuery] string? grupos, [FromQuery] string? gruposCliente, [FromQuery] string? tamanos, [FromQuery] string? clientes,
        [FromQuery] string? estatus, [FromQuery] string? detalle, [FromQuery] string? rep, [FromQuery] string? fecha)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesion expirada" });
        try
        {
            var esOfe = tipo != "descuentos";
            var tipoNom = esOfe ? "ofertas" : "descuentos";
            vista = vista is "dadas" or "dias" or "clientes" or "productividad" or "representantes" ? vista : "lista";
            var (ini, fin) = ParseRange(fechaIni, fechaFin);
            var lEstatus = ParseEstatus(estatus);
            var lCanal = ParseInts(canales); var lSup = ParseInts(suplidores); var lCat = ParseInts(categorias); var lPro = ParseInts(productos);
            var lGru = ParseList(grupos); var lGC = ParseList(gruposCliente); var lTam = ParseInts(tamanos); var lCli = ParseInts(clientes);
            var hoy = DateOnly.FromDateTime(DateTime.Today);
            var idsSel = ParseList(ids).ToHashSet();
            var estadosSel = ParseList(estados).ToHashSet(StringComparer.OrdinalIgnoreCase);

            using var cn = NewConn(); cn.Open();
            var reps = CargarReps(cn);
            var hayFiltroRep = ParseList(centros).Count + ParseList(supervisores).Count + ParseList(vendedores).Count + ParseList(divisiones).Count > 0;
            var (repsOkL, _) = RepsFiltrados(reps, centros, supervisores, vendedores, divisiones);
            var repsOk = repsOkL.ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool RepPasa(string r) => !hayFiltroRep || repsOk.Contains(r);
            string SupDe(string r) => reps.TryGetValue(r, out var ri) && ri.SupCod != "" ? ri.SupCod : "(sin)";
            string SupNom(string r) => reps.TryGetValue(r, out var ri) && ri.Supervisor != "" ? ri.Supervisor : "(Sin supervisor)";
            string RepNom(string r) => reps.TryGetValue(r, out var ri) ? ri.Nombre : r;
            string CentroDe(string r) => reps.TryGetValue(r, out var ri) ? ri.Centro : "";
            string CedDe(string r) => reps.TryGetValue(r, out var ri) && ri.CedCodigo != "" ? ri.CedCodigo : "(sin)";
            string CentroNom(string r) => reps.TryGetValue(r, out var ri) && ri.Centro != "" ? ri.Centro : "(Sin centro)";

            // Datos pesados (catálogo + líneas + cartera): una consulta por combinación de filtros del SQL, 3 min en memoria
            var clave = string.Join("|", tipoNom, ini, fin, string.Join(",", lEstatus), centros, supervisores, vendedores, divisiones, canales, suplidores,
                categorias, productos, grupos, gruposCliente, tamanos, clientes);
            var datos = cache.GetOrCreate("promos|" + clave, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3);
                var d = new PromoDatos { Catalogo = CargarCatalogoPromos(cn, esOfe, ini, fin) };
                d.Lineas = CargarLineasPromo(cn, esOfe, ini, fin, lEstatus, hayFiltroRep ? repsOkL : null, lCanal, lSup, lCat, lPro, lGru, lGC, lTam, lCli);
                foreach (var pid in d.Lineas.Select(l => l.Pid).Distinct())     // promos usadas pero sin vigencia en el rango: se conservan, marcadas
                    if (!d.Catalogo.ContainsKey(pid)) d.Catalogo[pid] = new PromoCat { Id = pid, Nombre = (esOfe ? "Oferta " : "Descuento ") + pid + " (sin catálogo vigente)", EnCatalogo = false };
                foreach (var (r, cli, _) in Asignaciones(cn, hayFiltroRep ? repsOkL : new List<string>(), lTam, lGC))
                { if (!d.Asig.TryGetValue(r, out var hs)) d.Asig[r] = hs = new(); hs.Add(cli); }
                return d;
            })!;
            var catalogo = datos.Catalogo; var asig = datos.Asig;
            var canalesNom = CargarCanales(cn);

            // ¿La promo pasa los filtros de catálogo (producto / vendedor / canal)?
            bool PromoPasa(PromoCat pc)
            {
                if (lSup.Count + lCat.Count + lPro.Count > 0)
                {
                    bool okProd = pc.Pros.Count > 0 &&
                        (lSup.Count == 0 || pc.Suplidores.Keys.Any(lSup.Contains)) &&
                        (lCat.Count == 0 || pc.Categorias.Keys.Any(lCat.Contains)) &&
                        (lPro.Count == 0 || pc.Pros.Any(lPro.Contains));
                    if (!okProd) return false;
                }
                if (hayFiltroRep && pc.Elegibles is not null && !pc.Elegibles.Any(repsOk.Contains)) return false;
                if (lCanal.Count > 0 && pc.CanID > 0 && !lCanal.Contains(pc.CanID)) return false;
                return true;
            }
            string EstadoDe(PromoCat pc)
            {
                var i = DateOnly.FromDateTime(pc.Ini); var f = pc.SinFin ? DateOnly.MaxValue : DateOnly.FromDateTime(pc.Fin);
                return !pc.EnCatalogo ? "Fuera de vigencia" : f < hoy ? "Finalizada" : i > hoy ? "Programada" : "Activa";
            }
            bool EstadoOk(PromoCat pc) => estadosSel.Count == 0 || estadosSel.Contains(EstadoDe(pc));
            // Promociones en alcance de los filtros (producto/vendedor/canal + estado + selección de promos)
            var catFiltrado = catalogo.Values.Where(pc => pc.EnCatalogo && PromoPasa(pc) && EstadoOk(pc) && (idsSel.Count == 0 || idsSel.Contains(pc.Id))).ToList();
            // + las usadas en el rango que ya no tienen vigencia (solo si no se filtró por estado)
            var extras = estadosSel.Count == 0 ? catalogo.Values.Where(pc => !pc.EnCatalogo && (idsSel.Count == 0 || idsSel.Contains(pc.Id))).ToList() : new List<PromoCat>();

            string Lista(IEnumerable<string> xs, int max = 4)
            {
                var l = xs.Where(x => x != "").Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
                return l.Count == 0 ? "—" : l.Count <= max ? string.Join(", ", l) : string.Join(", ", l.Take(max)) + $" (+{l.Count - max})";
            }
            string CentrosDe(PromoCat pc) => pc.Elegibles is null ? "Todos" : Lista(pc.Elegibles.Select(CentroDe));
            string CanalDe(PromoCat pc) => pc.CanID > 0 ? (canalesNom.TryGetValue(pc.CanID, out var cn2) ? cn2 : "Canal " + pc.CanID) : "Todos";
            string NombreDe(string pid) => catalogo.TryGetValue(pid, out var pc) ? pc.Nombre : pid;

            // ── 1. LISTA ───────────────────────────────────────────────────────────
            if (vista == "lista")
            {
                var conCartera = RepsConCartera(cn);
                var filas = catFiltrado.OrderBy(pc => pc.Fin).ThenBy(pc => pc.Nombre).Select(pc => new
                {
                    Id = pc.Id, Nombre = pc.Nombre,
                    Inicio = pc.Ini.ToString("yyyy-MM-dd"), Fin = pc.SinFin ? "" : pc.Fin.ToString("yyyy-MM-dd"),
                    Estado = EstadoDe(pc), Suplidor = Lista(pc.Suplidores.Values), Categoria = Lista(pc.Categorias.Values),
                    Canal = CanalDe(pc), Centro = CentrosDe(pc),
                    Vendedores = (pc.Elegibles ?? (IEnumerable<string>)conCartera).Count(r => conCartera.Contains(r) && RepPasa(r)), Productos = pc.Pros.Count
                }).ToList();
                return Ok(new { ok = true, tipo = tipoNom, vista, filas });
            }

            // vendedores a los que aplica una promo: lista configurada, o todos los que tienen cartera
            IEnumerable<string> ElegiblesDe(PromoCat pc) =>
                (pc.Elegibles ?? (IEnumerable<string>)asig.Keys).Where(r => asig.ContainsKey(r) && RepPasa(r));

            object FilaAgg(string nivel, string supCod, string sup, string repCod, string repN, string centro, string pid, string promo, PromoAgg a, int asignados)
            {
                var inc = asignados > 0 ? Math.Round(a.Cli.Count * 100.0 / asignados, 1) : 0;
                return new { Nivel = nivel, SupCod = supCod, Supervisor = sup, RepCodigo = repCod, Vendedor = repN, Centro = centro, PromoID = pid, Promo = promo,
                    Dadas = a.Dadas, Clientes = a.Cli.Count, Monto = Math.Round(a.Monto), MontoPromo = Math.Round(a.MontoPromo), Unidades = Math.Round(a.Uni), Asignados = asignados, IncidenciaPct = inc };
            }

            // ── 2. DADAS (promos seleccionadas) ────────────────────────────────────
            if (vista == "dadas")
            {
                if (idsSel.Count == 0) return Ok(new { ok = true, tipo = tipoNom, vista, filas = new List<object>(), mensaje = "Selecciona al menos una " + (esOfe ? "oferta" : "promoción de descuento") + "." });
                var lin = datos.Lineas.Where(l => idsSel.Contains(l.Pid)).ToList();
                var porRep = new Dictionary<string, PromoAgg>(StringComparer.OrdinalIgnoreCase);
                var porRepPromo = new Dictionary<(string, string), PromoAgg>();
                foreach (var l in lin)
                {
                    if (!porRep.TryGetValue(l.Rep, out var a)) porRep[l.Rep] = a = new();
                    if (!porRepPromo.TryGetValue((l.Rep, l.Pid), out var b)) porRepPromo[(l.Rep, l.Pid)] = b = new();
                    foreach (var x in new[] { a, b }) { x.Dadas++; x.Monto += l.Monto; x.MontoPromo += l.MontoPromo; x.Uni += l.Uni; x.Cli.Add(l.Cli); }
                }
                var universo = new HashSet<string>(porRep.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (var id in idsSel) if (catalogo.TryGetValue(id, out var pc)) foreach (var r in ElegiblesDe(pc)) universo.Add(r);

                var vacio = new PromoAgg();
                var filas = new List<object>();
                var totAgg = new PromoAgg();
                foreach (var g in universo.GroupBy(SupDe).Select(g => new { Sup = g.Key, Reps = g.ToList() })
                         .OrderByDescending(g => g.Reps.Sum(r => porRep.TryGetValue(r, out var a) ? a.Dadas : 0)).ThenBy(g => SupNom(g.Reps[0])))
                {
                    var sAgg = new PromoAgg(); var sAsig = new HashSet<int>();
                    foreach (var r in g.Reps)
                    {
                        if (porRep.TryGetValue(r, out var a)) sAgg.Sumar(a);
                        if (asig.TryGetValue(r, out var hs)) sAsig.UnionWith(hs);
                    }
                    filas.Add(FilaAgg("supervisor", g.Sup, SupNom(g.Reps[0]), "", "", "", "", "", sAgg, sAsig.Count));
                    foreach (var r in g.Reps.OrderByDescending(r => porRep.TryGetValue(r, out var a) ? a.Dadas : 0).ThenBy(RepNom, StringComparer.CurrentCultureIgnoreCase))
                    {
                        var ra = porRep.TryGetValue(r, out var aa) ? aa : vacio;
                        filas.Add(FilaAgg("vendedor", g.Sup, SupNom(g.Reps[0]), r, RepNom(r), CentroDe(r), "", "", ra, asig.TryGetValue(r, out var hs2) ? hs2.Count : 0));
                        if (detalle == "promo")
                            foreach (var pid in idsSel.Where(p => porRepPromo.ContainsKey((r, p))).OrderByDescending(p => porRepPromo[(r, p)].Dadas))
                                filas.Add(FilaAgg("promo", g.Sup, SupNom(g.Reps[0]), r, RepNom(r), CentroDe(r), pid, NombreDe(pid), porRepPromo[(r, pid)], 0));
                    }
                    totAgg.Sumar(sAgg);
                }
                var asigTotal = universo.Where(asig.ContainsKey).SelectMany(r => asig[r]).Distinct().Count();
                filas.Add(FilaAgg("total", "", "", "", "", "", "", "", totAgg, asigTotal));
                return Ok(new { ok = true, tipo = tipoNom, vista, filas, seleccion = idsSel.Select(id => new { Id = id, Nombre = NombreDe(id) }).ToList() });
            }

            // ── 2b. DÍAS: drill de un vendedor → días en que dio la(s) promo(s) seleccionada(s) ──
            if (vista == "dias")
            {
                var r0 = (rep ?? "").Trim();
                var lin = datos.Lineas.Where(l => string.Equals(l.Rep, r0, StringComparison.OrdinalIgnoreCase) && (idsSel.Count == 0 || idsSel.Contains(l.Pid))).ToList();
                var filas = lin.GroupBy(l => l.Fecha).OrderBy(g => g.Key).Select(g => new
                {
                    Fecha = g.Key, Dadas = g.Count(), Clientes = g.Select(l => l.Cli).Distinct().Count(),
                    Monto = Math.Round(g.Sum(l => l.Monto)), MontoPromo = Math.Round(g.Sum(l => l.MontoPromo)), Unidades = Math.Round(g.Sum(l => l.Uni)),
                    Promos = g.Select(l => l.Pid).Distinct().Count()
                }).ToList();
                var tot = new { Dadas = lin.Count, Clientes = lin.Select(l => l.Cli).Distinct().Count(), Monto = Math.Round(lin.Sum(l => l.Monto)),
                    MontoPromo = Math.Round(lin.Sum(l => l.MontoPromo)), Unidades = Math.Round(lin.Sum(l => l.Uni)) };
                return Ok(new { ok = true, tipo = tipoNom, vista, rep = r0, vendedor = RepNom(r0), supervisor = SupNom(r0), centro = CentroDe(r0), filas, total = tot });
            }

            // ── 2c. CLIENTES: último nivel — clientes y promociones de ese vendedor ese día ──
            if (vista == "clientes")
            {
                var r0 = (rep ?? "").Trim(); var f0 = (fecha ?? "").Trim();
                var lin = datos.Lineas.Where(l => string.Equals(l.Rep, r0, StringComparison.OrdinalIgnoreCase) && l.Fecha == f0 && (idsSel.Count == 0 || idsSel.Contains(l.Pid))).ToList();
                var filas = lin.GroupBy(l => (l.Cli, l.CliCod, l.CliNom, l.Pid)).Select(g => new
                {
                    CliID = g.Key.Cli, CliCodigo = g.Key.CliCod, CliNombre = g.Key.CliNom, PromoID = g.Key.Pid, Promo = NombreDe(g.Key.Pid),
                    Dadas = g.Count(), Monto = Math.Round(g.Sum(l => l.Monto)), MontoPromo = Math.Round(g.Sum(l => l.MontoPromo)), Unidades = Math.Round(g.Sum(l => l.Uni)),
                    Pedidos = string.Join(", ", g.Select(l => l.Ped).Distinct())
                }).OrderBy(x => x.CliNombre, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Promo).ToList();
                var tot = new { Dadas = lin.Count, Clientes = lin.Select(l => l.Cli).Distinct().Count(), Monto = Math.Round(lin.Sum(l => l.Monto)),
                    MontoPromo = Math.Round(lin.Sum(l => l.MontoPromo)), Unidades = Math.Round(lin.Sum(l => l.Uni)) };
                return Ok(new { ok = true, tipo = tipoNom, vista, rep = r0, vendedor = RepNom(r0), supervisor = SupNom(r0), fecha = f0, filas, total = tot });
            }

            // ── Base común de productividad y representantes: líneas dentro del alcance de filtros/estado/selección ──
            var idsAlcance = new HashSet<string>(catFiltrado.Select(c => c.Id)); idsAlcance.UnionWith(extras.Select(c => c.Id));
            var linAlc = datos.Lineas.Where(l => idsAlcance.Contains(l.Pid)).ToList();
            var promosAlc = catFiltrado.Concat(extras).ToList();

            // ── 4. REPRESENTANTES: árbol Centro › Supervisor › Vendedor con todas las promos que les aplican ──
            if (vista == "representantes")
            {
                var porRepPromo = new Dictionary<(string, string), PromoAgg>();
                foreach (var l in linAlc)
                {
                    if (!porRepPromo.TryGetValue((l.Rep, l.Pid), out var b)) porRepPromo[(l.Rep, l.Pid)] = b = new();
                    b.Dadas++; b.Monto += l.Monto; b.MontoPromo += l.MontoPromo; b.Uni += l.Uni; b.Cli.Add(l.Cli);
                }
                bool Aplica(string r, PromoCat pc) => porRepPromo.ContainsKey((r, pc.Id)) || (pc.EnCatalogo && (pc.Elegibles is null ? asig.ContainsKey(r) : pc.Elegibles.Contains(r)));
                var repsAlc = asig.Keys.Concat(linAlc.Select(l => l.Rep)).Where(RepPasa).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                object Entidad(string cve, string nombre, List<string> rs, Func<List<object>>? hijos, string sub = "")
                {
                    var promos = new List<(PromoCat pc, PromoAgg a)>(); var tot = new PromoAgg();
                    foreach (var pc in promosAlc)
                    {
                        if (!rs.Any(r => Aplica(r, pc))) continue;
                        var a = new PromoAgg();
                        foreach (var r in rs) if (porRepPromo.TryGetValue((r, pc.Id), out var x)) a.Sumar(x);
                        promos.Add((pc, a)); tot.Sumar(a);
                    }
                    var pl = promos.OrderByDescending(p => p.a.Dadas).ThenByDescending(p => p.a.Cli.Count).ThenBy(p => p.pc.Nombre).Select(p => new
                    {
                        PromoID = p.pc.Id, Promo = p.pc.Nombre, Estado = EstadoDe(p.pc), Dadas = p.a.Dadas, Clientes = p.a.Cli.Count,
                        Monto = Math.Round(p.a.Monto), MontoPromo = Math.Round(p.a.MontoPromo), Unidades = Math.Round(p.a.Uni)
                    }).ToList();
                    return new
                    {
                        Clave = cve, Nombre = nombre, Sub = sub, Vendedores = rs.Count, Dadas = tot.Dadas, Clientes = tot.Cli.Count, Monto = Math.Round(tot.Monto),
                        MontoPromo = Math.Round(tot.MontoPromo), Unidades = Math.Round(tot.Uni), PromosAplican = pl.Count, PromosUsadas = pl.Count(p => p.Dadas > 0),
                        Promos = pl, Hijos = hijos?.Invoke() ?? new List<object>()
                    };
                }
                var centrosL = repsAlc.GroupBy(CedDe).OrderBy(g => CentroNom(g.First())).Select(gc =>
                {
                    var rsC = gc.ToList();
                    return Entidad(gc.Key, CentroNom(rsC[0]), rsC, () => rsC.GroupBy(SupDe).OrderBy(gs => SupNom(gs.First())).Select(gs =>
                    {
                        var rsS = gs.ToList();
                        return Entidad(gs.Key, SupNom(rsS[0]), rsS, () => rsS.OrderBy(RepNom, StringComparer.CurrentCultureIgnoreCase)
                            .Select(r => Entidad(r, RepNom(r), new List<string> { r }, null, CentroNom(r))).ToList(), CentroNom(rsS[0]));
                    }).ToList());
                }).ToList();
                return Ok(new { ok = true, tipo = tipoNom, vista, centros = centrosL, promosEnAlcance = promosAlc.Count });
            }

            // ── 3. PRODUCTIVIDAD ──────────────────────────────────────────────────
            {
                var lin = linAlc;
                var porPromo = new Dictionary<string, PromoAgg>(); var porRep = new Dictionary<string, PromoAgg>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in lin)
                {
                    if (!porPromo.TryGetValue(l.Pid, out var a)) porPromo[l.Pid] = a = new();
                    if (!porRep.TryGetValue(l.Rep, out var b)) porRep[l.Rep] = b = new();
                    foreach (var x in new[] { a, b }) { x.Dadas++; x.Monto += l.Monto; x.MontoPromo += l.MontoPromo; x.Uni += l.Uni; x.Cli.Add(l.Cli); x.Reps.Add(l.Rep); }
                }
                int DiasVigentes(PromoCat pc)
                {
                    if (!pc.EnCatalogo) return 0;
                    var d0 = DateOnly.FromDateTime(pc.Ini) > ini ? DateOnly.FromDateTime(pc.Ini) : ini;
                    var d1 = pc.SinFin ? fin : (DateOnly.FromDateTime(pc.Fin) < fin ? DateOnly.FromDateTime(pc.Fin) : fin);
                    return Math.Max(0, d1.DayNumber - d0.DayNumber + 1);
                }
                var promos = promosAlc.Select(pc =>
                {
                    var a = porPromo.TryGetValue(pc.Id, out var x) ? x : new PromoAgg();
                    // los que la dieron cuentan como elegibles aunque la tabla no los liste (así la cobertura nunca pasa de 100%)
                    var eleg = pc.EnCatalogo ? ElegiblesDe(pc).Union(a.Reps, StringComparer.OrdinalIgnoreCase).Count() : a.Reps.Count;
                    return new
                    {
                        Id = pc.Id, Nombre = pc.Nombre, Inicio = pc.EnCatalogo ? pc.Ini.ToString("yyyy-MM-dd") : "", Fin = pc.EnCatalogo && !pc.SinFin ? pc.Fin.ToString("yyyy-MM-dd") : "",
                        Estado = EstadoDe(pc), Suplidor = Lista(pc.Suplidores.Values, 2), DiasVigentes = DiasVigentes(pc),
                        Dadas = a.Dadas, Clientes = a.Cli.Count, Monto = Math.Round(a.Monto), MontoPromo = Math.Round(a.MontoPromo), Unidades = Math.Round(a.Uni),
                        VendedoresQueDieron = a.Reps.Count, Elegibles = eleg, CoberturaPct = eleg > 0 ? Math.Round(a.Reps.Count * 100.0 / eleg, 1) : 0
                    };
                }).OrderByDescending(p => p.Dadas).ThenByDescending(p => p.Monto).ThenBy(p => p.Nombre).ToList();

                // Universo de vendedores para el "Last 5": aplican a alguna promo vigente Y tuvieron pedidos en el rango
                var pedRep = datos.PedRep ??= PedidosPorRep(cn, ini, fin, lEstatus, hayFiltroRep ? repsOkL : null);
                var universoV = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pc in catFiltrado) foreach (var r in ElegiblesDe(pc)) universoV.Add(r);
                universoV.UnionWith(porRep.Keys);
                object V(string r) { var a = porRep.TryGetValue(r, out var x) ? x : new PromoAgg();
                    return new { RepCodigo = r, Vendedor = RepNom(r), Supervisor = SupNom(r), Centro = CentroDe(r), Dadas = a.Dadas, Clientes = a.Cli.Count, Monto = Math.Round(a.Monto),
                                 MontoPromo = Math.Round(a.MontoPromo), Asignados = asig.TryGetValue(r, out var hs) ? hs.Count : 0, Pedidos = pedRep.TryGetValue(r, out var n) ? n : 0 }; }
                double D(string r) => porRep.TryGetValue(r, out var a) ? a.Dadas : 0;
                var topV = porRep.Keys.OrderByDescending(D).ThenBy(RepNom).Take(5).Select(V).ToList();
                var lastV = universoV.Where(r => pedRep.TryGetValue(r, out var n) && n > 0).OrderBy(D).ThenBy(RepNom).Take(5).Select(V).ToList();
                var conUso = promos.Where(p => p.Dadas > 0).Take(3).ToList();
                var menos = promos.Where(p => catalogo[p.Id].EnCatalogo).OrderBy(p => p.Dadas).ThenByDescending(p => p.DiasVigentes).ThenBy(p => p.Nombre).Take(3).ToList();
                var resumen = new
                {
                    PromosVigentes = catFiltrado.Count, PromosConUso = promos.Count(p => p.Dadas > 0), Dadas = promos.Sum(p => p.Dadas),
                    Clientes = lin.Select(l => l.Cli).Distinct().Count(), Monto = Math.Round(lin.Sum(l => l.Monto)), MontoPromo = Math.Round(lin.Sum(l => l.MontoPromo)),
                    VendedoresQueDieron = porRep.Count
                };
                return Ok(new { ok = true, tipo = tipoNom, vista, promos, resumen, tarjetas = new { topVendedores = topV, lastVendedores = lastV, topPromos = conUso, lastPromos = menos } });
            }
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }
}
