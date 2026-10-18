using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

// ── "Reunión Gerencial" (Supervisión Dinámica → PDF Presentaciones → Informes Gerenciales) ──
// Pedido del usuario 2026-10-05: llevar a la app el "motor" del Informe Ejecutivo de Cierre de
// Ventas mensual (scripts Python de Giancarlo: charts.py / build_xlsx*.py / build_pdf.py +
// data.py) — el usuario solo elige Mes y Año y el informe se arma solo.
//
// Reglas de datos (las mismas del motor, verificadas contra sep-2026 al centavo):
//   · Base = venta NETA sin ITBIS = ValorBruto (nunca ValorVenta).
//   · Excluye Inversiones Caobo en todo, salvo el apéndice de Caobo.
//   · Excluye vendedor 89 (Oficina) en categorías/proveedores/vendedores/ABCD.
//   · % Forecast = presupuesto oficial con Canal<>'Institucional'.
//   · VWVENTASRESUMEN{año} trae TAMBIÉN el año siguiente → filtrar SIEMPRE por [Año].
//   · Pera con pera COMPLETO y simétrico: del año anterior se sacan TODAS las marcas sin venta
//     este año (+ categoría GALLETERIA siempre); del año actual, las marcas nuevas. La lista se
//     recalcula cada vez (nunca se hereda) y se devuelve para auditoría.
// Narrativa: el backend solo entrega datos; el borrador de conclusiones lo arma el front y el
// usuario lo edita antes de descargar (PDF client-side; Excel por POST /excel con lo editado).
[ApiController]
[Route("apipdfpres/reunion-gerencial")]
[RequiereModulo("supervision")]
public class ApiReunionGerencialController(IConfiguration config, ILogger<ApiReunionGerencialController> log, IWebHostEnvironment env) : ControllerBase
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));
    private bool IsAuth => HttpContext.Session.GetString("user_login") is not null;

    private const string CAOBO = "INVERSIONES CAOBO%";
    private const string REP_OFICINA = "89";

    // Cache: la vista VWVENTASRESUMEN es pesada (~60s el informe completo, medido sep-2026).
    //  · Memoria (30 min) para cualquier mes — re-generar / descargar no vuelve a la BD.
    //  · Disco (App_Data/reunion_gerencial/AAAA-MM.json) SOLO para meses ya terminados: un mes
    //    cerrado no cambia, así que se consulta una vez y luego abre al instante.
    //  "forzar=1" (botón "Actualizar datos") salta ambos y re-escribe el archivo.
    private static readonly ConcurrentDictionary<string, (DateTime en, string json)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOut = new(JsonSerializerDefaults.Web);
    private string RutaCache(int a, int m) => Path.Combine(env.ContentRootPath, "App_Data", "reunion_gerencial", $"{a}-{m:00}.json");

    // ── GET /apipdfpres/reunion-gerencial?anio=2026&mes=9 ──────────────────────────────
    [HttpGet("")]
    public async Task<IActionResult> Generar([FromQuery] int anio, [FromQuery] int mes, [FromQuery] int forzar = 0)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (anio < 2020 || anio > 2100 || mes < 1 || mes > 12) return BadRequest(new { ok = false, error = "Mes/año inválido" });
        var hoy = DateTime.Today;
        if (anio > hoy.Year || (anio == hoy.Year && mes > hoy.Month))
            return BadRequest(new { ok = false, error = "Ese mes todavía no ha empezado." });

        var key = $"{anio}-{mes}";
        bool mesTerminado = new DateTime(anio, mes, 1).AddMonths(1) <= hoy;
        var ruta = RutaCache(anio, mes);
        if (forzar == 0)
        {
            if (Cache.TryGetValue(key, out var c) && DateTime.Now - c.en < CacheTtl) return Content(c.json, "application/json");
            if (mesTerminado && System.IO.File.Exists(ruta))
            {
                var j = await System.IO.File.ReadAllTextAsync(ruta);
                Cache[key] = (DateTime.Now, j);
                return Content(j, "application/json");
            }
        }
        try
        {
            var json = JsonSerializer.Serialize(await Construir(anio, mes), JsonOut);
            Cache[key] = (DateTime.Now, json);
            if (mesTerminado)
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(ruta)!); await System.IO.File.WriteAllTextAsync(ruta, json); }
                catch (Exception ex) { log.LogWarning(ex, "No se pudo guardar cache de Reunión Gerencial en {Ruta}", ruta); }
            }
            return Content(json, "application/json");
        }
        catch (InvalidOperationException ex) { return BadRequest(new { ok = false, error = ex.Message }); }
        catch (Exception ex)
        {
            log.LogError(ex, "Reunión Gerencial {Anio}-{Mes}", anio, mes);
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }

    // ── Ejecución de consultas (cada una con su propia conexión → corren en paralelo) ──
    private async Task<List<Dictionary<string, object?>>> Q(string sql, params (string, object)[] ps)
    {
        await using var cn = NewConn();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 };
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        await using var rd = await cmd.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await rd.ReadAsync())
        {
            var r = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rd.FieldCount; i++) r[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i);
            rows.Add(r);
        }
        return rows;
    }

    private static double D(Dictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0;
    private static int I(Dictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
    private static string S(Dictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null ? (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Trim() : "";

    // "Santo Domnigo" (sic, en ventas) / "Centro Santo Domingo" (presupuesto) → "Santo Domingo".
    private static string Centro(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("Centro ", StringComparison.OrdinalIgnoreCase)) s = s[7..].Trim();
        if (s.Equals("Santo Domnigo", StringComparison.OrdinalIgnoreCase)) s = "Santo Domingo";
        return s == "" ? "(Sin centro)" : s;
    }

    private static readonly string[] MESES = { "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };

    private async Task<HashSet<string>> VistasExistentes()
    {
        var rows = await Q("SELECT name FROM sys.views WHERE name LIKE 'VWVENTASRESUMEN20%' OR name LIKE 'VWPresupuestosAlDiaUnidadesProductos%'");
        return rows.Select(r => S(r, "name").ToUpperInvariant()).ToHashSet();
    }

    private async Task<object> Construir(int A, int M)
    {
        int P = A - 1;
        var vistas = await VistasExistentes();
        string vA = $"VWVENTASRESUMEN{A}", vP = $"VWVENTASRESUMEN{P}";
        if (!vistas.Contains(vA.ToUpperInvariant())) throw new InvalidOperationException($"No existe la vista de ventas {vA} en Movil Business.");
        bool hayPrev = vistas.Contains(vP.ToUpperInvariant());
        string vPre = vistas.Contains($"VWPRESUPUESTOSALDIAUNIDADESPRODUCTOS{A}") ? $"VWPresupuestosAlDiaUnidadesProductos{A}" : "VWPresupuestosAlDiaUnidadesProductos";

        int m3 = Math.Max(1, M - 2);
        var desde = new DateTime(A, M, 1); var hasta = desde.AddMonths(1);
        var pAM = new (string, object)[] { ("@a", A), ("@m", M) };

        var tForecast = Q($@"SELECT Centro,Canal,SUM(PrePresupuestoMonto) presupuesto,SUM(PreEjecutadoMonto) ejecutado,SUM(Proyectado) proyectado
            FROM {vPre} WHERE PreMes=@m AND PreAnio=@a GROUP BY Centro,Canal", pAM);
        var tSerieA = Q($@"SELECT Centro,Mes,SUM(ValorBruto) neto FROM {vA} WHERE [Año]=@a AND Mes<=@m AND ClienteNombre NOT LIKE '{CAOBO}' GROUP BY Centro,Mes", pAM);
        var tSerieP = hayPrev ? Q($@"SELECT Centro,Mes,SUM(ValorBruto) neto FROM {vP} WHERE [Año]=@p AND ClienteNombre NOT LIKE '{CAOBO}' GROUP BY Centro,Mes", ("@p", P)) : Task.FromResult(new List<Dictionary<string, object?>>());
        // Rendimiento: UNA sola lectura del mes al grano centro×vendedor×cliente×factura×suplidor×categoría×segmento
        // reemplaza ~8 consultas separadas (categorías, proveedores, vendedores, ABCD, pera con pera, Caobo…) que
        // en paralelo competían entre sí en el servidor (63s medido con sep-2026).
        const string SEG = "CASE WHEN TamañoCliente LIKE 'A%' THEN 'A' WHEN TamañoCliente LIKE 'B%' THEN 'B' WHEN TamañoCliente LIKE 'C%' THEN 'C' WHEN TamañoCliente LIKE 'D%' THEN 'D' ELSE 'SC' END";
        const string CAO = "CASE WHEN ClienteNombre LIKE '" + CAOBO + "' THEN 1 ELSE 0 END";
        var tDet = Q($@"SELECT Centro,CodigoVendedor rep,VendedorGrupo grupo,MAX(NombreVendedor) nombre,ClienteCodigo cli,{CAO} cao,DocNum doc,SUPLIDOR sup,Categoria cat,{SEG} seg,
              SUM(ValorBruto) n,SUM(ValorVenta) it
            FROM {vA} WHERE [Año]=@a AND Mes=@m
            GROUP BY Centro,CodigoVendedor,VendedorGrupo,ClienteCodigo,{CAO},DocNum,SUPLIDOR,Categoria,{SEG}", pAM);
        var tAbcdPrev = M > m3 ? Q($@"SELECT Centro,Mes,seg,SUM(neto) neto,COUNT(DISTINCT ClienteCodigo) clientes FROM (
              SELECT Centro,Mes,ClienteCodigo,ValorBruto neto,{SEG} seg
              FROM {vA} WHERE [Año]=@a AND Mes BETWEEN @m3 AND @m1 AND ClienteNombre NOT LIKE '{CAOBO}' AND CodigoVendedor<>'{REP_OFICINA}') x
            GROUP BY Centro,Mes,seg", ("@a", A), ("@m1", M - 1), ("@m3", m3)) : Task.FromResult(new List<Dictionary<string, object?>>());
        var tCuota = Q($@"SELECT RepCodigo,MAX(RepNombre) nombre,MAX(Centro) Centro,MAX(VendedorGrupo) grupo,SUM(PrePresupuestoMonto) cuota,SUM(PreEjecutadoMonto) ejec
            FROM {vPre} WHERE PreMes=@m AND PreAnio=@a AND Canal<>'Institucional' GROUP BY RepCodigo", pAM);
        var tColo = Q($@"SELECT Centro,ProductoDescripcion sku,SUM(ValorBruto) neto,COUNT(DISTINCT ClienteCodigo) clientes
            FROM {vA} WHERE [Año]=@a AND Mes=@m AND SUPLIDOR='COLOMBINA' AND ClienteNombre NOT LIKE '{CAOBO}' AND ValorBruto>0 GROUP BY Centro,ProductoDescripcion", pAM);
        var tEfect = Q(@"SELECT RepCodigo,SUM(CASE WHEN VisEstatus IN (1,2) THEN 1 ELSE 0 END) realizadas,SUM(CASE WHEN VisEstatus=2 THEN 1 ELSE 0 END) con_pedido
            FROM vwVisitas WHERE VisFechaEntrada>=@d AND VisFechaEntrada<@h AND VisTipoVisita='1' GROUP BY RepCodigo", ("@d", desde), ("@h", hasta));
        // Año anterior, mismo mes: suplidor × centro × galletería × Caobo (pera con pera + apéndice Caobo).
        var tPrevMes = hayPrev ? Q($@"SELECT Centro,SUPLIDOR sup,CASE WHEN Categoria='GALLETERIA' THEN 1 ELSE 0 END gal,{CAO} cao,SUM(ValorBruto) n,SUM(ValorVenta) it
            FROM {vP} WHERE [Año]=@p AND Mes=@m GROUP BY Centro,SUPLIDOR,CASE WHEN Categoria='GALLETERIA' THEN 1 ELSE 0 END,{CAO}", ("@p", P), ("@m", M)) : Task.FromResult(new List<Dictionary<string, object?>>());

        await Task.WhenAll(tForecast, tSerieA, tSerieP, tDet, tAbcdPrev, tCuota, tColo, tEfect, tPrevMes);

        var serieARows = tSerieA.Result;
        if (!serieARows.Any(r => I(r, "Mes") == M)) throw new InvalidOperationException($"No hay ventas registradas para {MESES[M - 1]} {A}.");

        var det = tDet.Result.Select(r => new Det(Centro(S(r, "Centro")), S(r, "rep"), S(r, "grupo"), S(r, "nombre"), S(r, "cli"), I(r, "cao") == 1,
            S(r, "doc"), S(r, "sup"), S(r, "cat"), S(r, "seg"), D(r, "n"), D(r, "it"))).ToList();
        // "Analítico" = sin Caobo y sin la cuenta Oficina 89 (regla del motor para categorías/proveedores/vendedores/ABCD).
        var anal = det.Where(x => !x.Cao && x.Rep != REP_OFICINA).ToList();
        var analPos = anal.Where(x => x.N > 0).ToList(); // equivalente a ValorBruto>0 (separa devoluciones)

        // ── Centros (orden: Santo Domingo, Azua, luego el resto por venta) ──
        var ventaMes = serieARows.Where(r => I(r, "Mes") == M).GroupBy(r => Centro(S(r, "Centro"))).ToDictionary(g => g.Key, g => g.Sum(r => D(r, "neto")));
        var centros = ventaMes.Keys.OrderBy(c => c == "Santo Domingo" ? 0 : c == "Azua" ? 1 : 2).ThenByDescending(c => ventaMes[c]).ToList();

        // ── Serie mensual ──
        Dictionary<string, double[]> Serie(List<Dictionary<string, object?>> rows) =>
            centros.ToDictionary(c => c, c => Enumerable.Range(1, 12).Select(m => rows.Where(r => Centro(S(r, "Centro")) == c && I(r, "Mes") == m).Sum(r => D(r, "neto"))).ToArray());
        var serieA = Serie(serieARows); var serieP = Serie(tSerieP.Result);

        // ── Forecast por centro/canal ──
        var forecast = tForecast.Result.Select(r => new
        {
            centro = Centro(S(r, "Centro")), canal = S(r, "Canal"),
            presupuesto = D(r, "presupuesto"), ejecutado = D(r, "ejecutado"), proyectado = D(r, "proyectado"),
        }).OrderBy(x => centros.IndexOf(x.centro) < 0 ? 99 : centros.IndexOf(x.centro)).ThenBy(x => x.canal).ToList();
        bool mesCerrado = forecast.Count > 0 && forecast.All(f => Math.Abs(f.ejecutado - f.proyectado) < 1);
        var hoy = DateTime.Today;
        if (A == hoy.Year && M == hoy.Month) mesCerrado = false;

        // ── Categorías (Top 17 + Otros por centro) ──
        var categorias = centros.ToDictionary(c => c, c =>
        {
            var l = anal.Where(x => x.Centro == c).GroupBy(x => x.Cat).Select(g => (n: g.Key, v: g.Sum(x => x.N))).OrderByDescending(x => x.v).ToList();
            var top = l.Take(17).Select(x => new { nombre = x.n == "" ? "(Sin categoría)" : x.n, neto = x.v }).ToList();
            var otros = l.Skip(17).Sum(x => x.v);
            if (otros != 0) top.Add(new { nombre = "Otros", neto = otros });
            return top;
        });

        // ── ABCD trimestre (mes actual desde el detalle; meses previos del trimestre por consulta) ──
        string[] segs = { "A", "B", "C", "D", "SC" };
        var abcd = centros.ToDictionary(c => c, c => Enumerable.Range(m3, M - m3 + 1).ToDictionary(m => m.ToString(), m => segs.ToDictionary(s => s, s =>
        {
            if (m == M)
            {
                var l = anal.Where(x => x.Centro == c && x.Seg == s).ToList();
                return new { neto = l.Sum(x => x.N), clientes = l.Select(x => x.Cli).Distinct().Count() };
            }
            var r = tAbcdPrev.Result.FirstOrDefault(x => Centro(S(x, "Centro")) == c && I(x, "Mes") == m && S(x, "seg") == s);
            return new { neto = r == null ? 0 : D(r, "neto"), clientes = r == null ? 0 : I(r, "clientes") };
        })));

        // ── Proveedores ──
        var proveedores = centros.ToDictionary(c => c, c => analPos.Where(x => x.Centro == c).GroupBy(x => x.Sup)
            .Select(g => new { nombre = g.Key, neto = g.Sum(x => x.N), clientes = g.Select(x => x.Cli).Distinct().Count(), facturas = g.Select(x => x.Doc).Distinct().Count() })
            .OrderByDescending(x => x.neto).ToList());

        // ── Vendedores: unión venta ∪ cuota (presupuesto), sin Oficina 89 ──
        var efect = tEfect.Result.ToDictionary(r => S(r, "RepCodigo"), r => (real: I(r, "realizadas"), ped: I(r, "con_pedido")));
        var ventaRep = analPos.GroupBy(x => x.Rep).ToDictionary(g => g.Key, g =>
        {
            var top = g.GroupBy(x => (x.Centro, x.Grupo)).OrderByDescending(h => h.Sum(x => x.N)).First().Key;
            return (centro: top.Centro, grupo: top.Grupo, nombre: g.Select(x => x.Nombre).FirstOrDefault(n => n != "") ?? "",
                    neto: g.Sum(x => x.N), clientes: g.Select(x => x.Cli).Distinct().Count(), facturas: g.Select(x => x.Doc).Distinct().Count());
        });
        var cuotaRep = tCuota.Result.Where(r => S(r, "RepCodigo") != REP_OFICINA).ToDictionary(r => S(r, "RepCodigo"), r => r);
        var vendedores = ventaRep.Keys.Union(cuotaRep.Keys).Where(k => k != "" && k != REP_OFICINA).Select(k =>
        {
            ventaRep.TryGetValue(k, out var v); cuotaRep.TryGetValue(k, out var q);
            bool hayV = ventaRep.ContainsKey(k);
            efect.TryGetValue(k, out var e);
            return new
            {
                codigo = k,
                centro = hayV ? v.centro : Centro(S(q!, "Centro")),
                grupo = hayV ? v.grupo : S(q!, "grupo"),
                nombre = hayV && v.nombre != "" ? v.nombre : (q != null ? S(q, "nombre") : "Cod " + k),
                neto = hayV ? v.neto : 0, clientes = hayV ? v.clientes : 0, facturas = hayV ? v.facturas : 0,
                cuota = q != null ? D(q, "cuota") : 0, ejecutado = q != null ? D(q, "ejec") : 0,
                visitas = e.real, conPedido = e.ped,
            };
        }).Where(v => v.cuota > 0 || v.neto > 0).OrderBy(v => v.centro).ThenByDescending(v => v.cuota > 0 ? v.ejecutado / v.cuota : -1).ToList();

        // ── Cartera distinta por centro (clientes en ruta de los vendedores del centro) ──
        var cartera = new Dictionary<string, int>();
        foreach (var c in centros)
        {
            var reps = vendedores.Where(v => v.centro == c).Select(v => v.codigo).Distinct().ToList();
            if (reps.Count == 0) { cartera[c] = 0; continue; }
            var ps = reps.Select((r, i) => ($"@r{i}", (object)r)).ToArray();
            var rows = await Q($"SELECT COUNT(DISTINCT CliID) n FROM vwRutavisitaclientes WHERE RepCodigo IN ({string.Join(",", ps.Select(p => p.Item1))})", ps);
            cartera[c] = rows.Count > 0 ? I(rows[0], "n") : 0;
        }
        var diasHabiles = Enumerable.Range(1, DateTime.DaysInMonth(A, M)).Count(d => new DateTime(A, M, d).DayOfWeek != DayOfWeek.Sunday);
        var operacion = centros.ToDictionary(c => c, c =>
        {
            var vs = vendedores.Where(v => v.centro == c).ToList();
            int real = vs.Sum(v => v.visitas), ped = vs.Sum(v => v.conPedido);
            var pos = analPos.Where(x => x.Centro == c).ToList();
            int cli = pos.Select(x => x.Cli).Distinct().Count(), fac = pos.Select(x => x.Doc).Distinct().Count();
            var venta = ventaMes.GetValueOrDefault(c);
            return new
            {
                venta, porDia = diasHabiles > 0 ? venta / diasHabiles : 0,
                facturas = fac, ticket = fac > 0 ? venta / fac : 0,
                activos = cli, cartera = cartera[c], activacion = cartera[c] > 0 ? (double)cli / cartera[c] : (double?)null,
                visitas = real, conPedido = ped, efectividad = real > 0 ? (double)ped / real : (double?)null,
            };
        });

        // ── Colombina Top 5 SKU ──
        var colombina = centros.ToDictionary(c => c, c =>
        {
            var l = tColo.Result.Where(r => Centro(S(r, "Centro")) == c).Select(r => new { sku = S(r, "sku"), neto = D(r, "neto"), clientes = I(r, "clientes") }).OrderByDescending(x => x.neto).ToList();
            return new { total = l.Sum(x => x.neto), top = l.Take(5).ToList() };
        });

        // ── Pera con pera (like-for-like), COMPLETO y simétrico ──
        // Filas sin Caobo de ambos años al grano centro × suplidor × galletería.
        var lflA = det.Where(x => !x.Cao).Select(x => (centro: x.Centro, sup: x.Sup, gal: x.Cat == "GALLETERIA", n: x.N)).ToList();
        var prevRows = tPrevMes.Result;
        var lflP = prevRows.Where(r => I(r, "cao") == 0).Select(r => (centro: Centro(S(r, "Centro")), sup: S(r, "sup"), gal: I(r, "gal") == 1, n: D(r, "n"))).ToList();
        var supA = lflA.GroupBy(x => x.sup).ToDictionary(g => g.Key, g => g.Sum(x => x.n));
        var supP = lflP.GroupBy(x => x.sup).ToDictionary(g => g.Key, g => g.Sum(x => x.n));
        var todos = supA.Keys.Union(supP.Keys).ToList();
        // Descontinuada = movía algo el año anterior y este año ~0 (≤ 2% o ≤ RD$1,000). Nueva = al revés.
        // Se excluyen de AMBOS años (como el motor de Python), incluido algún residuo/nota de crédito.
        bool Casi0(double v, double refv) => v <= Math.Max(1000, 0.02 * Math.Abs(refv));
        var descontinuadas = todos.Where(s => Math.Abs(supP.GetValueOrDefault(s)) > 0.5 && Casi0(supA.GetValueOrDefault(s), supP.GetValueOrDefault(s)))
            .Select(s => new { suplidor = s, anterior = supP.GetValueOrDefault(s), actual = supA.GetValueOrDefault(s) }).OrderByDescending(x => x.anterior).ToList();
        var nuevas = todos.Where(s => supA.GetValueOrDefault(s) > 0 && Casi0(supP.GetValueOrDefault(s), supA.GetValueOrDefault(s)))
            .Select(s => new { suplidor = s, anterior = supP.GetValueOrDefault(s), actual = supA.GetValueOrDefault(s) }).OrderByDescending(x => x.actual).ToList();
        var fuera = descontinuadas.Select(x => x.suplidor).Concat(nuevas.Select(x => x.suplidor)).ToHashSet();
        double galP = lflP.Where(x => x.gal && !fuera.Contains(x.sup)).Sum(x => x.n);
        double galA = lflA.Where(x => x.gal && !fuera.Contains(x.sup)).Sum(x => x.n);
        var cmpP = centros.ToDictionary(c => c, c => lflP.Where(x => x.centro == c && !x.gal && !fuera.Contains(x.sup)).Sum(x => x.n));
        var cmpA = centros.ToDictionary(c => c, c => lflA.Where(x => x.centro == c && !x.gal && !fuera.Contains(x.sup)).Sum(x => x.n));
        double rP = centros.Sum(c => serieP[c][M - 1]), rA = centros.Sum(c => serieA[c][M - 1]);
        double tcP = cmpP.Values.Sum(), tcA = cmpA.Values.Sum();
        var lfl = new
        {
            hayAnterior = hayPrev && rP > 0,
            reportadoAnterior = rP, reportadoActual = rA, comparableAnterior = tcP, comparableActual = tcA,
            salidas = tcP - rP, deltaBase = tcA - tcP, entradas = rA - tcA,
            yoyReportado = rP > 0 ? rA / rP - 1 : (double?)null,
            yoyComparable = tcP > 0 ? tcA / tcP - 1 : (double?)null,
            porCentro = centros.ToDictionary(c => c, c => new
            {
                comparableAnterior = cmpP[c], comparableActual = cmpA[c],
                reportadoAnterior = serieP[c][M - 1], reportadoActual = serieA[c][M - 1],
                yoyComparable = cmpP[c] > 0 ? cmpA[c] / cmpP[c] - 1 : (double?)null,
                yoyReportado = serieP[c][M - 1] > 0 ? serieA[c][M - 1] / serieP[c][M - 1] - 1 : (double?)null,
            }),
            descontinuadas, nuevas, galleteriaAnterior = galP, galleteriaActual = galA,
        };

        // ── Caobo por suplidor (ÚNICA parte que incluye Caobo) ──
        var cA = det.Where(x => x.Cao).GroupBy(x => x.Sup).ToDictionary(g => g.Key, g => (n: g.Sum(x => x.N), it: g.Sum(x => x.It)));
        var cP = prevRows.Where(r => I(r, "cao") == 1).GroupBy(r => S(r, "sup")).ToDictionary(g => g.Key, g => g.Sum(r => D(r, "n")));
        var caobo = cA.Keys.Union(cP.Keys).Select(s => new
        {
            suplidor = s,
            neto = cA.GetValueOrDefault(s).n, conItbis = cA.GetValueOrDefault(s).it, netoAnterior = cP.GetValueOrDefault(s),
        }).Where(x => Math.Abs(x.neto) > 0.5 || Math.Abs(x.netoAnterior) > 0.5).OrderByDescending(x => x.neto).ToList();

        // ── Reconciliación ventas vs ejecutado oficial (ex-Institucional) ──
        double ejecOficial = forecast.Where(f => !f.canal.Equals("Institucional", StringComparison.OrdinalIgnoreCase)).Sum(f => f.ejecutado);

        return new
        {
            ok = true,
            meta = new
            {
                anio = A, mes = M, anioAnterior = P, mesNombre = MESES[M - 1], diasHabiles, mesCerrado,
                trimestre = Enumerable.Range(m3, M - m3 + 1).ToArray(), vistaPresupuesto = vPre,
                generado = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            },
            centros, serieActual = serieA, serieAnterior = serieP, forecast, categorias, abcd, proveedores,
            vendedores, operacion, colombina, lfl, caobo,
            reconciliacion = new { ventas = rA, ejecutadoOficial = ejecOficial, diferenciaPct = ejecOficial != 0 ? rA / ejecOficial - 1 : (double?)null },
        };
    }

    // ── POST /apipdfpres/reunion-gerencial/excel ───────────────────────────────────────
    // Recibe el MISMO JSON que devolvió GET (data), la narrativa ya editada en pantalla y
    // las gráficas como PNG (dataURL) → arma el Excel-cerebro multi-hoja con ClosedXML.
    public sealed class ExcelReq
    {
        public JsonElement Data { get; set; }
        public Dictionary<string, string>? Narr { get; set; }
        public List<List<string>>? Acciones { get; set; }
        public Dictionary<string, string>? Graficas { get; set; }
    }

    private static readonly XLColor VERDE = XLColor.FromHtml("#0C9444"), AZUL = XLColor.FromHtml("#203F99"), GRIS = XLColor.FromHtml("#6E6E6E"),
        CARBON = XLColor.FromHtml("#3A3A3A"), AZCLARO = XLColor.FromHtml("#E7EBF5"), ROJO = XLColor.FromHtml("#C0392B"), BORDE = XLColor.FromHtml("#D9D9D9");
    private const string FRD = "\"RD$\"#,##0;(\"RD$\"#,##0);-", FPCT = "0.0%", FNUM = "#,##0";

    [HttpPost("excel")]
    [RequestSizeLimit(40_000_000)]
    public IActionResult Excel([FromBody] ExcelReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            var d = req.Data; var narr = req.Narr ?? new(); var gra = req.Graficas ?? new();
            var meta = d.GetProperty("meta");
            int A = meta.GetProperty("anio").GetInt32(), M = meta.GetProperty("mes").GetInt32(), P = A - 1;
            string MN = meta.GetProperty("mesNombre").GetString() ?? "";
            var centros = d.GetProperty("centros").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            string N(string k) => narr.TryGetValue(k, out var v) ? v : "";

            using var wb = new XLWorkbook();
            wb.Style.Font.FontName = "Arial"; wb.Style.Font.FontSize = 10;

            IXLWorksheet Hoja(string nombre, string titulo, string? sub)
            {
                var ws = wb.Worksheets.Add(nombre); ws.ShowGridLines = false; ws.Column(1).Width = 3;
                ws.Cell(1, 2).Value = titulo; ws.Cell(1, 2).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(CARBON);
                if (!string.IsNullOrWhiteSpace(sub)) { ws.Cell(2, 2).Value = sub; ws.Cell(2, 2).Style.Font.SetFontSize(9).Font.SetFontColor(GRIS); }
                return ws;
            }
            void Hdr(IXLWorksheet ws, int r, int c, params string[] h)
            {
                for (int i = 0; i < h.Length; i++)
                {
                    var cell = ws.Cell(r, c + i); cell.Value = h[i];
                    cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(VERDE)
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetWrapText(true)
                        .Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(BORDE);
                }
            }
            void V(IXLCell cell, object? v, string? fmt = null, bool b = false, XLColor? bg = null, XLColor? fg = null)
            {
                switch (v)
                {
                    case null: break;
                    case string s when s.StartsWith('='): cell.FormulaA1 = s[1..]; break;
                    case string s: cell.Value = s; break;
                    case double x: cell.Value = x; break;
                    case int x: cell.Value = x; break;
                    default: cell.Value = Convert.ToString(v, CultureInfo.InvariantCulture); break;
                }
                if (fmt != null) cell.Style.NumberFormat.Format = fmt;
                if (b) cell.Style.Font.SetBold();
                if (bg != null) cell.Style.Fill.SetBackgroundColor(bg);
                if (fg != null) cell.Style.Font.SetFontColor(fg);
                cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(BORDE);
            }
            void Nota(IXLWorksheet ws, int r, string txt, int hastaCol = 9)
            {
                if (string.IsNullOrWhiteSpace(txt)) return;
                var rg = ws.Range(r, 2, r, hastaCol); rg.Merge(); rg.Value = txt;
                rg.Style.Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top).Font.SetFontSize(9).Font.SetFontColor(GRIS);
                ws.Row(r).Height = Math.Max(15, Math.Ceiling(txt.Length / 110.0) * 13);
            }
            void Img(IXLWorksheet ws, string key, int fila, int col, double escala = 0.55)
            {
                if (!gra.TryGetValue(key, out var url) || string.IsNullOrEmpty(url)) return;
                var i = url.IndexOf("base64,", StringComparison.Ordinal); if (i < 0) return;
                var ms = new MemoryStream(Convert.FromBase64String(url[(i + 7)..]));
                var pic = ws.AddPicture(ms).MoveTo(ws.Cell(fila, col)); pic.Scale(escala);
            }
            static double Dv(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
            static double? Dn(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            static string Sv(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            string Sem(double p) => p >= 1 ? "Verde" : p >= 0.85 ? "Ámbar" : "Rojo";

            var serieA = d.GetProperty("serieActual"); var serieP = d.GetProperty("serieAnterior");
            var forecast = d.GetProperty("forecast").EnumerateArray().ToList();
            var lfl = d.GetProperty("lfl");
            bool EsInst(JsonElement f) => Sv(f, "canal").Equals("Institucional", StringComparison.OrdinalIgnoreCase);
            double PresuC(string c) => forecast.Where(f => Sv(f, "centro") == c && !EsInst(f)).Sum(f => Dv(f, "presupuesto"));
            double EjecC(string c) => forecast.Where(f => Sv(f, "centro") == c && !EsInst(f)).Sum(f => Dv(f, "ejecutado"));
            double SerieV(JsonElement s, string c, int m) => s.TryGetProperty(c, out var arr) ? arr[m - 1].GetDouble() : 0;

            // ── Hoja 1: Resumen ──
            var ws = Hoja("Resumen", $"INFORME EJECUTIVO DE CIERRE DE VENTAS — {MN} {A}",
                $"Reunión Gerencial · Fuente: Movil Business · Generado {Sv(meta, "generado")} · Base: venta NETA sin ITBIS · Excluye Inversiones Caobo");
            foreach (var (col, w) in new[] { (2, 24), (3, 16), (4, 16), (5, 12), (6, 9), (7, 13), (8, 15) }) ws.Column(col).Width = w;
            Hdr(ws, 4, 2, "Vista", $"Ejecutado {MN}", "Forecast", "% Forecast", "Sem.", "Δ% interanual", "Δ% pera con pera");
            int r = 5;
            var filasRes = new List<(string n, double e, double p, double? y, double? yl)> {
                ("Total AM", centros.Sum(EjecC), centros.Sum(PresuC), Dn(lfl, "yoyReportado"), Dn(lfl, "yoyComparable")) };
            foreach (var c in centros)
            {
                var pc = lfl.GetProperty("porCentro").GetProperty(c);
                filasRes.Add((c, EjecC(c), PresuC(c), Dn(pc, "yoyReportado"), Dn(pc, "yoyComparable")));
            }
            foreach (var (n, e, p, y, yl) in filasRes)
            {
                var bg = r == 5 ? AZCLARO : null;
                V(ws.Cell(r, 2), n, b: r == 5, bg: bg); V(ws.Cell(r, 3), e, FRD, bg: bg); V(ws.Cell(r, 4), p, FRD, bg: bg);
                V(ws.Cell(r, 5), $"=IF(D{r}=0,0,C{r}/D{r})", FPCT, bg: bg); V(ws.Cell(r, 6), p > 0 ? Sem(e / p) : "—", bg: bg);
                V(ws.Cell(r, 7), y, FPCT, bg: bg, fg: (y ?? 0) < 0 ? ROJO : VERDE); V(ws.Cell(r, 8), yl, FPCT, bg: bg, fg: (yl ?? 0) < 0 ? ROJO : VERDE);
                r++;
            }
            r++; Nota(ws, r++, N("CAP_KPI"), 8); Nota(ws, r++, N("CAP_BRIDGE"), 8);
            Img(ws, "bridge", r + 1, 2, 0.6); Img(ws, "series", r + 24, 2, 0.6);

            // ── Hoja 2: Capa 1 ──
            ws = Hoja("Capa 1 - General", "CAPA 1 · Plataforma general — Serie mensual neta (RD$, ex-Caobo)", N("CAPA1_INTRO"));
            ws.Column(2).Width = 22; for (int i = 3; i <= 16; i++) ws.Column(i).Width = 12;
            r = 4; Hdr(ws, r, 2, new[] { "Serie / Mes" }.Concat(Enumerable.Range(1, M).Select(m => MESES_ABBR[m - 1])).Append($"PM {MESES_ABBR[M - 1]} (3m)").ToArray());
            var filasSerie = new List<(string, Func<int, double>)> { ($"Total AM {A}", m => centros.Sum(c => SerieV(serieA, c, m))) };
            foreach (var c in centros) filasSerie.Add(($"{c} {A}", m => SerieV(serieA, c, m)));
            filasSerie.Add(($"Total AM {P}", m => centros.Sum(c => SerieV(serieP, c, m))));
            foreach (var c in centros) filasSerie.Add(($"{c} {P}", m => SerieV(serieP, c, m)));
            foreach (var (n, f) in filasSerie)
            {
                r++; V(ws.Cell(r, 2), n, b: n.StartsWith("Total"));
                for (int m = 1; m <= M; m++) V(ws.Cell(r, 2 + m), f(m), FRD);
                int c0 = Math.Max(1, M - 2) + 2, c1 = M + 2;
                V(ws.Cell(r, M + 3), $"=AVERAGE({ws.Cell(r, c0).Address}:{ws.Cell(r, c1).Address})", FRD, b: true);
            }
            r += 2; ws.Cell(r, 2).Value = $"Forecast {MN} {A} por centro y canal (RD$)"; ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(11); r++;
            Hdr(ws, r, 2, "Centro", "Canal", "Presupuesto", "Ejecutado", "% Cumpl.", "Sem."); int f0 = r + 1;
            foreach (var f in forecast)
            {
                r++; var p = Dv(f, "presupuesto"); var e = Dv(f, "ejecutado");
                V(ws.Cell(r, 2), Sv(f, "centro")); V(ws.Cell(r, 3), Sv(f, "canal") + (EsInst(f) ? " (Caobo)*" : ""));
                V(ws.Cell(r, 4), p, FRD); V(ws.Cell(r, 5), e, FRD); V(ws.Cell(r, 6), $"=IF(D{r}=0,0,E{r}/D{r})", FPCT); V(ws.Cell(r, 7), p > 0 ? Sem(e / p) : "—");
            }
            r++; V(ws.Cell(r, 2), "TOTAL (ex-Institucional)", b: true, bg: AZCLARO); V(ws.Cell(r, 3), "", bg: AZCLARO);
            V(ws.Cell(r, 4), centros.Sum(PresuC), FRD, true, AZCLARO); V(ws.Cell(r, 5), centros.Sum(EjecC), FRD, true, AZCLARO);
            V(ws.Cell(r, 6), $"=IF(D{r}=0,0,E{r}/D{r})", FPCT, true, AZCLARO); V(ws.Cell(r, 7), "", bg: AZCLARO);
            r += 2; Nota(ws, r, "* Institucional = Inversiones Caobo (aliado): se excluye del % Forecast.");
            Nota(ws, r + 1, N("CAP_SERIES"));
            Img(ws, "series", r + 3, 2, 0.6); Img(ws, "forecast", r + 3, 10, 0.6);

            // ── Bloques por centro ──
            var vendedores = d.GetProperty("vendedores").EnumerateArray().ToList();
            int bi = 0;
            foreach (var c in centros)
            {
                var color = c == "Santo Domingo" ? AZUL : VERDE;
                var nombreHoja = (c == "Santo Domingo" ? "Bloque A - SDO" : c == "Azua" ? "Bloque B - Sur (Azua)" : $"Bloque {(char)('A' + bi)} - {c}");
                if (nombreHoja.Length > 31) nombreHoja = nombreHoja[..31];
                bi++;
                ws = Hoja(nombreHoja, $"{c} — Cierre {MN} {A} (neto, ex-Caobo)", N($"C{bi}_intro"));
                foreach (var (col, w) in new[] { (2, 34), (3, 30), (4, 15), (5, 15), (6, 11), (7, 10), (8, 11), (9, 12) }) ws.Column(col).Width = w;
                var op = d.GetProperty("operacion").GetProperty(c);
                r = 4; ws.Cell(r, 2).Value = "1 · Indicadores de operación"; ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontColor(color); r++;
                Hdr(ws, r, 2, "Indicador", "Valor");
                var ind = new (string, object?, string)[] {
                    ($"Venta neta {MN}", Dv(op,"venta"), FRD), ($"Productividad por día hábil (÷{Dv(meta,"diasHabiles")})", Dv(op,"porDia"), FRD),
                    ("Ticket promedio (venta ÷ facturas)", Dv(op,"ticket"), FRD), ("Clientes activos (facturados)", (int)Dv(op,"activos"), FNUM),
                    ("Cartera (clientes en ruta, distinta)", (int)Dv(op,"cartera"), FNUM), ("Activación de cartera", Dn(op,"activacion"), FPCT),
                    ("Efectividad de visita (con pedido ÷ realizadas)", Dn(op,"efectividad"), FPCT) };
                foreach (var (n, v, f) in ind) { r++; V(ws.Cell(r, 2), n); V(ws.Cell(r, 3), v, f); }
                r++; Nota(ws, ++r, N($"C{bi}_op"));

                r += 2; ws.Cell(r, 2).Value = "2 · Proveedores (Top 4 + Otros)"; ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontColor(color); r++;
                Hdr(ws, r, 2, "Proveedor", $"Neto {MESES_ABBR[M - 1]}", "% part.", "Clientes", "Facturas", "Ticket", "Prod/cliente");
                var prov = d.GetProperty("proveedores").GetProperty(c).EnumerateArray().ToList();
                double totProv = prov.Sum(x => Dv(x, "neto"));
                var filasP = prov.Take(4).Select(x => (Sv(x, "nombre"), Dv(x, "neto"), Dv(x, "clientes"), Dv(x, "facturas"))).ToList();
                if (prov.Count > 4) filasP.Add(("Otros", prov.Skip(4).Sum(x => Dv(x, "neto")), prov.Skip(4).Sum(x => Dv(x, "clientes")), prov.Skip(4).Sum(x => Dv(x, "facturas"))));
                int p0 = r + 1;
                foreach (var (n, ne, cl, fa) in filasP)
                {
                    r++; V(ws.Cell(r, 2), n, b: n != "Otros"); V(ws.Cell(r, 3), ne, FRD); V(ws.Cell(r, 4), totProv > 0 ? ne / totProv : 0, FPCT);
                    V(ws.Cell(r, 5), cl, FNUM); V(ws.Cell(r, 6), fa, FNUM); V(ws.Cell(r, 7), fa > 0 ? ne / fa : 0, FRD); V(ws.Cell(r, 8), cl > 0 ? ne / cl : 0, FRD);
                }
                r++; V(ws.Cell(r, 2), "TOTAL centro", b: true, bg: AZCLARO); V(ws.Cell(r, 3), $"=SUM(C{p0}:C{r - 1})", FRD, true, AZCLARO);
                V(ws.Cell(r, 4), $"=SUM(D{p0}:D{r - 1})", FPCT, true, AZCLARO); for (int k = 5; k <= 8; k++) V(ws.Cell(r, k), "", bg: AZCLARO);
                r++; Nota(ws, ++r, N($"C{bi}_prov"));

                r += 2; ws.Cell(r, 2).Value = $"3 · Clientes A·B·C·D ({MN} {A})"; ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontColor(color); r++;
                Hdr(ws, r, 2, "Segmento", $"Neto {MESES_ABBR[M - 1]}", "% part.", "Clientes", "Prod/cliente");
                var ab = d.GetProperty("abcd").GetProperty(c).GetProperty(M.ToString());
                var segs = new[] { ("A Grande", "A"), ("B Mediano", "B"), ("C Pequeño", "C"), ("D Muy pequeño", "D"), ("Sin clasif.", "SC") };
                double totAb = segs.Sum(s => Dv(ab.GetProperty(s.Item2), "neto")); int a0 = r + 1;
                foreach (var (lbl, s) in segs)
                {
                    r++; var ne = Dv(ab.GetProperty(s), "neto"); var cl = Dv(ab.GetProperty(s), "clientes");
                    V(ws.Cell(r, 2), lbl); V(ws.Cell(r, 3), ne, FRD); V(ws.Cell(r, 4), totAb > 0 ? ne / totAb : 0, FPCT); V(ws.Cell(r, 5), cl, FNUM); V(ws.Cell(r, 6), cl > 0 ? ne / cl : 0, FRD);
                }
                r++; V(ws.Cell(r, 2), "TOTAL", b: true, bg: AZCLARO); V(ws.Cell(r, 3), $"=SUM(C{a0}:C{r - 1})", FRD, true, AZCLARO);
                V(ws.Cell(r, 4), $"=SUM(D{a0}:D{r - 1})", FPCT, true, AZCLARO); V(ws.Cell(r, 5), $"=SUM(E{a0}:E{r - 1})", FNUM, true, AZCLARO); V(ws.Cell(r, 6), "", bg: AZCLARO);
                r++; Nota(ws, ++r, N($"C{bi}_abcd"));

                r += 2; ws.Cell(r, 2).Value = "4 · Fuerza de venta — Cumplimiento de cuota + Efectividad"; ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontColor(color); r++;
                Hdr(ws, r, 2, "Grupo", "Vendedor", "Cuota", "Ejecutado", "% Cuota", "Sem.", "Efectiv.", "Venta neta");
                int v0 = r + 1;
                foreach (var v in vendedores.Where(v => Sv(v, "centro") == c))
                {
                    r++; var cu = Dv(v, "cuota"); var ej = Dv(v, "ejecutado"); var vi = Dv(v, "visitas");
                    V(ws.Cell(r, 2), "Grupo " + Sv(v, "grupo")); V(ws.Cell(r, 3), $"{Sv(v, "nombre")} ({Sv(v, "codigo")})");
                    V(ws.Cell(r, 4), cu, FRD); V(ws.Cell(r, 5), ej, FRD);
                    if (cu > 0) { V(ws.Cell(r, 6), $"=E{r}/D{r}", FPCT); V(ws.Cell(r, 7), Sem(ej / cu)); } else { V(ws.Cell(r, 6), "s/cuota", fg: GRIS); V(ws.Cell(r, 7), "—"); }
                    V(ws.Cell(r, 8), vi > 0 ? Dv(v, "conPedido") / vi : null, FPCT); V(ws.Cell(r, 9), Dv(v, "neto"), FRD);
                }
                r++; V(ws.Cell(r, 2), "TOTAL centro", b: true, bg: AZCLARO); V(ws.Cell(r, 3), "", bg: AZCLARO);
                V(ws.Cell(r, 4), $"=SUM(D{v0}:D{r - 1})", FRD, true, AZCLARO); V(ws.Cell(r, 5), $"=SUM(E{v0}:E{r - 1})", FRD, true, AZCLARO);
                V(ws.Cell(r, 6), $"=IF(D{r}=0,0,E{r}/D{r})", FPCT, true, AZCLARO); V(ws.Cell(r, 7), "", bg: AZCLARO);
                V(ws.Cell(r, 8), Dn(op, "efectividad"), FPCT, true, AZCLARO); V(ws.Cell(r, 9), $"=SUM(I{v0}:I{r - 1})", FRD, true, AZCLARO);
                r++; Nota(ws, ++r, N($"C{bi}_ffvv")); Nota(ws, ++r, N($"C{bi}_senal"));
                Img(ws, $"prov_{bi}", 4, 11, 0.5); Img(ws, $"abcd_{bi}", 26, 11, 0.5); Img(ws, $"gauss_{bi}", 48, 11, 0.5);
            }

            // ── Pera con pera (auditoría) ──
            ws = Hoja("Pera con pera", $"Comparable pera con pera — {MN} {A} vs {MN} {P}",
                "Mismo portafolio en ambos años: del año anterior se excluyen TODAS las marcas sin venta este año y la categoría GALLETERIA; del año actual, las marcas nuevas. Lista recalculada para este mes.");
            foreach (var (col, w) in new[] { (2, 34), (3, 18), (4, 18), (5, 14) }) ws.Column(col).Width = w;
            r = 4; Hdr(ws, r, 2, "Concepto", $"{MN} {P}", $"{MN} {A}", "Δ%");
            void Fila3(string n, double a, double b, bool tot = false)
            {
                r++; var bg = tot ? AZCLARO : null; V(ws.Cell(r, 2), n, b: tot, bg: bg); V(ws.Cell(r, 3), a, FRD, tot, bg); V(ws.Cell(r, 4), b, FRD, tot, bg);
                V(ws.Cell(r, 5), a != 0 ? b / a - 1 : null, FPCT, tot, bg, (a != 0 && b / a - 1 < 0) ? ROJO : VERDE);
            }
            Fila3("Reportado (ex-Caobo)", Dv(lfl, "reportadoAnterior"), Dv(lfl, "reportadoActual"));
            Fila3("Comparable pera con pera", Dv(lfl, "comparableAnterior"), Dv(lfl, "comparableActual"), true);
            foreach (var c in centros) { var pc = lfl.GetProperty("porCentro").GetProperty(c); Fila3("   " + c + " (comparable)", Dv(pc, "comparableAnterior"), Dv(pc, "comparableActual")); }
            r += 2; ws.Cell(r, 2).Value = $"Marcas excluidas del año {P} (descontinuadas)"; ws.Cell(r, 2).Style.Font.SetBold(); r++;
            Hdr(ws, r, 2, "Suplidor", $"Neto {MN} {P}", $"Neto {MN} {A}");
            foreach (var x in lfl.GetProperty("descontinuadas").EnumerateArray()) { r++; V(ws.Cell(r, 2), Sv(x, "suplidor")); V(ws.Cell(r, 3), Dv(x, "anterior"), FRD); V(ws.Cell(r, 4), Dv(x, "actual"), FRD); }
            r++; V(ws.Cell(r, 2), "Categoría GALLETERIA (resto de suplidores)"); V(ws.Cell(r, 3), Dv(lfl, "galleteriaAnterior"), FRD); V(ws.Cell(r, 4), Dv(lfl, "galleteriaActual"), FRD);
            r += 2; ws.Cell(r, 2).Value = $"Marcas excluidas del año {A} (nuevas)"; ws.Cell(r, 2).Style.Font.SetBold(); r++;
            Hdr(ws, r, 2, "Suplidor", $"Neto {MN} {P}", $"Neto {MN} {A}");
            foreach (var x in lfl.GetProperty("nuevas").EnumerateArray()) { r++; V(ws.Cell(r, 2), Sv(x, "suplidor")); V(ws.Cell(r, 3), Dv(x, "anterior"), FRD); V(ws.Cell(r, 4), Dv(x, "actual"), FRD); }

            // ── Colombina ──
            ws = Hoja("Apéndice Colombina", $"Apéndice · Colombina — Top 5 SKU por CEDIS ({MN} {A})", N("COLO_INTRO"));
            foreach (var (col, w) in new[] { (2, 36), (3, 15), (4, 14), (5, 11), (6, 13) }) ws.Column(col).Width = w;
            r = 4;
            foreach (var c in centros)
            {
                var co = d.GetProperty("colombina").GetProperty(c); var tot = Dv(co, "total");
                ws.Cell(r, 2).Value = $"{c} — Colombina total RD${tot:N0}"; ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontColor(c == "Santo Domingo" ? AZUL : VERDE); r++;
                Hdr(ws, r, 2, "SKU", "Neto", "% de Colombina", "Clientes", "Prod/cliente"); int k0 = r + 1;
                foreach (var x in co.GetProperty("top").EnumerateArray())
                {
                    r++; var ne = Dv(x, "neto"); var cl = Dv(x, "clientes");
                    V(ws.Cell(r, 2), Sv(x, "sku")); V(ws.Cell(r, 3), ne, FRD); V(ws.Cell(r, 4), tot > 0 ? ne / tot : 0, FPCT); V(ws.Cell(r, 5), cl, FNUM); V(ws.Cell(r, 6), cl > 0 ? ne / cl : 0, FRD);
                }
                r++; V(ws.Cell(r, 2), "Top 5 subtotal", b: true, bg: AZCLARO); V(ws.Cell(r, 3), $"=SUM(C{k0}:C{r - 1})", FRD, true, AZCLARO);
                V(ws.Cell(r, 4), $"=SUM(D{k0}:D{r - 1})", FPCT, true, AZCLARO); V(ws.Cell(r, 5), "", bg: AZCLARO); V(ws.Cell(r, 6), "", bg: AZCLARO);
                r += 2;
            }
            Nota(ws, r, N("COLO_CAP"), 6);
            for (int i = 0; i < centros.Count; i++) Img(ws, $"colombina_{i + 1}", 4 + i * 20, 8, 0.55);

            // ── Cierre - Acciones ──
            ws = Hoja("Cierre - Acciones", "Cierre · Conclusiones y recomendaciones accionables", "Las señales del informe traducidas a acción: qué pasó, dónde, qué hacer, con dueño y fecha.");
            foreach (var (col, w) in new[] { (2, 22), (3, 46), (4, 40), (5, 20), (6, 12) }) ws.Column(col).Width = w;
            r = 4; Hdr(ws, r, 2, "Señal", "Qué pasó / Dónde", "Qué hacer", "Responsable", "Fecha");
            foreach (var a in req.Acciones ?? new())
            {
                r++;
                for (int k = 0; k < 5; k++) { var cell = ws.Cell(r, 2 + k); V(cell, k < a.Count ? a[k] : ""); cell.Style.Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top); }
                ws.Cell(r, 2).Style.Font.SetBold(); ws.Row(r).Height = 54;
            }
            ws.Cell(r + 2, 2).Value = "AM Comercial — Confidencial"; ws.Cell(r + 2, 2).Style.Font.SetFontSize(8).Font.SetFontColor(GRIS);

            // ── Ventas a Caobo ──
            var cao = d.GetProperty("caobo").EnumerateArray().ToList();
            if (cao.Count > 0)
            {
                ws = Hoja("Ventas a Caobo", $"Apéndice · Ventas a Inversiones Caobo por suplidor ({MN} {A})", N("CAOBO_INTRO"));
                foreach (var (col, w) in new[] { (2, 28), (3, 16), (4, 16), (5, 16), (6, 12) }) ws.Column(col).Width = w;
                r = 4; Hdr(ws, r, 2, "Suplidor", $"Neto {A}", $"Con ITBIS {A}", $"Neto {P}", "Δ% interan."); int k0 = r + 1;
                foreach (var x in cao)
                {
                    r++; var n = Dv(x, "neto"); var n0 = Dv(x, "netoAnterior");
                    V(ws.Cell(r, 2), Sv(x, "suplidor")); V(ws.Cell(r, 3), n, FRD); V(ws.Cell(r, 4), Dv(x, "conItbis"), FRD); V(ws.Cell(r, 5), n0, FRD);
                    V(ws.Cell(r, 6), n0 != 0 ? n / n0 - 1 : null, FPCT, fg: n >= n0 ? VERDE : ROJO);
                }
                r++; V(ws.Cell(r, 2), "TOTAL", b: true, bg: AZCLARO);
                foreach (var k in new[] { 3, 4, 5 }) V(ws.Cell(r, k), $"=SUM({ws.Cell(k0, k).Address}:{ws.Cell(r - 1, k).Address})", FRD, true, AZCLARO);
                V(ws.Cell(r, 6), $"=IF(E{r}=0,0,C{r}/E{r}-1)", FPCT, true, AZCLARO);
                Nota(ws, r + 2, N("CAOBO_CAP"), 6); Img(ws, "caobo", 4, 8, 0.6);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"AM_Cierre_{MN}{A}.xlsx");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Reunión Gerencial Excel");
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }

    private sealed record Det(string Centro, string Rep, string Grupo, string Nombre, string Cli, bool Cao, string Doc, string Sup, string Cat, string Seg, double N, double It);

    private static readonly string[] MESES_ABBR = { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
}
