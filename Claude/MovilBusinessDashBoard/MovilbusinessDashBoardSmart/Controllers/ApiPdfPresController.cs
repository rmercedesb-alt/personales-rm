using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

// ── API de "PDF Presentaciones" (Supervisión Dinámica → Informes Gerenciales) ──
// Pedido del usuario 2026-08-30. Primer informe: "Gobierno Comercial" →
// "Ventas Proyectadas próxima semana". Reusa a propósito la lógica de filtro/
// cruce Suplidor↔Categoría↔Producto y la curva de estacionalidad ya construidas
// y depuradas en ApiPresupuestosController/PresupuestosCore (mismo motor que el
// usuario aprobó reusar) en vez de reimplementarlas.
[ApiController]
[Route("apipdfpres")]
[RequiereModulo("supervision")]
public class ApiPdfPresController(IConfiguration config, ILogger<ApiPdfPresController> log) : ControllerBase
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));
    private bool IsAuth => HttpContext.Session.GetString("user_login") is not null;

    // ── Curva "general" cacheada en PresupuestosCurvas_RM (misma tabla/clave que usa
    // Presupuestos) — si Presupuestos ya la calentó para ese (año,mes), la lectura es
    // inmediata; si no, se calcula una sola vez (contra VWVentas, ~17s) y se guarda para
    // siempre, igual que en ApiPresupuestosController. Sin candado de concurrencia propio:
    // este endpoint no tiene el patrón de "varias pestañas piden lo mismo a la vez" que
    // motivó ObtenerCacheado en Presupuestos.
    private static double[] CurvaGeneralCacheada(SqlConnection cn, int anio, int mes)
    {
        var enTabla = PresupuestosCore.LeerCurvaDeTabla(cn, "general", "GENERAL", anio, mes);
        if (enTabla != null) return enTabla;
        var curva = PresupuestosCore.CurvaPorFranjas(cn, anio, mes, null, null, 0);
        PresupuestosCore.GuardarCurvaEnTabla(cn, "general", "GENERAL", anio, mes, curva);
        return curva;
    }

    // Índice de "día hábil" (sin domingos) dentro del mes, 1-based, tope 31 — misma
    // convención que PresupuestosCore.ReducirACurva (curva[wd] = fracción ACUMULADA
    // del mes lograda hasta ese día hábil inclusive).
    private static int IndiceDiaHabil(int anio, int mes, int dia)
    {
        int wd = 0;
        for (int d = 1; d <= dia; d++) if (new DateTime(anio, mes, d).DayOfWeek != DayOfWeek.Sunday) wd++;
        return Math.Min(wd, 31);
    }

    // Fracción del PRESUPUESTO MENSUAL que corresponde al tramo [diaDesde..diaHasta] de
    // ese mes, según la curva de estacionalidad (diferencia de la curva ACUMULADA entre
    // el día justo antes del tramo y el último día del tramo).
    private static double FraccionRango(double[] curva, int anio, int mes, int diaDesde, int diaHasta)
    {
        int idxHasta = IndiceDiaHabil(anio, mes, diaHasta);
        int idxAntes = diaDesde <= 1 ? 0 : IndiceDiaHabil(anio, mes, diaDesde - 1);
        double f = curva[idxHasta] - curva[idxAntes];
        return f < 0 ? 0 : f;
    }

    private sealed record Tramo(int Anio, int Mes, int DiaDesde, int DiaHasta);
    private static List<Tramo> PartirEnTramosDeMes(DateTime desde, DateTime hasta)
    {
        var tramos = new List<Tramo>();
        var cursor = desde;
        while (cursor <= hasta)
        {
            var finMes = new DateTime(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
            var finTramo = finMes < hasta ? finMes : hasta;
            tramos.Add(new Tramo(cursor.Year, cursor.Month, cursor.Day, finTramo.Day));
            cursor = finTramo.AddDays(1);
        }
        return tramos;
    }

    // ── GET /apipdfpres/proyeccion-ventas ───────────────────────────────────────
    // "Ventas Proyectadas próxima semana" (Gobierno Comercial). Proyecta, para un
    // rango de fechas FUTURO, qué parte del presupuesto YA CARGADO para ese/esos
    // mes(es) cae dentro del rango, según la curva de estacionalidad — no mezcla
    // con Ejecutado real (no aplica: son fechas que aún no ocurren).
    //
    // Fuente de datos, igual que Resumen() en ApiPresupuestosController:
    //  - Dinero y Cajas: siempre desde Producto (MNTDPROD / Unddprod), que trae
    //    Cat3ID+Cat1ID en la misma fila — permite el cruce Suplidor×Categoría×Centro
    //    exacto para el Detalle, y de paso da Centro/Canal/Supervisor para el resumen.
    //  - Facturas: NO existe a nivel de Producto (limitación de datos ya documentada
    //    en ApiPresupuestosController) — se toma de Categoría (UNDDLINFACT, filtra
    //    por Categoría; un filtro de Suplidor sin Categoría no lo acota, mismo
    //    comportamiento ya aceptado en Presupuestos) — no entra al Detalle por
    //    Suplidor/Categoría (que queda en Monto/Cajas), solo al resumen ejecutivo y
    //    a los sub-desgloses por Centro/Canal/Supervisor.
    [HttpGet("proyeccion-ventas")]
    public IActionResult ProyeccionVentas(
        [FromQuery] string desde, [FromQuery] string hasta,
        [FromQuery] string? centros, [FromQuery] string? canales,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores,
        [FromQuery] string? suplidor, [FromQuery] string? categoria)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (!DateTime.TryParse(desde, out var dDesde) || !DateTime.TryParse(hasta, out var dHasta) || dDesde.Date > dHasta.Date)
            return BadRequest(new { ok = false, error = "Rango de fechas inválido" });
        dDesde = dDesde.Date; dHasta = dHasta.Date;
        if ((dHasta - dDesde).TotalDays > 62) return BadRequest(new { ok = false, error = "El rango no puede superar 62 días" });

        try
        {
            using var cn = NewConn();
            cn.Open();
            return Ok(GenerarProyeccionVentas(cn, dDesde, dHasta, centros, canales, supervisores, vendedores, suplidor, categoria));
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Núcleo de "Ventas Proyectadas", SIN dependencia de HttpContext/sesión — factorizado
    // 2026-09-03 (mismo patrón ya usado con GenerarCierreDiario) para que también pueda
    // dispararse desde una programación guardada (ReportesProgramadosSchedulerService),
    // no solo desde el botón "Generar informe" en pantalla.
    internal static object GenerarProyeccionVentas(
        SqlConnection cn, DateTime dDesde, DateTime dHasta,
        string? centros, string? canales, string? supervisores, string? vendedores, string? suplidor, string? categoria)
    {
        {
            var reps = ApiPresupuestosController.ResolverReps(cn, centros, canales, null, supervisores, vendedores);
            var huboFiltroOrg = new[] { centros, canales, supervisores, vendedores }.Any(s => ApiPresupuestosController.ParseList(s).Count > 0);
            var vSuplidor = ApiPresupuestosController.ParseList(suplidor);
            var vCategoria = ApiPresupuestosController.ParseList(categoria);

            var tramos = PartirEnTramosDeMes(dDesde, dHasta);
            var curvasPorMes = new Dictionary<int, double[]>(); // clave: anio*100+mes
            var fraccionPorMes = new Dictionary<int, double>();
            foreach (var t in tramos)
            {
                var ym = t.Anio * 100 + t.Mes;
                if (!curvasPorMes.TryGetValue(ym, out var curva)) { curva = CurvaGeneralCacheada(cn, t.Anio, t.Mes); curvasPorMes[ym] = curva; }
                fraccionPorMes[ym] = FraccionRango(curva, t.Anio, t.Mes, t.DiaDesde, t.DiaHasta);
            }
            var ymList = tramos.Select(t => t.Anio * 100 + t.Mes).Distinct().ToList();

            if (huboFiltroOrg && reps.Count == 0)
                return new
                {
                    ok = true,
                    meta = new { desde = dDesde.ToString("yyyy-MM-dd"), hasta = dHasta.ToString("yyyy-MM-dd") },
                    resumen = new { facturas = 0.0, cajas = 0.0, monto = 0.0 },
                    porCentro = Array.Empty<object>(), porCanal = Array.Empty<object>(), porSupervisor = Array.Empty<object>(),
                    detalle = Array.Empty<object>(), porDia = Array.Empty<object>(),
                };

            var parms = new Dictionary<string, object>();
            var where = new List<string>();
            var ymNames = ymList.Select((v, i) => { var n = $"@ym{i}"; parms[n] = v; return n; }).ToList();
            where.Add($"(ph.PreAnio*100+ph.PreMes) IN ({string.Join(",", ymNames)})");
            if (reps.Count > 0) where.Add(ApiPresupuestosController.BuildIn("ph.RepCodigo", reps, "rp", parms)[4..]);
            if (vSuplidor.Count > 0) where.Add(ApiPresupuestosController.BuildIn("fpr.Cat3ID", vSuplidor, "sup", parms)[4..]);
            if (vCategoria.Count > 0) where.Add(ApiPresupuestosController.BuildIn("fpr.Cat1ID", vCategoria, "cat", parms)[4..]);

            // ── Dinero + Cajas, vía Producto (da Cat3/Cat1 + Centro/Canal/Supervisor a la vez) ──
            var sqlProd = $@"
SELECT ph.PreAnio, ph.PreMes, ph.PreTipo,
       ISNULL(r.CedCodigo,'') CedCodigo, ISNULL(cd.CedDescripcion, ISNULL(r.CedCodigo,'(Sin centro)')) CentroNombre,
       ISNULL(ra.CanID,0) CanID, ISNULL(ca.CanDescripcion,'(Sin canal)') CanNombre,
       ISNULL(r.RepSupervisor,'') SupCod, ISNULL(sup.RepNombre, r.RepSupervisor) SupNombre,
       fpr.Cat3ID, ISNULL(c3.Cat3Descripcion,'(Sin suplidor)') Cat3Nombre,
       fpr.Cat1ID, ISNULL(c1.Cat1Descripcion,'(Sin categoría)') Cat1Nombre,
       SUM(CASE WHEN ph.PreTipo='Unddprod' THEN ph.PrePresupuesto/NULLIF(fpr.ProUnidades,0) ELSE ph.PrePresupuesto END) Presu
FROM PresupuestosHistoricos ph WITH (NOLOCK)
CROSS APPLY (SELECT TRY_CAST(CASE WHEN LEFT(ph.PreReferencia,1)='V' THEN SUBSTRING(ph.PreReferencia,2,20) ELSE ph.PreReferencia END AS INT) AS ProdId) fpx
INNER JOIN Productos fpr WITH (NOLOCK) ON fpr.ProID=fpx.ProdId
LEFT JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID=fpr.Cat3ID
LEFT JOIN Categoria1 c1 WITH (NOLOCK) ON c1.Cat1ID=fpr.Cat1ID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=ph.RepCodigo
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
LEFT JOIN CanalDistribucion ca WITH (NOLOCK) ON ca.CanID=ra.CanID
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
WHERE ph.PreTipo IN ('MNTDPROD','Unddprod') AND {string.Join(" AND ", where)}
GROUP BY ph.PreAnio, ph.PreMes, ph.PreTipo, r.CedCodigo, cd.CedDescripcion, ra.CanID, ca.CanDescripcion,
         r.RepSupervisor, sup.RepNombre, fpr.Cat3ID, c3.Cat3Descripcion, fpr.Cat1ID, c1.Cat1Descripcion";

            var filasProd = new List<(int anio, int mes, string tipo, string cedCodigo, string centro, int canId, string canal,
                string supCod, string supNombre, int cat3Id, string cat3Nombre, int cat1Id, string cat1Nombre, double presu)>();
            using (var cmd = new SqlCommand(sqlProd, cn) { CommandTimeout = 30 })
            {
                foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    filasProd.Add((
                        rd.GetInt16(0), rd.GetInt16(1), rd.GetString(2),
                        rd.GetString(3), rd.GetString(4), rd.GetInt32(5), rd.GetString(6),
                        rd.GetString(7), rd.IsDBNull(8) ? "" : rd.GetString(8),
                        rd.IsDBNull(9) ? 0 : rd.GetInt32(9), rd.GetString(10),
                        rd.IsDBNull(11) ? 0 : rd.GetInt32(11), rd.GetString(12),
                        rd.IsDBNull(13) ? 0 : Convert.ToDouble(rd.GetValue(13))));
            }

            // ── Facturas, vía Categoría (UNDDLINFACT) — sin Cat3/Producto disponible ──
            var whereFact = new List<string>(where);
            var parmsFact = new Dictionary<string, object>(parms);
            // El filtro por Cat3ID no aplica aquí (no existe esa dimensión en esta fuente);
            // se descarta ese fragmento del WHERE si estaba presente y, en su lugar, se
            // filtra por Categoría directo sobre PreReferencia (igual que BuildRefFiltro "cat1").
            whereFact.RemoveAll(w => w.Contains("fpr.Cat3ID") || w.Contains("fpr.Cat1ID"));
            var refFiltroCat1 = ApiPresupuestosController.BuildRefFiltro("cat1", vSuplidor, vCategoria, new List<string>(), parmsFact, "ph");
            var sqlFact = $@"
SELECT ph.PreAnio, ph.PreMes,
       ISNULL(r.CedCodigo,'') CedCodigo, ISNULL(cd.CedDescripcion, ISNULL(r.CedCodigo,'(Sin centro)')) CentroNombre,
       ISNULL(ra.CanID,0) CanID, ISNULL(ca.CanDescripcion,'(Sin canal)') CanNombre,
       ISNULL(r.RepSupervisor,'') SupCod, ISNULL(sup.RepNombre, r.RepSupervisor) SupNombre,
       SUM(ph.PrePresupuesto) Presu
FROM PresupuestosHistoricos ph WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=ph.RepCodigo
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
LEFT JOIN CanalDistribucion ca WITH (NOLOCK) ON ca.CanID=ra.CanID
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
WHERE ph.PreTipo='UNDDLINFACT' {refFiltroCat1} AND {string.Join(" AND ", whereFact)}
GROUP BY ph.PreAnio, ph.PreMes, r.CedCodigo, cd.CedDescripcion, ra.CanID, ca.CanDescripcion, r.RepSupervisor, sup.RepNombre";

            var filasFact = new List<(int anio, int mes, string cedCodigo, string centro, int canId, string canal, string supCod, string supNombre, double presu)>();
            using (var cmd = new SqlCommand(sqlFact, cn) { CommandTimeout = 30 })
            {
                foreach (var kv in parmsFact) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    filasFact.Add((
                        rd.GetInt16(0), rd.GetInt16(1), rd.GetString(2), rd.GetString(3),
                        rd.GetInt32(4), rd.GetString(5), rd.GetString(6), rd.IsDBNull(7) ? "" : rd.GetString(7),
                        rd.IsDBNull(8) ? 0 : Convert.ToDouble(rd.GetValue(8))));
            }

            double Fraccion(int anio, int mes) => fraccionPorMes.TryGetValue(anio * 100 + mes, out var f) ? f : 0;

            // ── Ventas por Día — Cajas y Monto a vender cada día del rango, pedido explícito
            // del usuario 2026-08-30. Reusa el mismo total mensual filtrado (filasProd, ANTES
            // de aplicar la fracción del rango completo) y lo reparte día por día con la
            // fracción de UN SOLO día (misma curva, misma diferencia acumulada que arriba pero
            // entre un día y el mismo día — telescópica: la suma de estos días da exacto el
            // mismo total que "resumen" para el rango completo).
            var totalMesCrudo = new Dictionary<int, (double monto, double cajas)>(); // clave: anio*100+mes
            foreach (var f in filasProd)
            {
                int ym = f.anio * 100 + f.mes;
                if (!totalMesCrudo.TryGetValue(ym, out var t)) t = (0, 0);
                bool esCajasRaw = f.tipo == "Unddprod";
                totalMesCrudo[ym] = (t.monto + (esCajasRaw ? 0 : f.presu), t.cajas + (esCajasRaw ? f.presu : 0));
            }
            string[] nombresDia = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };
            var porDia = new List<object>();
            for (var d = dDesde; d <= dHasta; d = d.AddDays(1))
            {
                int ym = d.Year * 100 + d.Month;
                var curva = curvasPorMes[ym];
                double fDia = FraccionRango(curva, d.Year, d.Month, d.Day, d.Day);
                var totMes = totalMesCrudo.TryGetValue(ym, out var tm) ? tm : (0, 0);
                porDia.Add(new
                {
                    fecha = d.ToString("yyyy-MM-dd"),
                    diaSemana = nombresDia[(int)d.DayOfWeek],
                    cajas = totMes.cajas * fDia,
                    monto = totMes.monto * fDia,
                });
            }

            double monto = 0, cajas = 0, facturas = 0;
            var porCentro = new Dictionary<string, (string nombre, double monto, double cajas, double facturas)>();
            var porCanal = new Dictionary<int, (string nombre, double monto, double cajas, double facturas)>();
            var porSupervisor = new Dictionary<string, (string nombre, double monto, double cajas, double facturas)>();
            var detalle = new Dictionary<(int cat3, int cat1), (string suplidor, string categoria, double monto, double cajas, Dictionary<string, double> montoPorCentro, Dictionary<string, double> cajasPorCentro)>();
            var centrosVistos = new Dictionary<string, string>(); // cedCodigo -> nombre

            foreach (var f in filasProd)
            {
                double valor = f.presu * Fraccion(f.anio, f.mes);
                bool esCajas = f.tipo == "Unddprod";
                if (esCajas) cajas += valor; else monto += valor;

                centrosVistos[f.cedCodigo] = f.centro;

                if (!porCentro.TryGetValue(f.cedCodigo, out var pc)) pc = (f.centro, 0, 0, 0);
                pc = (f.centro, pc.monto + (esCajas ? 0 : valor), pc.cajas + (esCajas ? valor : 0), pc.facturas);
                porCentro[f.cedCodigo] = pc;

                if (!porCanal.TryGetValue(f.canId, out var pca)) pca = (f.canal, 0, 0, 0);
                pca = (f.canal, pca.monto + (esCajas ? 0 : valor), pca.cajas + (esCajas ? valor : 0), pca.facturas);
                porCanal[f.canId] = pca;

                if (!porSupervisor.TryGetValue(f.supCod, out var ps)) ps = (f.supNombre, 0, 0, 0);
                ps = (f.supNombre, ps.monto + (esCajas ? 0 : valor), ps.cajas + (esCajas ? valor : 0), ps.facturas);
                porSupervisor[f.supCod] = ps;

                var keyDet = (f.cat3Id, f.cat1Id);
                if (!detalle.TryGetValue(keyDet, out var d))
                    d = (f.cat3Nombre, f.cat1Nombre, 0, 0, new Dictionary<string, double>(), new Dictionary<string, double>());
                double dMonto = d.monto + (esCajas ? 0 : valor), dCajas = d.cajas + (esCajas ? valor : 0);
                if (esCajas) d.cajasPorCentro[f.cedCodigo] = d.cajasPorCentro.GetValueOrDefault(f.cedCodigo) + valor;
                else d.montoPorCentro[f.cedCodigo] = d.montoPorCentro.GetValueOrDefault(f.cedCodigo) + valor;
                detalle[keyDet] = (d.suplidor, d.categoria, dMonto, dCajas, d.montoPorCentro, d.cajasPorCentro);
            }

            foreach (var f in filasFact)
            {
                double valor = f.presu * Fraccion(f.anio, f.mes);
                facturas += valor;
                centrosVistos[f.cedCodigo] = f.centro;

                if (!porCentro.TryGetValue(f.cedCodigo, out var pc)) pc = (f.centro, 0, 0, 0);
                porCentro[f.cedCodigo] = (f.centro, pc.monto, pc.cajas, pc.facturas + valor);

                if (!porCanal.TryGetValue(f.canId, out var pca)) pca = (f.canal, 0, 0, 0);
                porCanal[f.canId] = (f.canal, pca.monto, pca.cajas, pca.facturas + valor);

                if (!porSupervisor.TryGetValue(f.supCod, out var ps)) ps = (f.supNombre, 0, 0, 0);
                porSupervisor[f.supCod] = (f.supNombre, ps.monto, ps.cajas, ps.facturas + valor);
            }

            var centrosOrdenados = centrosVistos.OrderBy(kv => kv.Value).ToList();

            var resultado = new
            {
                ok = true,
                meta = new { desde = dDesde.ToString("yyyy-MM-dd"), hasta = dHasta.ToString("yyyy-MM-dd") },
                resumen = new { facturas, cajas, monto },
                centros = centrosOrdenados.Select(c => new { cedCodigo = c.Key, nombre = c.Value }),
                porCentro = porCentro.OrderByDescending(kv => kv.Value.monto)
                    .Select(kv => new { cedCodigo = kv.Key, nombre = kv.Value.nombre, facturas = kv.Value.facturas, cajas = kv.Value.cajas, monto = kv.Value.monto }),
                porCanal = porCanal.OrderByDescending(kv => kv.Value.monto)
                    .Select(kv => new { canId = kv.Key, nombre = kv.Value.nombre, facturas = kv.Value.facturas, cajas = kv.Value.cajas, monto = kv.Value.monto }),
                porSupervisor = porSupervisor.OrderByDescending(kv => kv.Value.monto)
                    .Select(kv => new { supCod = kv.Key, nombre = kv.Value.nombre, facturas = kv.Value.facturas, cajas = kv.Value.cajas, monto = kv.Value.monto }),
                detalle = detalle.OrderByDescending(kv => kv.Value.monto)
                    .Select(kv => new
                    {
                        cat3Id = kv.Key.cat3,
                        suplidor = kv.Value.suplidor,
                        cat1Id = kv.Key.cat1,
                        categoria = kv.Value.categoria,
                        monto = kv.Value.monto,
                        cajas = kv.Value.cajas,
                        porCentro = centrosOrdenados.ToDictionary(
                            c => c.Key,
                            c => new { monto = kv.Value.montoPorCentro.GetValueOrDefault(c.Key), cajas = kv.Value.cajasPorCentro.GetValueOrDefault(c.Key) }),
                    }),
                porDia,
                vsPresupuesto = GenerarVentasVsPresupuesto(cn, DateTime.Today, centros, canales, supervisores, vendedores, suplidor, categoria),
            };
            return resultado;
        }
    }

    // ── Capítulo "Ventas vs. Presupuesto al día y proyectado" (2026-10-05) ──────────
    // Va ANTES de "Ventas Proyectadas próxima semana" en el mismo informe. Mes de
    // referencia = el mes de HOY. Misma fuente y mismos criterios que "Resultados
    // acumulados del Mes" del Cierre Diario: PreTipo mensual MNTDCAT (Presupuesto/
    // Ejecutado tal cual los mantiene el proceso externo), días HÁBILES de
    // VWPresupuestosAlDiaUnidadesProductos, Suplidor derivado del mapa Cat1→Cat3 dominante,
    // canal Institucional excluido salvo filtro explícito.
    //  - Presupuesto al día = Presupuesto del mes × (días hábiles transcurridos / días hábiles del mes)
    //  - Proyectado al cierre = Ejecutado / días hábiles transcurridos × días hábiles del mes
    //    (proyección "a ritmo actual"); se compara contra el Presupuesto del mes.
    // Devuelve null si algo falla: el informe de Ventas Proyectadas NO debe romperse por este capítulo.
    private sealed record Cub(double Presu, double AlDia, double Ejec, double Proy, double? Est, int Rest);
    // Curva de estacionalidad por CANAL, igual que Presupuestos (ObtenerCurva con 1 canal): 1 categoría exacta → curva de esa categoría;
    // si no, la general. Se lee de PresupuestosCurvas_RM (la misma que ya calienta la pantalla de Presupuestos) y, si falta, se calcula
    // (VWVentas, lento la 1.ª vez) y se persiste. Caché en memoria 6 h para no releer en cada informe.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime t, double[]? c)> _curvaRep = new();
    private static double[]? CurvaReporte(SqlConnection cn, List<string> vCategoria, int canId, int anio, int mes)
    {
        bool uno = vCategoria.Count == 1; string tipo = uno ? "cat1" : "general", clave = uno ? vCategoria[0] : "GENERAL";
        var key = $"{tipo}:{clave}:{canId}:{anio}{mes:00}";
        if (_curvaRep.TryGetValue(key, out var e) && (DateTime.Now - e.t).TotalHours < 6) return e.c;
        double[]? c = PresupuestosCore.LeerCurvaDeTabla(cn, tipo, clave, anio, mes, canId);
        if (c == null)
        {
            c = PresupuestosCore.CurvaPorFranjas(cn, anio, mes, uno ? clave : null, null, canId);
            if (c != null) PresupuestosCore.GuardarCurvaEnTabla(cn, tipo, clave, anio, mes, c, canId);
        }
        _curvaRep[key] = (DateTime.Now, c); return c;
    }
    private static object? GenerarVentasVsPresupuesto(
        SqlConnection cn, DateTime hoy, string? centros, string? canales, string? supervisores, string? vendedores,
        string? suplidor, string? categoria)
    {
        try
        {
            int anio = hoy.Year, mes = hoy.Month;
            var vSuplidor = ApiPresupuestosController.ParseList(suplidor);
            var vCategoria = ApiPresupuestosController.ParseList(categoria);
            var vCentros = ApiPresupuestosController.ParseList(centros);
            var vCanales = ApiPresupuestosController.ParseList(canales);
            var vSupervisores = ApiPresupuestosController.ParseList(supervisores);
            var vVendedores = ApiPresupuestosController.ParseList(vendedores);

            var mapa = PresupuestosCore.MapaCat1ACat3Dia(cn, anio, mes);
            List<string> vCat1Efectivo = vCategoria;
            if (vCategoria.Count == 0 && vSuplidor.Count > 0)
            {
                var supSet = vSuplidor.Select(s => int.TryParse(s, out var n) ? n : -1).ToHashSet();
                vCat1Efectivo = mapa.Where(kv => supSet.Contains(kv.Value)).Select(kv => kv.Key.ToString()).ToList();
                if (vCat1Efectivo.Count == 0) vCat1Efectivo.Add("-1");
            }
            var parms = new Dictionary<string, object>();
            var dimFiltro = ApiPresupuestosController.BuildRefFiltro("cat1", new List<string>(), vCat1Efectivo, new List<string>(), parms, "ph");
            var filas = ConsultarPresupuestoMes(cn, "MNTDCAT", anio, mes, vCentros, vCanales, new List<string>(), vSupervisores, vVendedores, dimFiltro, parms);
            // Igual que Presupuestos (ResolverReps): solo vendedores ACTIVOS y que no sean supervisores — así las cifras cuadran con esa pantalla.
            var activos = new HashSet<string>();
            using (var ca = new SqlCommand("SELECT RepCodigo FROM Representantes WITH (NOLOCK) WHERE RepEstatus=1 AND ISNULL(RepCargo,'')<>'Supervisor'", cn) { CommandTimeout = 30 })
            using (var rr = ca.ExecuteReader()) while (rr.Read()) activos.Add(rr.GetString(0));
            filas = filas.Where(x => activos.Contains(x.RepCodigo)).ToList();

            // ── Misma metodología que Presupuestos → Detalle (drill/organizacion) — pedido del usuario 2026-10-12: ──
            // cada VENDEDOR se calcula con PresupuestosCore.Calcular (días hábiles de PresupuestosCore, avance según la última
            // actualización de SUS datos con 1 día de rezago, Proyectado lineal, Estimado con la curva de estacionalidad de SU
            // canal) y luego se SUMA por Centro / Canal / Supervisor / Suplidor; los % y la Necesidad Diaria se recalculan sobre
            // el total ya agregado de cada fila. Por Suplidor la cubeta es vendedor×suplidor.
            int diasDelMes = PresupuestosCore.DiasHabiles(anio, mes, DateTime.DaysInMonth(anio, mes));
            var cat3Nombres = ObtenerNombresCat3(cn);
            string ClaveSup(int? cat1) => cat1.HasValue && mapa.TryGetValue(cat1.Value, out var c3) ? c3.ToString() : "0";
            string NombreSup(int? cat1) => cat1.HasValue && mapa.TryGetValue(cat1.Value, out var c3) ? (cat3Nombres.TryGetValue(c3, out var n) ? n : ("Suplidor " + c3)) : "(Sin suplidor)";
            var curvas = new Dictionary<int, double[]?>();
            double[]? CurvaCanal(int canId) { if (!curvas.TryGetValue(canId, out var c)) curvas[canId] = c = CurvaReporte(cn, vCategoria, canId, anio, mes); return c; }

            Cub Cubeta(double presu, double ejec, DateTime? ultima, int canId)
            {
                int diasT = PresupuestosCore.DiasTranscurridos(ultima, anio, mes);
                var r = PresupuestosCore.Calcular(presu, ejec, diasT, diasDelMes, CurvaCanal(canId), ultima);
                int diasTAj = PresupuestosCore.DiasTranscurridosAjustado(diasT, diasDelMes);
                PresupuestosCore.AjustarProyectadoPorCanal(r, new[] { (presu, ejec, diasTAj) }, diasDelMes);
                return new Cub(presu, r.PresupuestoAlDia, ejec, r.VentasProyectadas ?? 0, r.VentasEstimadas, r.DiasRestantes);
            }
            object FilaV2(string clave, string nombre, Cub a)
            {
                double? alcance = a.Presu != 0 ? 100.0 * a.Ejec / a.Presu : null, proyPct = a.Presu != 0 ? 100.0 * a.Proy / a.Presu : null;
                double? estPct = a.Est != null && a.Presu != 0 ? 100.0 * a.Est.Value / a.Presu : null;
                return new
                {
                    clave, nombre, presupuesto = a.Presu, presupuestoAlDia = a.AlDia, ejecutado = a.Ejec,
                    alcancePct = alcance, deficitMomento = a.AlDia - a.Ejec,
                    proyectado = a.Proy, proyectadoPct = proyPct, estimado = a.Est, estimadoPct = estPct,
                    deficitEstimado = a.Est != null ? a.Presu - a.Est.Value : (double?)null,
                    necesidadDiaria = a.Rest > 0 ? (a.Presu - a.Ejec) / a.Rest : (double?)null,
                    // compatibilidad con informes/correos que ya leen estos nombres
                    logroAlDiaPct = a.AlDia != 0 ? 100.0 * a.Ejec / a.AlDia : (double?)null, diferencia = a.Ejec - a.AlDia, diferenciaProy = a.Proy - a.Presu,
                };
            }
            Cub Sumar(IEnumerable<Cub> xs)
            {
                double p = 0, d = 0, e = 0, y = 0, es = 0; bool tiene = false; int rest = 0;
                foreach (var c in xs) { p += c.Presu; d += c.AlDia; e += c.Ejec; y += c.Proy; if (c.Est != null) { es += c.Est.Value; tiene = true; } if (rest == 0) rest = c.Rest; }
                return new Cub(p, d, e, y, tiene ? es : (double?)null, rest);
            }
            List<object> Resumir(IEnumerable<(string k, string n, Cub c)> items)
                => items.GroupBy(i => i.k).Select(g => (k: g.Key, n: g.First().n, a: Sumar(g.Select(i => i.c))))
                    .OrderByDescending(x => x.a.Presu).Select(x => FilaV2(x.k, x.n, x.a)).ToList();

            // cubeta por vendedor (todas sus categorías juntas) — para Centro/Canal/Supervisor y el total
            var repCub = filas.GroupBy(x => x.RepCodigo)
                .Select(g => { var f0 = g.First(); return (f: f0, c: Cubeta(g.Sum(x => x.Presu), g.Sum(x => x.Ejec), g.Max(x => x.Ultima), f0.CanID)); }).ToList();
            // cubeta por vendedor×suplidor — para Por Suplidor
            var suplCub = filas.GroupBy(x => (s: ClaveSup(x.DimId), r: x.RepCodigo))
                .Select(g => { var f0 = g.First(); return (k: g.Key.s, n: NombreSup(f0.DimId), c: Cubeta(g.Sum(x => x.Presu), g.Sum(x => x.Ejec), g.Max(x => x.Ultima), f0.CanID)); }).ToList();

            var ultimaGlobal = filas.Where(x => x.Ultima != null).Select(x => x.Ultima).DefaultIfEmpty(null).Max();
            int diasTranscurridos = PresupuestosCore.DiasTranscurridos(ultimaGlobal, anio, mes);
            int diasRestantes = Math.Max(0, diasDelMes - PresupuestosCore.DiasTranscurridosAjustado(diasTranscurridos, diasDelMes));
            return new
            {
                anio, mes, diasDelMes, diasTranscurridos, diasRestantes, formato = 2,
                total = FilaV2("", "Total", Sumar(repCub.Select(x => x.c))),
                porCentro = Resumir(repCub.Select(x => (x.f.CedCodigo, x.f.CentroNombre, x.c))),
                porCanal = Resumir(repCub.Select(x => (x.f.CanID.ToString(), x.f.CanalNombre, x.c))),
                porSupervisor = Resumir(repCub.Select(x => (x.f.SupCod, string.IsNullOrWhiteSpace(x.f.SupNombre) ? "(Sin supervisor)" : x.f.SupNombre, x.c))),
                porSuplidor = Resumir(suplCub.Select(x => (x.k, x.n, x.c))),
            };
        }
        catch { return null; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ── "Cierre Diario" (Supervisión Dinámica → PDF Presentaciones → Cierres) ──
    // Pedido explícito del usuario 2026-09-03. Reusa el motor de Presupuestos
    // (ApiPresupuestosController.ParseList/BuildIn/ResolverReps/BuildRefFiltroDiario,
    // internal static desde 2026-08-30/2026-09-03; PresupuestosCore.Mapa/MapaCat1ACat3Dia)
    // en vez de reimplementar el cruce Suplidor↔Categoría↔Producto ya depurado ahí.
    //
    // Fuente de "Ejecutado"/"Presupuesto": SIEMPRE PresupuestosHistoricos.PreEjecutado/
    // PrePresupuesto tal cual (mantenidos por proceso externo) — igual que en TODO el
    // resto del sistema (Cobros del día, Torre, Presupuestos). No se recalcula desde
    // VWPedidos aquí (VWPedidos sí se usa, más abajo, para los calendarios de 30 días
    // y el resumen ejecutivo — son series propias, sin campo "Ejecutado" que leer).
    //
    // Dos PreTipo distintos según la métrica (ver PresupuestosCore.Mapa):
    //   - Ventas (dinero):      MNTDCAT2DIA ("Meta lineal") — nativo diario, clave=Cat1ID
    //     (Categoría). Primario desde 2026-09-24 (antes MNTDCAT1DIA/"Meta ajustada", que
    //     ahora es la columna de referencia secundaria — ver AgruparAjustada).
    //     Suplidor se DERIVA (sin fila nativa) traduciendo el filtro de Suplidor a sus
    //     Cat1ID "dominantes" (PresupuestosCore.MapaCat1ACat3Dia) — mismo mecanismo que
    //     ApiPresupuestosController.ResolverCfgDiario para "suplidor,dinero".
    //   - Activaciones:         UNDDACTCAT3DIA — nativo diario, clave=Cat3ID (Suplidor)
    //     DIRECTO (sin derivar) para Centro/Canal/Supervisor/Vendedor/Suplidor. El
    //     desglose por CATEGORÍA de Activaciones usa una consulta SEPARADA contra
    //     UNDDLINACTDIA1 (Cat1-keyed) — el total por Categoría puede no cuadrar exacto
    //     con el total por Suplidor (2 PreTipo con distinta granularidad nativa; mismo
    //     tipo de limitación ya documentada en este archivo para UNDDLINFACT). Se avisa
    //     en el reporte, no se fuerza una reconciliación falsa.
    private sealed record FilaPresDia(
        string RepCodigo, string RepNombre, string CedCodigo, string CentroNombre,
        int CanID, string CanalNombre, string SupCod, string SupNombre, string Division,
        int? DimId, double Presu, double Ejec, DateTime? Ultima = null);

    // Días HÁBILES (lunes a sábado) del mes/transcurridos — pedido explícito 2026-09-20:
    // NO calcularlos nosotros (antes se usaba día de calendario, "no me sirve"), sino
    // tomarlos de VWPresupuestosAlDiaUnidadesProductos (columnas diasdelmes/
    // diasTranscurridos), la fuente que ya usa el negocio para esto. Verificado en vivo:
    // el valor es UNIFORME para todas las filas de un mismo PreAnio/PreMes (ej. sep-2026
    // = 26/15 en 8,855 filas), así que basta con TOP 1. Si el mes no tiene filas ahí
    // todavía (p.ej. mes futuro sin presupuesto cargado), fallback al cálculo ya usado en
    // el resto del sistema (PresupuestosCore.DiasHabiles) para no dejar el reporte roto.
    private static (int diasDelMes, int diasTranscurridos, int diasRestantes) ObtenerDiasHabilesMes(SqlConnection cn, int anio, int mes)
    {
        using (var cmd = new SqlCommand("SELECT TOP 1 diasdelmes, diasTranscurridos FROM VWPresupuestosAlDiaUnidadesProductos WHERE PreAnio=@a AND PreMes=@m", cn) { CommandTimeout = 30 })
        {
            cmd.Parameters.AddWithValue("@a", anio);
            cmd.Parameters.AddWithValue("@m", mes);
            using var rd = cmd.ExecuteReader();
            if (rd.Read())
            {
                int ddm = rd.GetInt32(0), dt = rd.GetInt32(1);
                return (ddm, dt, Math.Max(0, ddm - dt));
            }
        }
        int diasDelMesFallback = PresupuestosCore.DiasHabiles(anio, mes, DateTime.DaysInMonth(anio, mes));
        var hoy = DateTime.Today;
        int diasTranscurridosFallback = (hoy.Year == anio && hoy.Month == mes) ? PresupuestosCore.DiasHabiles(anio, mes, hoy.Day) : diasDelMesFallback;
        return (diasDelMesFallback, diasTranscurridosFallback, Math.Max(0, diasDelMesFallback - diasTranscurridosFallback));
    }

    // Consulta base: UN día, UN PreTipo diario (MNTDCAT1DIA | UNDDACTCAT3DIA | UNDDLINACTDIA1),
    // con el join organizacional completo (Centro/Canal/Supervisor/División) + el Cat1/Cat3
    // extraído de PreReferencia. dimFiltro ya viene armado (BuildRefFiltroDiario o filtro
    // directo por Cat3ID) — este método no decide la dimensión, solo la aplica.
    private static List<FilaPresDia> ConsultarPresupuestoDia(
        SqlConnection cn, string preTipo, int anio, int mes, int dia,
        List<string> vCentros, List<string> vCanales, List<string> vDivisiones,
        List<string> vSupervisores, List<string> vVendedores, string dimFiltro, Dictionary<string, object> parmsDim,
        int? diaHasta = null)
    {
        // diaHasta (opcional, 2026-10-06): si viene, agrega TODOS los días [dia..diaHasta] del mes en una sola
        // consulta (histórico de comportamiento del "Resumen Cierre del Día"); sin él, un solo día como siempre.
        var parms = new Dictionary<string, object>(parmsDim) { ["@anio"] = anio, ["@mes"] = mes, ["@dia"] = dia, ["@diaHasta"] = diaHasta ?? dia, ["@tipo"] = preTipo };
        var f1 = ApiPresupuestosController.BuildIn("r.CedCodigo", vCentros, "ce", parms);
        var f3 = ApiPresupuestosController.BuildIn("r.RepSupervisor", vSupervisores, "su", parms);
        var f4 = ApiPresupuestosController.BuildIn("ra.RepTipe", vDivisiones, "di", parms);
        var f5 = ApiPresupuestosController.BuildIn("ph.RepCodigo", vVendedores, "ve", parms);
        // Canal: si el usuario filtró explícito, se respeta tal cual (incluso Institucional
        // si lo pide a propósito); si NO filtró, se excluye Institucional (7000) por
        // defecto — pedido explícito del usuario ("todo el negocio, menos el canal
        // institucional"), misma constante ya usada en Presupuestos/Históricos/
        // PresupuestoGestión (PresupuestoBuilderCore.CanalInstitucional).
        var f2 = vCanales.Count > 0
            ? ApiPresupuestosController.BuildIn("ra.CanID", vCanales, "ca", parms)
            : $" AND ISNULL(ra.CanID,0)<>{PresupuestoBuilderCore.CanalInstitucional}";

        var sql = $@"
SELECT ph.RepCodigo, r.RepNombre, ISNULL(r.CedCodigo,'') CedCodigo, ISNULL(cd.CedDescripcion,ISNULL(r.CedCodigo,'(Sin centro)')) CentroNombre,
       ISNULL(ra.CanID,0) CanID, ISNULL(can.CanDescripcion,'(Sin canal)') CanalNombre,
       ISNULL(r.RepSupervisor,'') SupCod, ISNULL(sup.RepNombre,r.RepSupervisor) SupNombre, ISNULL(ra.RepTipe,'') Division,
       TRY_CAST(RIGHT(ph.PreReferencia, LEN(ph.PreReferencia)-CHARINDEX('-',ph.PreReferencia)) AS INT) DimId,
       SUM(ph.PrePresupuesto) Presu, SUM(ph.PreEjecutado) Ejec
FROM PresupuestosHistoricos ph WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=ph.RepCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN CanalDistribucion can WITH (NOLOCK) ON can.CanID=ra.CanID
WHERE ph.PreTipo=@tipo AND ph.PreAnio=@anio AND ph.PreMes=@mes
  AND CHARINDEX('-',ph.PreReferencia) > 0
  AND TRY_CAST(LEFT(ph.PreReferencia, CHARINDEX('-',ph.PreReferencia)-1) AS INT) BETWEEN @dia AND @diaHasta
{f1}{f2}{f3}{f4}{f5}{dimFiltro}
GROUP BY ph.RepCodigo, r.RepNombre, r.CedCodigo, cd.CedDescripcion, ra.CanID, can.CanDescripcion, r.RepSupervisor, sup.RepNombre, ra.RepTipe,
         TRY_CAST(RIGHT(ph.PreReferencia, LEN(ph.PreReferencia)-CHARINDEX('-',ph.PreReferencia)) AS INT)";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        var filas = new List<FilaPresDia>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            filas.Add(new FilaPresDia(
                rd.GetString(0), rd.IsDBNull(1) ? "" : rd.GetString(1), rd.GetString(2), rd.GetString(3),
                rd.GetInt32(4), rd.GetString(5), rd.GetString(6), rd.IsDBNull(7) ? "" : rd.GetString(7), rd.GetString(8),
                rd.IsDBNull(9) ? (int?)null : rd.GetInt32(9),
                rd.IsDBNull(10) ? 0 : Convert.ToDouble(rd.GetValue(10)), rd.IsDBNull(11) ? 0 : Convert.ToDouble(rd.GetValue(11))));
        return filas;
    }

    // Igual que ConsultarPresupuestoDia pero para el PreTipo MENSUAL de la misma familia
    // (MNTDCAT/UNDDACTCAT3/UNDDLINACT) — 2026-09-19, pedido explícito del usuario para el
    // nuevo punto "1. Resultados acumulados del Mes". Sin filtro de día (ni CHARINDEX): las
    // filas mensuales traen PreReferencia como el ID directo (Cat1ID/Cat3ID sin el prefijo
    // "DD-" de las diarias — verificado: TRY_CAST(PreReferencia AS INT) ya se usa así en
    // ApiPresupuestosController.cs:190/197). Por eso el filtro de dimensión usa
    // ApiPresupuestosController.BuildRefFiltro (el genérico), NO BuildRefFiltroDiario.
    private static List<FilaPresDia> ConsultarPresupuestoMes(
        SqlConnection cn, string preTipo, int anio, int mes,
        List<string> vCentros, List<string> vCanales, List<string> vDivisiones,
        List<string> vSupervisores, List<string> vVendedores, string dimFiltro, Dictionary<string, object> parmsDim)
    {
        var parms = new Dictionary<string, object>(parmsDim) { ["@anio"] = anio, ["@mes"] = mes, ["@tipo"] = preTipo };
        var f1 = ApiPresupuestosController.BuildIn("r.CedCodigo", vCentros, "mce", parms);
        var f3 = ApiPresupuestosController.BuildIn("r.RepSupervisor", vSupervisores, "msu", parms);
        var f4 = ApiPresupuestosController.BuildIn("ra.RepTipe", vDivisiones, "mdi", parms);
        var f5 = ApiPresupuestosController.BuildIn("ph.RepCodigo", vVendedores, "mve", parms);
        var f2 = vCanales.Count > 0
            ? ApiPresupuestosController.BuildIn("ra.CanID", vCanales, "mca", parms)
            : $" AND ISNULL(ra.CanID,0)<>{PresupuestoBuilderCore.CanalInstitucional}";

        var sql = $@"
SELECT ph.RepCodigo, r.RepNombre, ISNULL(r.CedCodigo,'') CedCodigo, ISNULL(cd.CedDescripcion,ISNULL(r.CedCodigo,'(Sin centro)')) CentroNombre,
       ISNULL(ra.CanID,0) CanID, ISNULL(can.CanDescripcion,'(Sin canal)') CanalNombre,
       ISNULL(r.RepSupervisor,'') SupCod, ISNULL(sup.RepNombre,r.RepSupervisor) SupNombre, ISNULL(ra.RepTipe,'') Division,
       TRY_CAST(ph.PreReferencia AS INT) DimId,
       SUM(ph.PrePresupuesto) Presu, SUM(ph.PreEjecutado) Ejec, MAX(ph.PreFechaActualizacion) Ultima
FROM PresupuestosHistoricos ph WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=ph.RepCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN CanalDistribucion can WITH (NOLOCK) ON can.CanID=ra.CanID
WHERE ph.PreTipo=@tipo AND ph.PreAnio=@anio AND ph.PreMes=@mes
{f1}{f2}{f3}{f4}{f5}{dimFiltro}
GROUP BY ph.RepCodigo, r.RepNombre, r.CedCodigo, cd.CedDescripcion, ra.CanID, can.CanDescripcion, r.RepSupervisor, sup.RepNombre, ra.RepTipe,
         TRY_CAST(ph.PreReferencia AS INT)";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        var filas = new List<FilaPresDia>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            filas.Add(new FilaPresDia(
                rd.GetString(0), rd.IsDBNull(1) ? "" : rd.GetString(1), rd.GetString(2), rd.GetString(3),
                rd.GetInt32(4), rd.GetString(5), rd.GetString(6), rd.IsDBNull(7) ? "" : rd.GetString(7), rd.GetString(8),
                rd.IsDBNull(9) ? (int?)null : rd.GetInt32(9),
                rd.IsDBNull(10) ? 0 : Convert.ToDouble(rd.GetValue(10)), rd.IsDBNull(11) ? 0 : Convert.ToDouble(rd.GetValue(11)), rd.IsDBNull(12) ? (DateTime?)null : rd.GetDateTime(12)));
        return filas;
    }

    // Par (Presupuesto,Ejecutado) de una variante secundaria de referencia para una clave de
    // agrupación — null cuando esa clave no tiene fila ese día (no se inventa un 0
    // falso: se muestra "—" en el front, ver cdTablaAlcance/cdTablaSupervisor).
    private static Dictionary<string, (double presu, double ejec)> AgruparAjustada(IEnumerable<FilaPresDia> filas, Func<FilaPresDia, string> clave)
        => filas.GroupBy(clave).ToDictionary(g => g.Key, g => (g.Sum(x => x.Presu), g.Sum(x => x.Ejec)));

    // Agrega FilaPresDia por un selector de clave (Centro/Canal/Suplidor/Categoría) + nombre,
    // con Alcance%/Diferencia — misma fórmula que el resto del sistema (Ejecutado-Presupuesto).
    // PRIMARIO desde 2026-09-24: 'filas' es MNTDCAT2DIA ("Meta lineal" — presupuesto diario
    // SIN ajuste por déficit acumulado, pedido explícito: "los presupuestos, tómalos de la
    // meta lineal, no el acumulado... para todos los niveles"). 'ajustada' (opcional, solo
    // Ventas) agrega presupuestoAjustada/logroAjustadaPct/diferenciaAjustada desde MNTDCAT1DIA
    // ("Meta ajustada por déficit" — la que se usaba como principal hasta ahora, mismo nombre
    // que ya usa el módulo Presupuestos en /apipresupuestos/deldia) — se deja como columna de
    // referencia, no se descarta el dato.
    // 'diasFactor' (opcional, solo el punto "Resultados acumulados del Mes") = diasTranscurridos/
    // diasDelMes — agrega presupuestoAlDia (=presupuesto*diasFactor, la meta "a ritmo" a esta
    // altura del mes) y logroAlDiaPct (=ejecutado/presupuestoAlDia). alcancePct sigue siendo
    // "Logro Acumulado %" (ejecutado vs el presupuesto TOTAL del mes, sin prorratear) — pedido
    // explícito 2026-09-20: "el logro al día... vs el presupuesto al día... el logro acumulado
    // ejecutado vs. el presupuesto del mes".
    // Orden: por "Logro al día %" cuando hay diasFactor (el punto del mes), si no por
    // "Logro Acumulado %"/"Logro del día %" (alcancePct) — pedido explícito 2026-09-19,
    // "ordena los datos por el logro al día en todos los niveles".
    private static List<object> AgruparAlcance(IEnumerable<FilaPresDia> filas, Func<FilaPresDia, string> clave, Func<FilaPresDia, string> nombre, Dictionary<string, (double presu, double ejec)>? ajustada = null, double? diasFactor = null)
        => filas.GroupBy(clave).Select(g =>
        {
            var f = g.First(); double presu = g.Sum(x => x.Presu), ejec = g.Sum(x => x.Ejec);
            double? alcancePct = presu != 0 ? 100.0 * ejec / presu : (double?)null;
            var aj = ajustada != null && ajustada.TryGetValue(g.Key, out var av) ? av : ((double presu, double ejec)?)null;
            double? presuAlDia = diasFactor.HasValue ? presu * diasFactor.Value : (double?)null;
            double? logroAlDiaPct = presuAlDia.HasValue && presuAlDia.Value != 0 ? 100.0 * ejec / presuAlDia.Value : (double?)null;
            return (orden: diasFactor.HasValue ? logroAlDiaPct : alcancePct, fila: (object)new {
                clave = g.Key, nombre = nombre(f), presupuesto = presu, ejecutado = ejec, alcancePct, diferencia = ejec - presu,
                presupuestoAjustada = aj?.presu, logroAjustadaPct = aj != null && aj.Value.presu != 0 ? 100.0 * aj.Value.ejec / aj.Value.presu : (double?)null,
                diferenciaAjustada = aj != null ? aj.Value.ejec - aj.Value.presu : (double?)null,
                presupuestoAlDia = presuAlDia, logroAlDiaPct,
            });
        }).OrderByDescending(x => x.orden ?? double.NegativeInfinity).Select(x => x.fila).ToList();

    // Centro→Canal→Supervisor→Vendedor: 1 fila por Supervisor con sus Vendedores anidados
    // (el front decide, con el toggle Resumido/Detallado, si expande el nivel Vendedor —
    // el backend siempre trae el detalle completo, más simple y evita otro round-trip).
    // 'ajustada'/'ajustadaVendedor' opcionales (solo Ventas), mismo criterio que AgruparAlcance.
    // 'diasFactor' opcional (solo el punto del Mes) — mismo criterio que AgruparAlcance,
    // aplica el MISMO factor a Supervisor y a cada Vendedor anidado.
    private static List<object> AgruparPorSupervisor(IEnumerable<FilaPresDia> filas, Dictionary<string, (double presu, double ejec)>? ajustada = null, Dictionary<string, (double presu, double ejec)>? ajustadaVendedor = null, double? diasFactor = null)
        => filas.GroupBy(x => x.SupCod).Select(g =>
        {
            double presu = g.Sum(x => x.Presu), ejec = g.Sum(x => x.Ejec);
            var vendedores = g.GroupBy(x => x.RepCodigo).Select(v =>
            {
                var f = v.First(); double vp = v.Sum(x => x.Presu), ve = v.Sum(x => x.Ejec);
                double? vpct = vp != 0 ? 100.0 * ve / vp : (double?)null;
                var vaj = ajustadaVendedor != null && ajustadaVendedor.TryGetValue(f.RepCodigo, out var vav) ? vav : ((double presu, double ejec)?)null;
                double? vPresuAlDia = diasFactor.HasValue ? vp * diasFactor.Value : (double?)null;
                double? vLogroAlDiaPct = vPresuAlDia.HasValue && vPresuAlDia.Value != 0 ? 100.0 * ve / vPresuAlDia.Value : (double?)null;
                return (orden: diasFactor.HasValue ? vLogroAlDiaPct : vpct, fila: (object)new {
                    clave = f.RepCodigo, nombre = f.RepNombre, presupuesto = vp, ejecutado = ve, alcancePct = vpct, diferencia = ve - vp,
                    presupuestoAjustada = vaj?.presu, logroAjustadaPct = vaj != null && vaj.Value.presu != 0 ? 100.0 * vaj.Value.ejec / vaj.Value.presu : (double?)null,
                    diferenciaAjustada = vaj != null ? vaj.Value.ejec - vaj.Value.presu : (double?)null,
                    presupuestoAlDia = vPresuAlDia, logroAlDiaPct = vLogroAlDiaPct,
                });
            }).OrderByDescending(x => x.orden ?? double.NegativeInfinity).Select(x => x.fila).ToList();
            double? pct = presu != 0 ? 100.0 * ejec / presu : (double?)null;
            var aj = ajustada != null && ajustada.TryGetValue(g.Key, out var av) ? av : ((double presu, double ejec)?)null;
            double? presuAlDia = diasFactor.HasValue ? presu * diasFactor.Value : (double?)null;
            double? logroAlDiaPct = presuAlDia.HasValue && presuAlDia.Value != 0 ? 100.0 * ejec / presuAlDia.Value : (double?)null;
            return (orden: diasFactor.HasValue ? logroAlDiaPct : pct, fila: (object)new {
                clave = g.Key, nombre = g.First().SupNombre, presupuesto = presu, ejecutado = ejec, alcancePct = pct, diferencia = ejec - presu, vendedores,
                presupuestoAjustada = aj?.presu, logroAjustadaPct = aj != null && aj.Value.presu != 0 ? 100.0 * aj.Value.ejec / aj.Value.presu : (double?)null,
                diferenciaAjustada = aj != null ? aj.Value.ejec - aj.Value.presu : (double?)null,
                presupuestoAlDia = presuAlDia, logroAlDiaPct,
            });
        }).OrderByDescending(x => x.orden ?? double.NegativeInfinity).Select(x => x.fila).ToList();

    // Suplidor→Categoría: 1 fila por Suplidor (derivado del mapa Cat1→Cat3 dominante) con
    // sus Categorías anidadas debajo — mismo patrón que AgruparPorSupervisor (Supervisor→
    // Vendedor). El subtotal del Suplidor SIEMPRE suma exacto sus Categorías hijas porque
    // ambos niveles salen de la MISMA fila fuente.
    // Orden: por "Logro del día %" descendente en AMBOS niveles — pedido explícito
    // 2026-09-19 ("ordena los datos por el logro al día en todos los niveles"), reemplaza
    // el orden por presupuesto descendente pedido el 2026-09-03 (instrucción posterior
    // del usuario, mismo criterio que la corrección de atribución en Comparativo Ctes.).
    // 'ajustada'/'ajustadaCategoria' opcionales (solo Ventas). 'diasFactor' opcional (solo el
    // punto del Mes) — mismo criterio que AgruparAlcance/AgruparPorSupervisor.
    private static List<object> AgruparPorSuplidorConCategorias(IEnumerable<FilaPresDia> filas, Func<int?, string> claveSuplidor, Func<int?, string> nombreSuplidor, Dictionary<int, string> cat1Nombres, Dictionary<string, (double presu, double ejec)>? ajustada = null, Dictionary<string, (double presu, double ejec)>? ajustadaCategoria = null, double? diasFactor = null)
        => filas.GroupBy(x => claveSuplidor(x.DimId)).Select(g =>
        {
            double presu = g.Sum(x => x.Presu), ejec = g.Sum(x => x.Ejec);
            var categorias = g.GroupBy(x => (x.DimId ?? 0).ToString()).Select(c =>
            {
                var f = c.First(); double cp = c.Sum(x => x.Presu), ce = c.Sum(x => x.Ejec);
                double? cpct = cp != 0 ? 100.0 * ce / cp : (double?)null;
                var clave = f.DimId?.ToString() ?? "0";
                var caj = ajustadaCategoria != null && ajustadaCategoria.TryGetValue(clave, out var cav) ? cav : ((double presu, double ejec)?)null;
                double? cPresuAlDia = diasFactor.HasValue ? cp * diasFactor.Value : (double?)null;
                double? cLogroAlDiaPct = cPresuAlDia.HasValue && cPresuAlDia.Value != 0 ? 100.0 * ce / cPresuAlDia.Value : (double?)null;
                return (orden: diasFactor.HasValue ? cLogroAlDiaPct : cpct, fila: (object)new {
                    clave, nombre = f.DimId.HasValue && cat1Nombres.TryGetValue(f.DimId.Value, out var n) ? n : "(Sin categoría)", presupuesto = cp, ejecutado = ce, alcancePct = cpct, diferencia = ce - cp,
                    presupuestoAjustada = caj?.presu, logroAjustadaPct = caj != null && caj.Value.presu != 0 ? 100.0 * caj.Value.ejec / caj.Value.presu : (double?)null,
                    diferenciaAjustada = caj != null ? caj.Value.ejec - caj.Value.presu : (double?)null,
                    presupuestoAlDia = cPresuAlDia, logroAlDiaPct = cLogroAlDiaPct,
                });
            }).OrderByDescending(x => x.orden ?? double.NegativeInfinity).Select(x => x.fila).ToList();
            double? pct = presu != 0 ? 100.0 * ejec / presu : (double?)null;
            var aj = ajustada != null && ajustada.TryGetValue(g.Key, out var av) ? av : ((double presu, double ejec)?)null;
            double? presuAlDia = diasFactor.HasValue ? presu * diasFactor.Value : (double?)null;
            double? logroAlDiaPct = presuAlDia.HasValue && presuAlDia.Value != 0 ? 100.0 * ejec / presuAlDia.Value : (double?)null;
            return (orden: diasFactor.HasValue ? logroAlDiaPct : pct, fila: (object)new {
                clave = g.Key, nombre = nombreSuplidor(g.First().DimId), presupuesto = presu, ejecutado = ejec, alcancePct = pct, diferencia = ejec - presu, categorias,
                presupuestoAjustada = aj?.presu, logroAjustadaPct = aj != null && aj.Value.presu != 0 ? 100.0 * aj.Value.ejec / aj.Value.presu : (double?)null,
                diferenciaAjustada = aj != null ? aj.Value.ejec - aj.Value.presu : (double?)null,
                presupuestoAlDia = presuAlDia, logroAlDiaPct,
            });
        }).OrderByDescending(x => x.orden ?? double.NegativeInfinity).Select(x => x.fila).ToList();

    public sealed record CierreDiarioSeccion(List<object> porCentro, List<object> porCanal, List<object> porSupervisor, List<object> porSuplidor, string? aviso);

    // ══════════════════════════════════════════════════════════════════════════
    // ── "Resumen Cierre del Día" (Gobierno Comercial, debajo de Ventas Proyectadas) ──
    // Pedido del usuario 2026-10-06. Entrega los datos de los puntos 1 (Pedidos vs Presupuesto por
    // Centro/Canal/Supervisor/Suplidor) y 2 (Cumplimiento y Efectividad de la visita por Supervisor);
    // el punto 3 (AMCO-GT) se calcula en el navegador con /apiseg/semana (el AMCO-GT se puntúa en JS,
    // igual que en Seguimientos) y los hallazgos/compromisos (puntos 4 y 5) se arman en el front con
    // todo junto. Cada fila trae su HISTÓRICO para medir contra el comportamiento habitual y no contra
    // el resultado del día (el mayorista es irregular y no debe medirse linealmente por el día):
    //   - Pedidos: alcance lineal acumulado de los días anteriores del mes (si el día es ≤5, el mes anterior completo).
    //   - Visitas: promedio de los 30 días anteriores y el mes calendario anterior.
    // Mismo criterio que el Cierre Diario: Presupuesto lineal MNTDCAT2DIA (primario) y ajustado MNTDCAT1DIA,
    // Ejecutado = PreEjecutado tal cual, Suplidor derivado del mapa Cat1→Cat3, Institucional excluido salvo filtro.
    // Rango [fechaIni..fechaFin] (por defecto el día de hoy) + los mismos filtros del resto de informes y
    // División (el front manda 10,11,12 por defecto, igual que AMCO-GT en Seguimientos). Si el rango cruza
    // meses se consulta mes por mes y se suma.
    [HttpGet("resumen-cierre-dia")]
    public IActionResult ResumenCierreDia(
        [FromQuery] string? fechaIni, [FromQuery] string? fechaFin, [FromQuery] string? fecha,
        [FromQuery] string? suplidor, [FromQuery] string? categoria,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(fechaIni)) fechaIni = fecha;
        if (string.IsNullOrWhiteSpace(fechaFin)) fechaFin = fechaIni;
        if (!DateTime.TryParse(fechaIni, out var dIni) || !DateTime.TryParse(fechaFin, out var dFin) || dIni.Date > dFin.Date)
            return BadRequest(new { ok = false, error = "Rango de fechas inválido" });
        if ((dFin.Date - dIni.Date).TotalDays > 62) return BadRequest(new { ok = false, error = "El rango no puede superar 62 días" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            return Ok(GenerarResumenCierreDia(cn, dIni.Date, dFin.Date, suplidor, categoria, centros, canales, divisiones, supervisores, vendedores));
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    internal static object GenerarResumenCierreDia(
        SqlConnection cn, DateTime fecha, DateTime fechaFin, string? suplidor, string? categoria,
        string? centros, string? canales, string? divisiones, string? supervisores, string? vendedores)
    {
        // 'fecha' = inicio del rango (referencia del histórico); 'fechaFin' = fin del rango (mes del mapa y día siguiente).
        int anio = fecha.Year, mes = fecha.Month, dia = fecha.Day;
        var vSuplidor = ApiPresupuestosController.ParseList(suplidor);
        var vCategoria = ApiPresupuestosController.ParseList(categoria);
        var vCentros = ApiPresupuestosController.ParseList(centros);
        var vCanales = ApiPresupuestosController.ParseList(canales);
        var vSupervisores = ApiPresupuestosController.ParseList(supervisores);
        var vVendedores = ApiPresupuestosController.ParseList(vendedores);
        var vDiv = ApiPresupuestosController.ParseList(divisiones);
        var tramos = PartirEnTramosDeMes(fecha, fechaFin);

        // ── Día siguiente hábil para los compromisos: sábado → lunes; domingo → lunes; resto +1 (desde el fin del rango) ──
        var manana = fechaFin.DayOfWeek == DayOfWeek.Saturday ? fechaFin.AddDays(2) : fechaFin.AddDays(1);
        var cultura = new System.Globalization.CultureInfo("es-DO");

        // ══ 1. Pedidos vs Presupuesto ══
        var mapa = PresupuestosCore.MapaCat1ACat3Dia(cn, anio, mes);
        List<string> vCat1Efectivo = vCategoria;
        if (vCategoria.Count == 0 && vSuplidor.Count > 0)
        {
            var supSet = vSuplidor.Select(s => int.TryParse(s, out var n) ? n : -1).ToHashSet();
            vCat1Efectivo = mapa.Where(kv => supSet.Contains(kv.Value)).Select(kv => kv.Key.ToString()).ToList();
            if (vCat1Efectivo.Count == 0) vCat1Efectivo.Add("-1");
        }
        List<FilaPresDia> Dia(string tipo, int a, int m, int d0, int? d1)
        {
            var p = new Dictionary<string, object>();
            var f = ApiPresupuestosController.BuildRefFiltroDiario("cat1", vCat1Efectivo, new List<string>(), p);
            return ConsultarPresupuestoDia(cn, tipo, a, m, d0, vCentros, vCanales, vDiv, vSupervisores, vVendedores, f, p, d1);
        }
        // Histórico de pedidos: días anteriores del mes; si estamos en los primeros 5 días, el mes anterior completo.
        int hA = anio, hM = mes, hD0 = 1, hD1 = dia - 1;
        string histEtiqueta = $"1 al {dia - 1} de {fecha.ToString("MMMM", cultura)}";
        if (dia <= 5)
        {
            var pm = new DateTime(anio, mes, 1).AddMonths(-1);
            hA = pm.Year; hM = pm.Month; hD1 = DateTime.DaysInMonth(hA, hM);
            histEtiqueta = "todo " + pm.ToString("MMMM", cultura);
        }
        var filasHoy = tramos.SelectMany(t => Dia("MNTDCAT2DIA", t.Anio, t.Mes, t.DiaDesde, t.DiaHasta)).ToList();
        var filasAj = tramos.SelectMany(t => Dia("MNTDCAT1DIA", t.Anio, t.Mes, t.DiaDesde, t.DiaHasta)).ToList();
        var filasHist = Dia("MNTDCAT2DIA", hA, hM, hD0, hD1);

        var cat3Nombres = ObtenerNombresCat3(cn);
        string ClaveSup(int? cat1) => cat1.HasValue && mapa.TryGetValue(cat1.Value, out var c3) ? c3.ToString() : "0";
        string NombreSup(int? cat1) => cat1.HasValue && mapa.TryGetValue(cat1.Value, out var c3) ? (cat3Nombres.TryGetValue(c3, out var n) ? n : ("Suplidor " + c3)) : "(Sin suplidor)";

        object FilaPed(string clave, string nombre, double presu, double ejec, (double presu, double ejec)? aj, (double presu, double ejec)? hi) => new
        {
            clave, nombre, presupuesto = presu, presupuestoAjustado = aj?.presu, ejecutado = ejec,
            alcancePct = presu != 0 ? 100.0 * ejec / presu : (double?)null,
            alcanceAjustadoPct = aj != null && aj.Value.presu != 0 ? 100.0 * aj.Value.ejec / aj.Value.presu : (double?)null,
            diferencia = ejec - presu,
            histPresupuesto = hi?.presu, histEjecutado = hi?.ejec,
            histAlcancePct = hi != null && hi.Value.presu != 0 ? 100.0 * hi.Value.ejec / hi.Value.presu : (double?)null,
        };
        List<object> TablaPed(Func<FilaPresDia, string> clave, Func<FilaPresDia, string> nombre)
        {
            var aj = AgruparAjustada(filasAj, clave); var hi = AgruparAjustada(filasHist, clave);
            return filasHoy.GroupBy(clave).Select(g =>
            {
                double presu = g.Sum(x => x.Presu), ejec = g.Sum(x => x.Ejec);
                (double presu, double ejec)? a = aj.TryGetValue(g.Key, out var av) ? av : ((double presu, double ejec)?)null;
                (double presu, double ejec)? h = hi.TryGetValue(g.Key, out var hv) ? hv : ((double presu, double ejec)?)null;
                return (orden: presu != 0 ? ejec / presu : double.NegativeInfinity,
                        fila: FilaPed(g.Key, nombre(g.First()), presu, ejec, a, h));
            }).OrderByDescending(x => x.orden).Select(x => x.fila).ToList();
        }
        var totAj = (filasAj.Sum(x => x.Presu), filasAj.Sum(x => x.Ejec));
        var totHi = (filasHist.Sum(x => x.Presu), filasHist.Sum(x => x.Ejec));
        var totalPed = FilaPed("", "Total", filasHoy.Sum(x => x.Presu), filasHoy.Sum(x => x.Ejec), totAj, totHi);

        // ══ 2. Cumplimiento y Efectividad de la visita, por Supervisor (ResumenVisitasRm) ══
        var pm0 = new DateTime(anio, mes, 1).AddMonths(-1);
        var pm1 = pm0.AddMonths(1);
        var h0 = fecha.AddDays(-30); // histórico: 30 días previos al INICIO del rango
        var vp = new Dictionary<string, object> { ["@f"] = fecha, ["@f2"] = fechaFin, ["@h0"] = h0, ["@pm0"] = pm0, ["@pm1"] = pm1 };
        var vf = ApiPresupuestosController.BuildIn("r.CedCodigo", vCentros, "vce", vp)
               + ApiPresupuestosController.BuildIn("r.RepSupervisor", vSupervisores, "vsu", vp)
               + ApiPresupuestosController.BuildIn("rv.RepCodigo", vVendedores, "vve", vp)
               + ApiPresupuestosController.BuildIn("ra.RepTipe", vDiv, "vdi", vp)
               + (vCanales.Count > 0 ? ApiPresupuestosController.BuildIn("ra.CanID", vCanales, "vca", vp) : "");
        var sqlV = $@"
SELECT ISNULL(r.RepSupervisor,'') SupCod, ISNULL(ra.RepTipe,'') Division, MAX(ISNULL(sup.RepNombre, r.RepSupervisor)) SupNombre,
       MAX(ISNULL(cd.CedDescripcion, r.CedCodigo)) Centro,
       COUNT(DISTINCT CASE WHEN CAST(rv.FechaVisita AS DATE) BETWEEN @f AND @f2 AND rv.TotalAvisitar>0 THEN rv.RepCodigo END) Vendedores,
       SUM(CASE WHEN CAST(rv.FechaVisita AS DATE) BETWEEN @f AND @f2 THEN ISNULL(rv.TotalAvisitar,0) ELSE 0 END) AVis,
       SUM(CASE WHEN CAST(rv.FechaVisita AS DATE) BETWEEN @f AND @f2 THEN ISNULL(rv.Visitados,0) ELSE 0 END) Vis,
       SUM(CASE WHEN CAST(rv.FechaVisita AS DATE) BETWEEN @f AND @f2 THEN ISNULL(rv.PedidosRealizados,0) ELSE 0 END) Ped,
       SUM(CASE WHEN CAST(rv.FechaVisita AS DATE)>=@h0 AND CAST(rv.FechaVisita AS DATE)<@f THEN ISNULL(rv.TotalAvisitar,0) ELSE 0 END) hAVis,
       SUM(CASE WHEN CAST(rv.FechaVisita AS DATE)>=@h0 AND CAST(rv.FechaVisita AS DATE)<@f THEN ISNULL(rv.Visitados,0) ELSE 0 END) hVis,
       SUM(CASE WHEN CAST(rv.FechaVisita AS DATE)>=@h0 AND CAST(rv.FechaVisita AS DATE)<@f THEN ISNULL(rv.PedidosRealizados,0) ELSE 0 END) hPed,
       SUM(CASE WHEN CAST(rv.FechaVisita AS DATE)>=@pm0 AND CAST(rv.FechaVisita AS DATE)<@pm1 THEN ISNULL(rv.TotalAvisitar,0) ELSE 0 END) mAVis,
       SUM(CASE WHEN CAST(rv.FechaVisita AS DATE)>=@pm0 AND CAST(rv.FechaVisita AS DATE)<@pm1 THEN ISNULL(rv.Visitados,0) ELSE 0 END) mVis,
       SUM(CASE WHEN CAST(rv.FechaVisita AS DATE)>=@pm0 AND CAST(rv.FechaVisita AS DATE)<@pm1 THEN ISNULL(rv.PedidosRealizados,0) ELSE 0 END) mPed
FROM ResumenVisitasRm rv WITH (NOLOCK)
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=rv.RepCodigo
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE CAST(rv.FechaVisita AS DATE)>=@pm0 AND CAST(rv.FechaVisita AS DATE)<=@f2 {vf}
GROUP BY ISNULL(r.RepSupervisor,''), ISNULL(ra.RepTipe,'')";
        var visRaw = new List<(string sup, string div, string nombre, string centro, int vend, double av, double v, double p, double hav, double hv, double hp, double mav, double mv, double mp)>();
        using (var cmd = new SqlCommand(sqlV, cn) { CommandTimeout = 60 })
        {
            foreach (var kv in vp) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
            using var rd = cmd.ExecuteReader();
            double D(int i) => rd.IsDBNull(i) ? 0 : Convert.ToDouble(rd.GetValue(i));
            while (rd.Read())
                visRaw.Add((rd.GetString(0), rd.GetString(1), rd.IsDBNull(2) ? "" : rd.GetString(2), rd.IsDBNull(3) ? "" : rd.GetString(3), (int)D(4),
                         D(5), D(6), D(7), D(8), D(9), D(10), D(11), D(12), D(13)));
        }
        // por supervisor (suma de sus divisiones) — misma forma que antes
        var vis = visRaw.GroupBy(x => x.sup).Select(g => (sup: g.Key, nombre: g.First().nombre, centro: g.First().centro, vend: g.Sum(x => x.vend), av: g.Sum(x => x.av), v: g.Sum(x => x.v), p: g.Sum(x => x.p),
            hav: g.Sum(x => x.hav), hv: g.Sum(x => x.hv), hp: g.Sum(x => x.hp), mav: g.Sum(x => x.mav), mv: g.Sum(x => x.mv), mp: g.Sum(x => x.mp))).ToList();
        object FilaVis(string clave, string nombre, string centro, int vend, double av, double v, double p, double hav, double hv, double hp, double mav, double mv, double mp) => new
        {
            clave, nombre, centro, vendedores = vend, aVisitar = av, visitados = v, pedidos = p,
            cumplimientoPct = av > 0 ? 100.0 * v / av : (double?)null,
            efectividadPct = v > 0 ? 100.0 * p / v : (double?)null,
            histCumplimientoPct = hav > 0 ? 100.0 * hv / hav : (double?)null,
            histEfectividadPct = hv > 0 ? 100.0 * hp / hv : (double?)null,
            mesAntCumplimientoPct = mav > 0 ? 100.0 * mv / mav : (double?)null,
            mesAntEfectividadPct = mv > 0 ? 100.0 * mp / mv : (double?)null,
            // sumas crudas: permiten re-agregar por persona/división en el navegador sin promediar porcentajes
            histAVisitar = hav, histVisitados = hv, histPedidos = hp, mesAntAVisitar = mav, mesAntVisitados = mv, mesAntPedidos = mp,
        };
        var visFilas = vis.Where(x => x.av > 0 || x.v > 0)
            .OrderByDescending(x => x.v > 0 ? x.p / x.v : -1)
            .Select(x => FilaVis(x.sup, x.nombre, x.centro, x.vend, x.av, x.v, x.p, x.hav, x.hv, x.hp, x.mav, x.mv, x.mp)).ToList();
        var visDivFilas = visRaw.Where(x => x.av > 0 || x.v > 0)
            .Select(x => FilaVis(x.sup + "|" + x.div, string.IsNullOrWhiteSpace(x.div) ? "(Sin división)" : x.div, x.centro, x.vend, x.av, x.v, x.p, x.hav, x.hv, x.hp, x.mav, x.mv, x.mp)).ToList();
        // 3 calendarios de 30 días (Pedidos RD$, Activaciones) terminando en la fecha final del informe — el de AMCO-GT se arma en el navegador
        var calPed = new List<DiaCalendario>(); var calAct = new List<DiaCalendario>();
        try { (calPed, calAct) = CalcularCalendarios30Dias(cn, fechaFin, vSuplidor, vCategoria, vCentros, vCanales, vDiv, vSupervisores, vVendedores); } catch { }
        object MapDiaRc(DiaCalendario d) => new { fecha = d.Fecha, diaSemana = d.DiaSemana, diaMes = d.DiaMes, valor = d.Valor, valorSemanaAnterior = d.ValorSemanaAnterior, deltaPct = d.DeltaPct };
        var visTotal = FilaVis("", "Total", "", vis.Sum(x => x.vend), vis.Sum(x => x.av), vis.Sum(x => x.v), vis.Sum(x => x.p),
            vis.Sum(x => x.hav), vis.Sum(x => x.hv), vis.Sum(x => x.hp), vis.Sum(x => x.mav), vis.Sum(x => x.mv), vis.Sum(x => x.mp));

        return new
        {
            ok = true,
            fecha = fecha.ToString("yyyy-MM-dd"),
            fechaFin = fechaFin.ToString("yyyy-MM-dd"),
            diaSemana = fecha.ToString("dddd", cultura),
            diaSemanaFin = fechaFin.ToString("dddd", cultura),
            fechaManana = manana.ToString("yyyy-MM-dd"),
            diaSemanaManana = manana.ToString("dddd", cultura),
            histPedidosEtiqueta = histEtiqueta,
            pedidos = new
            {
                total = totalPed,
                porCentro = TablaPed(x => x.CedCodigo, x => x.CentroNombre),
                porCanal = TablaPed(x => x.CanID.ToString(), x => x.CanalNombre),
                porSupervisor = TablaPed(x => x.SupCod, x => string.IsNullOrWhiteSpace(x.SupNombre) ? "(Sin supervisor)" : x.SupNombre),
                porSuplidor = TablaPed(x => ClaveSup(x.DimId), x => NombreSup(x.DimId)),
                porSupervisorDiv = TablaPed(x => x.SupCod + "|" + x.Division, x => string.IsNullOrWhiteSpace(x.Division) ? "(Sin división)" : x.Division),
            },
            visitas = new { total = visTotal, porSupervisor = visFilas, porSupervisorDiv = visDivFilas },
            calendarioPedidos = calPed.Select(MapDiaRc).ToList(), calendarioActivaciones = calAct.Select(MapDiaRc).ToList(),
        };
    }

    [HttpGet("cierre-diario")]
    public IActionResult CierreDiario(
        [FromQuery] string fecha, [FromQuery] string? suplidor, [FromQuery] string? categoria,
        [FromQuery] string? centros, [FromQuery] string? canales, [FromQuery] string? divisiones,
        [FromQuery] string? supervisores, [FromQuery] string? vendedores)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (!DateTime.TryParse(fecha, out var fechaDt)) return BadRequest(new { ok = false, error = "Fecha inválida" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            return Ok(GenerarCierreDiario(cn, fechaDt, suplidor, categoria, centros, canales, divisiones, supervisores, vendedores));
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Núcleo del Cierre Diario, SIN dependencia de HttpContext/sesión — reusado tal cual
    // por el endpoint HTTP (arriba) y por CierreDiarioSchedulerService (envío programado
    // 6pm, sin sesión de navegador) para no duplicar la lógica de agregación.
    internal static object GenerarCierreDiario(
        SqlConnection cn, DateTime fechaDt, string? suplidor, string? categoria,
        string? centros, string? canales, string? divisiones, string? supervisores, string? vendedores)
    {
        {
            int anio = fechaDt.Year, mes = fechaDt.Month, dia = fechaDt.Day;

            var vSuplidor = ApiPresupuestosController.ParseList(suplidor);
            var vCategoria = ApiPresupuestosController.ParseList(categoria);
            var vCentros = ApiPresupuestosController.ParseList(centros);
            var vCanales = ApiPresupuestosController.ParseList(canales);
            var vDivisiones = ApiPresupuestosController.ParseList(divisiones);
            var vSupervisores = ApiPresupuestosController.ParseList(supervisores);
            var vVendedores = ApiPresupuestosController.ParseList(vendedores);

            // Mapa Cat1<->Cat3 (solo si hace falta traducir entre dimensiones — se calcula
            // una sola vez, se reusa para Ventas y Activaciones-por-categoría).
            var necesitaMapa = (vSuplidor.Count > 0 && vCategoria.Count == 0) || (vCategoria.Count > 0 && vSuplidor.Count == 0);
            var mapaCat1Cat3 = necesitaMapa ? PresupuestosCore.MapaCat1ACat3Dia(cn, anio, mes) : null;

            // Cat1ID efectivos (para consultas Cat1-keyed: Ventas-dinero, Activaciones-categoría).
            List<string> vCat1Efectivo = vCategoria;
            if (mapaCat1Cat3 != null && vCategoria.Count == 0 && vSuplidor.Count > 0)
            {
                var supSet = vSuplidor.Select(s => int.TryParse(s, out var n) ? n : -1).ToHashSet();
                vCat1Efectivo = mapaCat1Cat3.Where(kv => supSet.Contains(kv.Value)).Select(kv => kv.Key.ToString()).ToList();
                if (vCat1Efectivo.Count == 0) vCat1Efectivo.Add("-1");
            }
            // Cat3ID efectivos (para consulta Cat3-keyed: Activaciones-org/suplidor).
            List<string> vCat3Efectivo = vSuplidor;
            if (mapaCat1Cat3 != null && vSuplidor.Count == 0 && vCategoria.Count > 0)
            {
                var cat1Set = vCategoria.Select(s => int.TryParse(s, out var n) ? n : -1).ToHashSet();
                vCat3Efectivo = mapaCat1Cat3.Where(kv => cat1Set.Contains(kv.Key)).Select(kv => kv.Value.ToString()).Distinct().ToList();
                if (vCat3Efectivo.Count == 0) vCat3Efectivo.Add("-1");
            }

            var parmsCat1 = new Dictionary<string, object>();
            var dimFiltroCat1 = ApiPresupuestosController.BuildRefFiltroDiario("cat1", vCat1Efectivo, new List<string>(), parmsCat1);
            var parmsCat3 = new Dictionary<string, object>();
            var dimFiltroCat3 = ApiPresupuestosController.BuildRefFiltroDiario("cat3", new List<string>(), vCat3Efectivo, parmsCat3);

            // ── Ventas (dinero) — MNTDCAT2DIA ("Meta lineal"), Cat1-keyed ───────────────
            // PRIMARIO desde 2026-09-24 — pedido explícito del usuario: "los presupuestos,
            // tómalos de la meta lineal, no el acumulado... el presupuesto y el logro hazlo
            // en base al presupuesto lineal del día. para todos los niveles del informe."
            // Antes el primario era MNTDCAT1DIA ("Meta ajustada por déficit", que acumula/
            // ajusta el faltante de días previos) — misma terminología que ya usa el módulo
            // Presupuestos (/apipresupuestos/deldia, columnas "Meta ajustada"/"Meta lineal").
            var filasVentas = ConsultarPresupuestoDia(cn, "MNTDCAT2DIA", anio, mes, dia, vCentros, vCanales, vDivisiones, vSupervisores, vVendedores, dimFiltroCat1, parmsCat1);
            var cat1Nombres = ObtenerNombresCat1(cn);
            var cat3Nombres = ObtenerNombresCat3(cn);
            // El rollup "por Suplidor" de Ventas necesita el mapa Cat1->Cat3 SIEMPRE (para
            // agrupar cada fila bajo su suplidor dominante), no solo cuando hubo filtro
            // cruzado — se reusa el ya calculado arriba si existe, si no se calcula ahora.
            var mapaRollupSuplidor = mapaCat1Cat3 ?? PresupuestosCore.MapaCat1ACat3Dia(cn, anio, mes);
            string ClaveSuplidorDeCat1(int? cat1) => cat1.HasValue && mapaRollupSuplidor.TryGetValue(cat1.Value, out var c3) ? c3.ToString() : "0";
            string NombreSuplidorDeCat1(int? cat1) => cat1.HasValue && mapaRollupSuplidor.TryGetValue(cat1.Value, out var c3) ? (cat3Nombres.TryGetValue(c3, out var n) ? n : ("Suplidor " + c3)) : "(Sin suplidor)";
            // ── Meta Ajustada (columna de referencia) — MNTDCAT1DIA, mismo Cat1-keyed ────
            // Secundario desde 2026-09-24 (antes era el primario). Solo existe para Ventas
            // (dinero) — Activaciones no tiene variante A/B mapeada en PresupuestosCore.Mapa
            // (TipoDiarioA/B son null para esas familias), así que no se inventa una serie
            // que la data no tiene.
            var filasVentasAjustada = ConsultarPresupuestoDia(cn, "MNTDCAT1DIA", anio, mes, dia, vCentros, vCanales, vDivisiones, vSupervisores, vVendedores, dimFiltroCat1, parmsCat1);
            var ajustadaPorCentro = AgruparAjustada(filasVentasAjustada, x => x.CedCodigo);
            var ajustadaPorCanal = AgruparAjustada(filasVentasAjustada, x => x.CanID.ToString());
            var ajustadaPorSupervisor = AgruparAjustada(filasVentasAjustada, x => x.SupCod);
            var ajustadaPorVendedor = AgruparAjustada(filasVentasAjustada, x => x.RepCodigo);
            var ajustadaPorSuplidor = AgruparAjustada(filasVentasAjustada, x => ClaveSuplidorDeCat1(x.DimId));
            var ajustadaPorCategoria = AgruparAjustada(filasVentasAjustada, x => (x.DimId ?? 0).ToString());

            var ventas = new CierreDiarioSeccion(
                porCentro: AgruparAlcance(filasVentas, x => x.CedCodigo, x => x.CentroNombre, ajustadaPorCentro),
                porCanal: AgruparAlcance(filasVentas, x => x.CanID.ToString(), x => x.CanalNombre, ajustadaPorCanal),
                porSupervisor: AgruparPorSupervisor(filasVentas, ajustadaPorSupervisor, ajustadaPorVendedor),
                porSuplidor: AgruparPorSuplidorConCategorias(filasVentas, ClaveSuplidorDeCat1, NombreSuplidorDeCat1, cat1Nombres, ajustadaPorSuplidor, ajustadaPorCategoria),
                aviso: null);

            // ── Activaciones — UNDDACTCAT3DIA (org, Cat3-keyed nativo) para Centro/Canal/Supervisor ──
            var filasActOrg = ConsultarPresupuestoDia(cn, "UNDDACTCAT3DIA", anio, mes, dia, vCentros, vCanales, vDivisiones, vSupervisores, vVendedores, dimFiltroCat3, parmsCat3);
            // ── Activaciones por Categoría — UNDDLINACTDIA1 (Cat1-keyed nativo) — también
            // fuente de Suplidor (derivado, igual que Ventas) para que el subtotal por
            // Suplidor sume exacto sus Categorías hijas (antes salían de 2 consultas
            // distintas que no reconciliaban — pedido explícito 2026-09-03).
            var parmsCat1b = new Dictionary<string, object>();
            var dimFiltroCat1b = ApiPresupuestosController.BuildRefFiltroDiario("cat1", vCat1Efectivo, new List<string>(), parmsCat1b);
            var filasActCat = ConsultarPresupuestoDia(cn, "UNDDLINACTDIA1", anio, mes, dia, vCentros, vCanales, vDivisiones, vSupervisores, vVendedores, dimFiltroCat1b, parmsCat1b);
            var activaciones = new CierreDiarioSeccion(
                porCentro: AgruparAlcance(filasActOrg, x => x.CedCodigo, x => x.CentroNombre),
                porCanal: AgruparAlcance(filasActOrg, x => x.CanID.ToString(), x => x.CanalNombre),
                porSupervisor: AgruparPorSupervisor(filasActOrg),
                porSuplidor: AgruparPorSuplidorConCategorias(filasActCat, ClaveSuplidorDeCat1, NombreSuplidorDeCat1, cat1Nombres),
                aviso: null);

            // ── "1. Resultados acumulados del Mes" — mismas 4 tablas del día, pero con los
            // PreTipo MENSUALES (MNTDCAT/UNDDACTCAT3/UNDDLINACT) — pedido explícito
            // 2026-09-19. Sin columnas "ajustada" (MNTDCAT1DIA/MNTDCAT2DIA son exclusivamente diarios).
            // diasDelMes/diasTranscurridos/diasRestantes — pedido explícito 2026-09-20: NO
            // son días de calendario ("no me sirve que diga 19"), son DÍAS HÁBILES (lunes a
            // sábado) tal cual los trae VWPresupuestosAlDiaUnidadesProductos (columnas
            // diasdelmes/diasTranscurridos — verificado en vivo: uniformes en TODA la vista
            // para un mismo PreAnio/PreMes, ej. sep-2026 = 26/15 en 8,855 filas), la MISMA
            // fuente que ya usa el resto del sistema para "días hábiles". diasFactor =
            // diasTranscurridos/diasDelMes — el "Logro al día %" se mide contra un
            // Presupuesto AL DÍA (presupuesto del mes prorrateado "a ritmo" hasta hoy, en
            // días HÁBILES), y el "Logro Acumulado %" (alcancePct de siempre) queda como
            // Ejecutado vs el Presupuesto TOTAL del mes, sin prorratear.
            var (diasDelMes, diasTranscurridos, diasRestantes) = ObtenerDiasHabilesMes(cn, anio, mes);
            double diasFactorMes = diasDelMes > 0 ? (double)diasTranscurridos / diasDelMes : 0;
            var parmsMesCat1 = new Dictionary<string, object>();
            var dimFiltroMesCat1 = ApiPresupuestosController.BuildRefFiltro("cat1", new List<string>(), vCat1Efectivo, new List<string>(), parmsMesCat1, "ph");
            var filasVentasMes = ConsultarPresupuestoMes(cn, "MNTDCAT", anio, mes, vCentros, vCanales, vDivisiones, vSupervisores, vVendedores, dimFiltroMesCat1, parmsMesCat1);
            var mesVentas = new CierreDiarioSeccion(
                porCentro: AgruparAlcance(filasVentasMes, x => x.CedCodigo, x => x.CentroNombre, diasFactor: diasFactorMes),
                porCanal: AgruparAlcance(filasVentasMes, x => x.CanID.ToString(), x => x.CanalNombre, diasFactor: diasFactorMes),
                porSupervisor: AgruparPorSupervisor(filasVentasMes, diasFactor: diasFactorMes),
                porSuplidor: AgruparPorSuplidorConCategorias(filasVentasMes, ClaveSuplidorDeCat1, NombreSuplidorDeCat1, cat1Nombres, diasFactor: diasFactorMes),
                aviso: null);

            var parmsMesCat3 = new Dictionary<string, object>();
            var dimFiltroMesCat3 = ApiPresupuestosController.BuildRefFiltro("cat3", vCat3Efectivo, new List<string>(), new List<string>(), parmsMesCat3, "ph");
            var filasActOrgMes = ConsultarPresupuestoMes(cn, "UNDDACTCAT3", anio, mes, vCentros, vCanales, vDivisiones, vSupervisores, vVendedores, dimFiltroMesCat3, parmsMesCat3);
            var parmsMesCat1b = new Dictionary<string, object>();
            var dimFiltroMesCat1b = ApiPresupuestosController.BuildRefFiltro("cat1", new List<string>(), vCat1Efectivo, new List<string>(), parmsMesCat1b, "ph");
            var filasActCatMes = ConsultarPresupuestoMes(cn, "UNDDLINACT", anio, mes, vCentros, vCanales, vDivisiones, vSupervisores, vVendedores, dimFiltroMesCat1b, parmsMesCat1b);
            // Aviso — verificado en vivo 2026-09-19: el PreTipo mensual UNDDACTCAT3 trae
            // PrePresupuesto poblado pero PreEjecutado SIEMPRE en 0 (el proceso externo no
            // lo actualiza a ese nivel), mientras que el mismo mes sumado desde el DIARIO
            // (UNDDACTCAT3DIA) sí trae Ejecutado real. Mismo criterio ya establecido en este
            // archivo para otras discrepancias entre PreTipo con distinta granularidad
            // nativa: "se avisa en el reporte, no se fuerza una reconciliación falsa".
            bool activacionesMesSinEjecutar = filasActOrgMes.Sum(x => x.Presu) > 0 && filasActOrgMes.Sum(x => x.Ejec) == 0;
            var mesActivaciones = new CierreDiarioSeccion(
                porCentro: AgruparAlcance(filasActOrgMes, x => x.CedCodigo, x => x.CentroNombre, diasFactor: diasFactorMes),
                porCanal: AgruparAlcance(filasActOrgMes, x => x.CanID.ToString(), x => x.CanalNombre, diasFactor: diasFactorMes),
                porSupervisor: AgruparPorSupervisor(filasActOrgMes, diasFactor: diasFactorMes),
                porSuplidor: AgruparPorSuplidorConCategorias(filasActCatMes, ClaveSuplidorDeCat1, NombreSuplidorDeCat1, cat1Nombres, diasFactor: diasFactorMes),
                aviso: activacionesMesSinEjecutar ? "El proceso externo (SAP) no está poblando el Ejecutado mensual de Activaciones (PreTipo UNDDACTCAT3) — el Logro% de este bloque saldrá en 0 aunque sí hubo activaciones. Ver el detalle diario (punto 2.1) mientras se corrige del lado de TI." : null);

            var (calVentas, calActivaciones) = CalcularCalendarios30Dias(cn, fechaDt, vSuplidor, vCategoria, vCentros, vCanales, vDivisiones, vSupervisores, vVendedores);
            var resumenEjecutivo = ArmarResumenEjecutivo(fechaDt, ventas, activaciones, calVentas, calActivaciones);
            var pensamiento = ObtenerPensamientoDelDia(cn, fechaDt.Date);

            var nombresFiltro = new
            {
                suplidor = vSuplidor.Select(s => cat3Nombres.TryGetValue(int.Parse(s), out var n) ? n : s).ToList(),
                categoria = vCategoria.Select(s => cat1Nombres.TryGetValue(int.Parse(s), out var n) ? n : s).ToList(),
            };

            // El JSON global del proyecto conserva el casing tal cual (Program.cs desactiva
            // el camelCase automático) — DiaCalendario es un record en PascalCase (idiomático
            // en C#), así que sin este mapeo explícito a minúscula el front (que lee
            // d.fecha/d.valor/d.deltaPct/d.diaMes) recibía siempre "undefined" (bug real
            // encontrado 2026-09-03, todo el resto del sistema usa objetos anónimos ya
            // escritos en minúscula en el propio C# por eso no le pasaba a nadie más).
            object MapDia(DiaCalendario d) => new { fecha = d.Fecha, diaSemana = d.DiaSemana, diaMes = d.DiaMes, valor = d.Valor, valorSemanaAnterior = d.ValorSemanaAnterior, deltaPct = d.DeltaPct };

            return new
            {
                ok = true,
                fecha = fechaDt.ToString("yyyy-MM-dd"),
                filtros = new { suplidor = nombresFiltro.suplidor, categoria = nombresFiltro.categoria, centros = vCentros, canales = vCanales, divisiones = vDivisiones, supervisores = vSupervisores, vendedores = vVendedores },
                ventas, activaciones, mesVentas, mesActivaciones, diasDelMes, diasTranscurridos, diasRestantes,
                calendarioVentas = calVentas.Select(MapDia).ToList(), calendarioActivaciones = calActivaciones.Select(MapDia).ToList(),
                resumenEjecutivo, pensamiento,
            };
        }
    }

    // Filtro organizacional + de producto compartido por calendarios y resumen ejecutivo
    // (todos leen VWPedidos/VWPedidosDetalle directo — fuente confirmada por el usuario
    // 2026-09-03, "VWPedidos"). Mismo default de Canal (excluye Institucional=7000 salvo
    // filtro explícito) que ConsultarPresupuestoDia, para que calendario y sección 1
    // respondan exactamente a los mismos filtros.
    private static string FiltroVWPedidos(
        List<string> vSuplidor, List<string> vCategoria, List<string> vCentros, List<string> vCanales,
        List<string> vDivisiones, List<string> vSupervisores, List<string> vVendedores, Dictionary<string, object> parms)
    {
        var f = "";
        f += ApiPresupuestosController.BuildIn("r.CedCodigo", vCentros, "vce", parms);
        f += vCanales.Count > 0 ? ApiPresupuestosController.BuildIn("ra.CanID", vCanales, "vca", parms) : $" AND ISNULL(ra.CanID,0)<>{PresupuestoBuilderCore.CanalInstitucional}";
        f += ApiPresupuestosController.BuildIn("ra.RepTipe", vDivisiones, "vdi", parms);
        f += ApiPresupuestosController.BuildIn("r.RepSupervisor", vSupervisores, "vsu", parms);
        f += ApiPresupuestosController.BuildIn("d.RepCodigo", vVendedores, "vve", parms);
        f += ApiPresupuestosController.BuildIn("pr.Cat3ID", vSuplidor, "vsup", parms);
        f += ApiPresupuestosController.BuildIn("pr.Cat1ID", vCategoria, "vcat", parms);
        return f;
    }

    public sealed record DiaCalendario(string Fecha, string DiaSemana, int DiaMes, double Valor, double? ValorSemanaAnterior, double? DeltaPct);

    // Sección 2 — 2 calendarios de 30 días (Ventas RD$ y Activaciones), terminando HOY.
    // Se consultan 37 días (30 a mostrar + 7 de respaldo) en una sola pasada por métrica,
    // para poder calcular el indicador +/- de los primeros días mostrados también (vs. el
    // mismo día de la semana, 7 días atrás — confirmado por el usuario).
    private static (List<DiaCalendario> ventas, List<DiaCalendario> activaciones) CalcularCalendarios30Dias(
        SqlConnection cn, DateTime fecha, List<string> vSuplidor, List<string> vCategoria,
        List<string> vCentros, List<string> vCanales, List<string> vDivisiones, List<string> vSupervisores, List<string> vVendedores)
    {
        var d1 = fecha.Date.AddDays(1); var d0 = d1.AddDays(-37);
        var parms = new Dictionary<string, object> { ["@d0"] = d0, ["@d1"] = d1 };
        var filtro = FiltroVWPedidos(vSuplidor, vCategoria, vCentros, vCanales, vDivisiones, vSupervisores, vVendedores, parms);
        var sql = $@"
;WITH B AS (
  SELECT CAST(p.PedFecha AS date) Dia, d.CliID,
         SUM(ISNULL(d.PedCantidad,0)) U, SUM(ISNULL(d.PedCantidad,0)*ISNULL(d.PedPrecio,0)) M
  FROM VWPedidosDetalle d WITH (NOLOCK)
  JOIN VWPedidos p WITH (NOLOCK) ON p.PedSecuencia=d.PedSecuencia AND p.RepCodigo=d.RepCodigo
  JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo=d.RepCodigo
  LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
  LEFT JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
  WHERE p.PedFecha>=@d0 AND p.PedFecha<@d1 {filtro}
  GROUP BY CAST(p.PedFecha AS date), d.CliID
)
SELECT Dia, SUM(M) Monto, SUM(CASE WHEN U>=1 THEN 1 ELSE 0 END) Activaciones
FROM B GROUP BY Dia";
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 90 };
        foreach (var kv in parms) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        var montoPorDia = new Dictionary<DateTime, double>(); var actPorDia = new Dictionary<DateTime, double>();
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
            {
                var dt = rd.GetDateTime(0).Date;
                montoPorDia[dt] = rd.IsDBNull(1) ? 0 : Convert.ToDouble(rd.GetValue(1));
                actPorDia[dt] = rd.IsDBNull(2) ? 0 : Convert.ToDouble(rd.GetValue(2));
            }
        List<DiaCalendario> Armar(Dictionary<DateTime, double> valores)
        {
            var lista = new List<DiaCalendario>();
            for (int i = 29; i >= 0; i--)
            {
                var dt = fecha.Date.AddDays(-i);
                var v = valores.TryGetValue(dt, out var vv) ? vv : 0;
                var dtAnt = dt.AddDays(-7);
                double? vAnt = valores.TryGetValue(dtAnt, out var va) ? va : (dtAnt >= d0 ? 0 : (double?)null);
                double? delta = vAnt.HasValue && vAnt.Value != 0 ? 100.0 * (v - vAnt.Value) / vAnt.Value : (vAnt.HasValue && v > 0 ? 100.0 : (double?)null);
                lista.Add(new DiaCalendario(dt.ToString("yyyy-MM-dd"), dt.ToString("ddd", new System.Globalization.CultureInfo("es-DO")), dt.Day, v, vAnt, delta));
            }
            return lista;
        }
        return (Armar(montoPorDia), Armar(actPorDia));
    }

    // Resumen Ejecutivo — hoy vs. promedio de los últimos N mismos días de semana (N=4),
    // por Centro/Canal/Supervisor (VWPedidos, misma serie que el calendario) + Top5
    // mejor/peor Alcance% (de las secciones ya calculadas contra Presupuesto). Genera
    // ~5 hallazgos en español a partir de los números — sin llamada a IA externa,
    // reporte determinista como el resto del sistema.
    private static object ArmarResumenEjecutivo(DateTime fecha, CierreDiarioSeccion ventas, CierreDiarioSeccion activaciones, List<DiaCalendario> calVentas, List<DiaCalendario> calActivaciones)
    {
        var hallazgos = new List<string>();
        string diaSemana = fecha.ToString("dddd", new System.Globalization.CultureInfo("es-DO"));

        // Comparativo del calendario: hoy vs. mismo día semana anterior (ya viene calculado ahí).
        var hoyVentas = calVentas.LastOrDefault();
        if (hoyVentas != null && hoyVentas.DeltaPct.HasValue)
            hallazgos.Add($"Ventas de hoy ({diaSemana}): RD$ {hoyVentas.Valor:#,0} — {(hoyVentas.DeltaPct.Value >= 0 ? "▲" : "▼")} {Math.Abs(hoyVentas.DeltaPct.Value):0.0}% vs. el {diaSemana} anterior (RD$ {hoyVentas.ValorSemanaAnterior:#,0}).");
        var hoyAct = calActivaciones.LastOrDefault();
        if (hoyAct != null && hoyAct.DeltaPct.HasValue)
            hallazgos.Add($"Activaciones de hoy: {hoyAct.Valor:#,0} — {(hoyAct.DeltaPct.Value >= 0 ? "▲" : "▼")} {Math.Abs(hoyAct.DeltaPct.Value):0.0}% vs. el {diaSemana} anterior ({hoyAct.ValorSemanaAnterior:#,0}).");

        // Mejor/peor Centro y Canal del día (por Alcance% de Ventas vs presupuesto).
        void TopBottomNivel(string etiqueta, List<object> filas)
        {
            var conAlcance = filas.Where(f => ((dynamic)f).alcancePct != null).ToList();
            if (conAlcance.Count == 0) return;
            var mejor = conAlcance.OrderByDescending(f => (double)((dynamic)f).alcancePct).First();
            var peor = conAlcance.OrderBy(f => (double)((dynamic)f).alcancePct).First();
            hallazgos.Add($"{etiqueta}: el de mejor alcance hoy es {((dynamic)mejor).nombre} ({(double)((dynamic)mejor).alcancePct:0.0}%); el de menor es {((dynamic)peor).nombre} ({(double)((dynamic)peor).alcancePct:0.0}%).");
        }
        TopBottomNivel("Por Centro", ventas.porCentro);
        TopBottomNivel("Por Canal", ventas.porCanal);
        // Por Supervisor: alcance agregado de su equipo (ya viene en porSupervisor).
        TopBottomNivel("Por Supervisor", ventas.porSupervisor);
        // Por Suplidor/Categoría (pedido 2026-09-03: "agrega cualquier observación de los
        // suplidores y las categorías") — Categoría se aplana desde porSuplidor[].categorias
        // (ya no viene como lista separada, ver AgruparPorSuplidorConCategorias). Se hace
        // para Ventas Y Activaciones (antes solo corría sobre Ventas).
        List<object> FlattenCategorias(List<object> porSup) => porSup.SelectMany(s => (IEnumerable<object>)((dynamic)s).categorias).ToList();
        TopBottomNivel("Por Suplidor", ventas.porSuplidor);
        TopBottomNivel("Por Categoría", FlattenCategorias(ventas.porSuplidor));
        TopBottomNivel("Activaciones · Por Centro", activaciones.porCentro);
        TopBottomNivel("Activaciones · Por Canal", activaciones.porCanal);
        TopBottomNivel("Activaciones · Por Supervisor", activaciones.porSupervisor);
        TopBottomNivel("Activaciones · Por Suplidor", activaciones.porSuplidor);
        TopBottomNivel("Activaciones · Por Categoría", FlattenCategorias(activaciones.porSuplidor));

        // Top 5 mejores/peores vendedores en Volumen (Ventas) y Activaciones, dentro del
        // logro de presupuesto (rankeados por Alcance%, no por cifra absoluta).
        List<object> Flatten(List<object> porSup) => porSup.SelectMany(s => (IEnumerable<object>)((dynamic)s).vendedores).ToList();
        var vendVentas = Flatten(ventas.porSupervisor).Where(v => ((dynamic)v).alcancePct != null).ToList();
        var vendAct = Flatten(activaciones.porSupervisor).Where(v => ((dynamic)v).alcancePct != null).ToList();
        object Top5(List<object> lista, bool mejores) => lista.OrderBy(v => mejores ? -(double)((dynamic)v).alcancePct : (double)((dynamic)v).alcancePct)
            .Take(5).Select(v => new { nombre = ((dynamic)v).nombre, alcancePct = (double)((dynamic)v).alcancePct }).ToList();

        var top5VentasMejores = Top5(vendVentas, true); var top5VentasPeores = Top5(vendVentas, false);
        var top5ActMejores = Top5(vendAct, true); var top5ActPeores = Top5(vendAct, false);
        if (vendVentas.Count > 0)
        {
            var mv = ((dynamic)((System.Collections.IEnumerable)top5VentasMejores).Cast<object>().First());
            hallazgos.Add($"Mayor oportunidad hoy en Volumen: {vendVentas.Count} vendedores con presupuesto asignado — el de mejor alcance es {mv.nombre} ({mv.alcancePct:0.0}%).");
        }

        return new
        {
            diaSemana,
            hallazgos,
            top5VentasMejores, top5VentasPeores, top5ActivacionesMejores = top5ActMejores, top5ActivacionesPeores = top5ActPeores,
        };
    }

    // Pensamiento del día — cacheado por fecha (PensamientoDelDia_RM). Si no hay fila de
    // hoy: banco local rotado por día del año (PensamientoDelDiaBanco_RM) — nunca bloquea
    // el reporte. La fuente EXTERNA vía web (pedida por el usuario) se resuelve en el
    // FRONT (fetch con timeout corto a una API pública de frases) y se guarda aquí con
    // POST /apipdfpres/pensamiento-del-dia cuando tiene éxito — así el backend nunca
    // depende de que el servidor tenga salida a internet para poder generar el reporte.
    private static object ObtenerPensamientoDelDia(SqlConnection cn, DateTime fecha)
    {
        try
        {
            using (var cmd = new SqlCommand("SELECT Frase, Autor, Fuente FROM PensamientoDelDia_RM WITH (NOLOCK) WHERE Fecha=@f", cn) { CommandTimeout = 15 })
            {
                cmd.Parameters.AddWithValue("@f", fecha.Date);
                using var rd = cmd.ExecuteReader();
                if (rd.Read()) return new { frase = rd.GetString(0), autor = rd.IsDBNull(1) ? null : rd.GetString(1), fuente = rd.IsDBNull(2) ? null : rd.GetString(2) };
            }
            // Sin fila de hoy todavía: banco local, con un ORDEN BARAJADO POR AÑO (no
            // secuencial ni "día del año % total") — pedido explícito del usuario
            // 2026-09-03 ("que no se repitan, por lo menos una cantidad considerada"):
            // antes con 49 frases el ciclo se repetía cada 49 días de forma mecánica y
            // predecible; ahora, con el banco ampliado a ~122
            // (SQL/PensamientoDelDiaBanco_RM_Ampliacion.sql) y un barajado Fisher-Yates
            // sembrado con el AÑO (mismo año → mismo orden, reproducible; año distinto →
            // orden distinto), el ciclo completo tarda ~122 días Y no sigue el mismo
            // patrón de un año a otro. Sigue siendo determinista (persiste como frase
            // oficial del día, ver abajo) — no es aleatorio en cada request.
            using (var cmdB = new SqlCommand("SELECT COUNT(*) FROM PensamientoDelDiaBanco_RM WITH (NOLOCK)", cn) { CommandTimeout = 15 })
            {
                var total = Convert.ToInt32(cmdB.ExecuteScalar() ?? 0);
                if (total == 0) return new { frase = "Cada día es una nueva oportunidad para crecer.", autor = (string?)null, fuente = "default" };
                var orden = Enumerable.Range(1, total).ToList();
                var rng = new Random(fecha.Year);
                for (int i = orden.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (orden[i], orden[j]) = (orden[j], orden[i]); }
                int idx1 = orden[(fecha.DayOfYear - 1) % total];
                using var cmdF = new SqlCommand("SELECT Frase, Autor FROM (SELECT Frase, Autor, ROW_NUMBER() OVER (ORDER BY BancoID) rn FROM PensamientoDelDiaBanco_RM) x WHERE rn=@idx", cn) { CommandTimeout = 15 };
                cmdF.Parameters.AddWithValue("@idx", idx1);
                using var rdF = cmdF.ExecuteReader();
                if (!rdF.Read()) return new { frase = "Cada día es una nueva oportunidad para crecer.", autor = (string?)null, fuente = "default" };
                var frase = rdF.GetString(0); var autor = rdF.IsDBNull(1) ? null : rdF.GetString(1);
                try
                {
                    using var cmdIns = new SqlCommand("INSERT INTO PensamientoDelDia_RM (Fecha,Frase,Autor,Fuente) VALUES (@f,@fr,@a,'banco-local')", cn) { CommandTimeout = 15 };
                    cmdIns.Parameters.AddWithValue("@f", fecha.Date); cmdIns.Parameters.AddWithValue("@fr", frase); cmdIns.Parameters.AddWithValue("@a", (object?)autor ?? DBNull.Value);
                    cmdIns.ExecuteNonQuery();
                }
                catch { /* alguien más ya la insertó primero, o la tabla no existe todavía — no bloquea la respuesta */ }
                return new { frase, autor, fuente = "banco-local" };
            }
        }
        catch { return new { frase = "Cada día es una nueva oportunidad para crecer.", autor = (string?)null, fuente = "default" }; } // tablas aún no creadas por TI — nunca bloquea el reporte
    }

    // ── POST /apipdfpres/pensamiento-del-dia ────────────────────────────────────
    // El front intenta una fuente externa vía web (API pública de frases); si tiene
    // éxito, la guarda aquí como la frase oficial de HOY (si ya hay una — otro usuario
    // llegó primero — no se pisa, gana la primera). Si el front no puede alcanzar la
    // fuente externa (sin salida a internet, CORS, timeout, etc.), simplemente no llama
    // este endpoint y el reporte sigue con el banco local — nunca bloquea nada.
    public sealed record PensamientoBody(string Frase, string? Autor);
    [HttpPost("pensamiento-del-dia")]
    public IActionResult GuardarPensamientoDelDia([FromBody] PensamientoBody body)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(body?.Frase)) return BadRequest(new { ok = false, error = "Falta 'Frase'" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            using var cmd = new SqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM PensamientoDelDia_RM WHERE Fecha=CAST(GETDATE() AS date))
  INSERT INTO PensamientoDelDia_RM (Fecha,Frase,Autor,Fuente) VALUES (CAST(GETDATE() AS date),@fr,@a,'web')", cn) { CommandTimeout = 15 };
            cmd.Parameters.AddWithValue("@fr", body.Frase);
            cmd.Parameters.AddWithValue("@a", (object?)body.Autor ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); } // no crítico — el front ya mostró la frase igual
    }

    private static Dictionary<int, string> ObtenerNombresCat1(SqlConnection cn)
    {
        var d = new Dictionary<int, string>();
        using var cmd = new SqlCommand("SELECT Cat1ID, Cat1Descripcion FROM Categoria1 WITH (NOLOCK)", cn) { CommandTimeout = 30 };
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) d[rd.GetInt32(0)] = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim();
        return d;
    }
    private static Dictionary<int, string> ObtenerNombresCat3(SqlConnection cn)
    {
        var d = new Dictionary<int, string>();
        using var cmd = new SqlCommand("SELECT Cat3ID, Cat3Descripcion FROM Categoria3 WITH (NOLOCK)", cn) { CommandTimeout = 30 };
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) d[rd.GetInt32(0)] = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim();
        return d;
    }

    // ── Destinatarios de "Cierres" (correo/WhatsApp) — solo Gerencia ───────────
    private bool EsGerencia(SqlConnection cn)
    {
        var login = HttpContext.Session.GetString("user_login") ?? "";
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.SupervisionUsuarioMapa_RM WHERE UsuInicioSesion=@u AND Rol='Gerencia' AND Activo=1", cn) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@u", login);
        try { return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0; } catch { return false; }
    }

    [HttpGet("cierres/destinatarios")]
    public IActionResult ListarDestinatarios()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var items = new List<object>();
            try
            {
                using var cmd = new SqlCommand("SELECT DesID,Nombre,Email,Telefono,Activo,ReporteCierreDiario,ReporteCierreSemanal,ReporteCierreMensual FROM dbo.CierreDiarioDestinatarios_RM WITH (NOLOCK) ORDER BY Nombre", cn) { CommandTimeout = 30 };
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    items.Add(new
                    {
                        desId = rd.GetInt32(0), nombre = rd.GetString(1), email = rd.GetString(2), telefono = rd.IsDBNull(3) ? null : rd.GetString(3),
                        activo = rd.GetBoolean(4), reporteCierreDiario = rd.GetBoolean(5), reporteCierreSemanal = rd.GetBoolean(6), reporteCierreMensual = rd.GetBoolean(7)
                    });
            }
            catch (Exception ex) { return Ok(new { ok = true, items = new List<object>(), aviso = "Falta crear CierreDiarioDestinatarios_RM (SQL/CierreDiarioDestinatarios_RM.sql): " + ex.Message }); }
            return Ok(new { ok = true, items });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record DestinatarioReq(int DesId, string Nombre, string Email, string? Telefono, bool Activo, bool ReporteCierreDiario, bool ReporteCierreSemanal, bool ReporteCierreMensual);

    [HttpPost("cierres/destinatarios")]
    public IActionResult GuardarDestinatario([FromBody] DestinatarioReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(req?.Nombre) || string.IsNullOrWhiteSpace(req?.Email)) return BadRequest(new { ok = false, error = "Faltan Nombre/Email" });
        try
        {
            using var cn = NewConn(); cn.Open();
            if (!EsGerencia(cn)) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede administrar destinatarios." });
            using var cmd = new SqlCommand(req.DesId > 0 ? @"
UPDATE dbo.CierreDiarioDestinatarios_RM SET Nombre=@n,Email=@e,Telefono=@t,Activo=@a,ReporteCierreDiario=@rd,ReporteCierreSemanal=@rs,ReporteCierreMensual=@rm,UsuInicioSesion=@u,FechaActualizacion=GETDATE()
WHERE DesID=@id" : @"
INSERT INTO dbo.CierreDiarioDestinatarios_RM (Nombre,Email,Telefono,Activo,ReporteCierreDiario,ReporteCierreSemanal,ReporteCierreMensual,UsuInicioSesion)
VALUES (@n,@e,@t,@a,@rd,@rs,@rm,@u)", cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@n", req.Nombre);
            cmd.Parameters.AddWithValue("@e", req.Email);
            cmd.Parameters.AddWithValue("@t", (object?)req.Telefono ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@a", req.Activo);
            cmd.Parameters.AddWithValue("@rd", req.ReporteCierreDiario);
            cmd.Parameters.AddWithValue("@rs", req.ReporteCierreSemanal);
            cmd.Parameters.AddWithValue("@rm", req.ReporteCierreMensual);
            cmd.Parameters.AddWithValue("@u", HttpContext.Session.GetString("user_login") ?? "");
            if (req.DesId > 0) cmd.Parameters.AddWithValue("@id", req.DesId);
            cmd.ExecuteNonQuery();
            return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = "Falta crear CierreDiarioDestinatarios_RM (SQL/CierreDiarioDestinatarios_RM.sql): " + ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ── Reportes Programados (generaliza el envío por correo) ──────────────────
    // Pedido explícito del usuario 2026-09-03, tras probar la 1ª versión (que solo
    // mandaba "Cierre Diario" a las 6pm con filtros por defecto — "sugiero que esta
    // herramienta la pongamos en cada base de los reportes... con un botón guardar
    // para enviar... y decidir cuál reporte le envío a quién"). Reemplaza el diseño
    // anterior (columnas fijas ReporteCierreDiario/Semanal/Mensual en la libreta) por
    // N programaciones (`ReportesProgramados_RM`), cada una con su propio Reporte +
    // FiltrosJSON + HoraEnvio + lista de destinatarios (`ReportesProgramadosDestinatarios_RM`).
    // ══════════════════════════════════════════════════════════════════════════

    // Dispatcher: arma el reporte pedido, con SUS filtros guardados. Las fechas
    // SIEMPRE se recalculan aquí (nunca se guardan fijas en FiltrosJSON) — para que
    // una programación diaria/semanal no quede con fechas viejas.
    internal static object GenerarReporte(SqlConnection cn, string reporte, string? suplidor, string? categoria, string? centros, string? canales, string? supervisores, string? divisiones, string? vendedores)
    {
        if (reporte == "cierres_cierre_diario")
            return GenerarCierreDiario(cn, DateTime.Today, suplidor, categoria, centros, canales, divisiones, supervisores, vendedores);
        if (reporte == "gobierno_comercial_ventas_proyectadas")
        {
            var hoy = DateTime.Today; var desde = hoy.AddDays(1); var hasta = desde.AddDays(6); // misma "próxima semana" que ppNextWeekRange() en el front
            return GenerarProyeccionVentas(cn, desde, hasta, centros, canales, supervisores, vendedores, suplidor, categoria);
        }
        throw new Exception($"Tipo de reporte desconocido: {reporte}");
    }

    // ── GET /apipdfpres/programados ──────────────────────────────────────────────
    [HttpGet("programados")]
    public IActionResult ListarProgramados()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var items = new List<object>();
            try
            {
                using var cmd = new SqlCommand(@"
SELECT p.ProgID, p.Reporte, p.Nombre, p.FiltrosJSON, p.HoraEnvio, p.Activo, p.UltimoEnvio, p.UltimoError,
       (SELECT COUNT(*) FROM dbo.ReportesProgramadosDestinatarios_RM d WHERE d.ProgID=p.ProgID) NumDest,
       (SELECT STRING_AGG(CAST(d2.DesID AS varchar(10)),',') FROM dbo.ReportesProgramadosDestinatarios_RM d2 WHERE d2.ProgID=p.ProgID) DesIds
FROM dbo.ReportesProgramados_RM p WITH (NOLOCK) ORDER BY p.HoraEnvio, p.Nombre", cn) { CommandTimeout = 30 };
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    items.Add(new
                    {
                        progId = rd.GetInt32(0), reporte = rd.GetString(1), nombre = rd.GetString(2),
                        filtros = rd.IsDBNull(3) ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(rd.GetString(3)),
                        horaEnvio = rd.GetString(4), activo = rd.GetBoolean(5),
                        ultimoEnvio = rd.IsDBNull(6) ? null : rd.GetDateTime(6).ToString("yyyy-MM-dd HH:mm"),
                        ultimoError = rd.IsDBNull(7) ? null : rd.GetString(7),
                        numDestinatarios = rd.GetInt32(8),
                        desIds = rd.IsDBNull(9) ? new List<int>() : rd.GetString(9).Split(',').Select(int.Parse).ToList(),
                    });
            }
            catch (Exception ex) { return Ok(new { ok = true, items = new List<object>(), aviso = "Falta crear ReportesProgramados_RM (SQL/ReportesProgramados_RM.sql): " + ex.Message }); }
            return Ok(new { ok = true, items });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record ProgramadoReq(int ProgId, string Reporte, string Nombre, JsonElement Filtros, string HoraEnvio, bool Activo, List<int> Destinatarios);

    // Valida y normaliza la lista de horas de una programación — pedido explícito
    // 2026-09-03: "permitir que el mismo reporte se envíe en varias horas,
    // separadas por coma" (ej. avances 10:00/12:00/3:00 PM + cierre 5:15 PM, mismo
    // filtro y destinatarios). Acepta "18:00" o "10:00,12:00,15:00,17:15"; cada
    // parte se valida como hora real, se normaliza a HH:mm, se deduplica y se
    // ordena — para que el scheduler no tenga que lidiar con formatos raros.
    private static (bool ok, string horas, string? error) NormalizarHoras(string? crudo)
    {
        var partes = (crudo ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length == 0) return (false, "", "Falta la hora de envío.");
        var horas = new SortedSet<TimeSpan>();
        foreach (var p in partes)
        {
            if (!TimeSpan.TryParse(p, out var ts) || ts < TimeSpan.Zero || ts >= TimeSpan.FromDays(1))
                return (false, "", $"Hora inválida: '{p}' (usa HH:mm, ej. 10:00 o 10:00,12:00,15:00,17:15).");
            horas.Add(new TimeSpan(ts.Hours, ts.Minutes, 0));
        }
        return (true, string.Join(",", horas.Select(h => h.ToString(@"hh\:mm"))), null);
    }

    // ── POST /apipdfpres/programados ─────────────────────────────────────────────
    [HttpPost("programados")]
    public IActionResult GuardarProgramado([FromBody] ProgramadoReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(req?.Reporte) || string.IsNullOrWhiteSpace(req?.Nombre) || string.IsNullOrWhiteSpace(req?.HoraEnvio))
            return BadRequest(new { ok = false, error = "Faltan Reporte/Nombre/HoraEnvio" });
        var (horasOk, horasNormalizadas, horasError) = NormalizarHoras(req.HoraEnvio);
        if (!horasOk) return BadRequest(new { ok = false, error = horasError });
        try
        {
            using var cn = NewConn(); cn.Open();
            if (!EsGerencia(cn)) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede programar envíos." });
            using var tx = cn.BeginTransaction();
            int progId;
            using (var cmd = new SqlCommand(req.ProgId > 0 ? @"
UPDATE dbo.ReportesProgramados_RM SET Reporte=@rep,Nombre=@n,FiltrosJSON=@f,HoraEnvio=@h,Activo=@a,UsuInicioSesion=@u,FechaActualizacion=GETDATE()
WHERE ProgID=@id; SELECT @id;" : @"
INSERT INTO dbo.ReportesProgramados_RM (Reporte,Nombre,FiltrosJSON,HoraEnvio,Activo,UsuInicioSesion)
OUTPUT INSERTED.ProgID VALUES (@rep,@n,@f,@h,@a,@u)", cn, tx))
            {
                cmd.Parameters.AddWithValue("@rep", req.Reporte);
                cmd.Parameters.AddWithValue("@n", req.Nombre);
                cmd.Parameters.AddWithValue("@f", req.Filtros.GetRawText());
                cmd.Parameters.AddWithValue("@h", horasNormalizadas);
                cmd.Parameters.AddWithValue("@a", req.Activo);
                cmd.Parameters.AddWithValue("@u", HttpContext.Session.GetString("user_login") ?? "");
                if (req.ProgId > 0) { cmd.Parameters.AddWithValue("@id", req.ProgId); progId = req.ProgId; cmd.ExecuteNonQuery(); }
                else progId = (int)cmd.ExecuteScalar()!;
            }
            using (var del = new SqlCommand("DELETE FROM dbo.ReportesProgramadosDestinatarios_RM WHERE ProgID=@id", cn, tx))
            { del.Parameters.AddWithValue("@id", progId); del.ExecuteNonQuery(); }
            foreach (var desId in req.Destinatarios ?? new List<int>())
            {
                using var ins = new SqlCommand("INSERT INTO dbo.ReportesProgramadosDestinatarios_RM (ProgID,DesID) VALUES (@p,@d)", cn, tx);
                ins.Parameters.AddWithValue("@p", progId); ins.Parameters.AddWithValue("@d", desId);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
            return Ok(new { ok = true, progId });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = "Falta crear ReportesProgramados_RM (SQL/ReportesProgramados_RM.sql): " + ex.Message }); }
    }

    [HttpPost("programados/eliminar")]
    public IActionResult EliminarProgramado([FromQuery] int id)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            if (!EsGerencia(cn)) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede programar envíos." });
            using var tx = cn.BeginTransaction();
            using (var d = new SqlCommand("DELETE FROM dbo.ReportesProgramadosDestinatarios_RM WHERE ProgID=@id", cn, tx)) { d.Parameters.AddWithValue("@id", id); d.ExecuteNonQuery(); }
            using (var p = new SqlCommand("DELETE FROM dbo.ReportesProgramados_RM WHERE ProgID=@id", cn, tx)) { p.Parameters.AddWithValue("@id", id); p.ExecuteNonQuery(); }
            tx.Commit();
            return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apipdfpres/programados/enviar-ahora?id= ───────────────────────────
    // Dispara AHORA MISMO, con un clic de Gerencia, la programación #id — mismo
    // código que usará el reloj a su hora (ReportesProgramadosSchedulerService),
    // solo que disparado por la persona en vez de por el reloj. Sirve para probar
    // en vivo sin esperar, y también como "reenviar ahora" si un envío falló.
    // Es LA PERSONA quien dispara el envío real (con su propio clic) — así no hace
    // falta que el asistente pida permiso para enviar, ya lo dio al presionar el botón.
    [HttpPost("programados/enviar-ahora")]
    public async Task<IActionResult> EnviarProgramadoAhora([FromQuery] int id)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            if (!EsGerencia(cn)) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede disparar el envío." });
            var r = await ReportesProgramadosSchedulerService.EnviarProgramado(cn, config, log, id);
            return Ok(new { ok = r.Ok, destinatarios = r.Destinatarios, error = r.Motivo });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apipdfpres/enviar-html ─────────────────────────────────────────────
    // Envío AD-HOC (pedido explícito 2026-09-03: "¿cómo puedo enviar a x personas un
    // reporte que tengo en pantalla, sin complicaciones si no está configurado?").
    // No regenera nada — manda TAL CUAL el HTML que el navegador ya tiene renderizado
    // en pantalla (con los filtros que la persona ya eligió), a direcciones sueltas
    // escritas en el momento (no hace falta tenerlas guardadas en la libreta antes).
    public sealed record EnviarHtmlReq(string Html, string Asunto, List<string> Destinatarios);
    [HttpPost("enviar-html")]
    public IActionResult EnviarHtmlAdHoc([FromBody] EnviarHtmlReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(req?.Html)) return BadRequest(new { ok = false, error = "Falta el contenido del reporte" });
        var destinatarios = (req.Destinatarios ?? new List<string>())
            .Select(e => e.Trim()).Where(e => e.Length > 0 && e.Contains('@') && e.Contains('.')).Distinct().ToList();
        if (destinatarios.Count == 0) return BadRequest(new { ok = false, error = "No hay direcciones de correo válidas" });
        try
        {
            var (ok, motivo) = ReportesProgramadosSchedulerService.EnviarPorCorreoDirecto(config, destinatarios, req.Asunto ?? "Reporte — MovilBusiness Dashboard", req.Html);
            return Ok(new { ok, error = motivo, destinatarios = destinatarios.Count });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apipdfpres/whatsapp/status ──────────────────────────────────────────
    // Proxy delgado hacia el sidecar Node "WhatsAppSender" (Baileys) — 2026-09-19. El
    // navegador nunca llama directo a localhost:5099 (ese puerto solo es alcanzable desde
    // el propio servidor); esto funciona igual detrás de IIS. Se usa para mostrar el QR
    // a escanear una sola vez (Supervisión → Cierres → botón WhatsApp).
    private static readonly HttpClient _whatsAppStatusHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    [HttpGet("whatsapp/status")]
    public async Task<IActionResult> WhatsAppStatus()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        bool activo = bool.TryParse(config["Smtp:WhatsApp:Activo"], out var a) && a;
        if (!activo) return Ok(new { ok = true, activo = false, conectado = false, qr = (string?)null });
        var sidecarUrl = (config["Smtp:WhatsApp:SidecarUrl"] ?? "http://localhost:5099").TrimEnd('/');
        try
        {
            var resp = await _whatsAppStatusHttp.GetStringAsync($"{sidecarUrl}/status");
            using var doc = JsonDocument.Parse(resp);
            var root = doc.RootElement;
            return Ok(new {
                ok = true, activo = true,
                conectado = root.TryGetProperty("conectado", out var c) && c.GetBoolean(),
                qr = root.TryGetProperty("qr", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() : null,
            });
        }
        catch (Exception ex) { return Ok(new { ok = true, activo = true, conectado = false, qr = (string?)null, aviso = "No se pudo contactar el sidecar WhatsAppSender: " + ex.Message }); }
    }

    // ── GET /apipdfpres/whatsapp/grupos — proxy hacia el sidecar, lista los grupos del
    // número YA vinculado (para el select "Grupo o Persona" del botón "Enviar por WhatsApp").
    [HttpGet("whatsapp/grupos")]
    public async Task<IActionResult> WhatsAppGrupos()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        var sidecarUrl = (config["Smtp:WhatsApp:SidecarUrl"] ?? "http://localhost:5099").TrimEnd('/');
        try
        {
            var resp = await _whatsAppStatusHttp.GetAsync($"{sidecarUrl}/grupos");
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return Ok(new { ok = false, error = "El sidecar respondió " + resp.StatusCode });
            using var doc = JsonDocument.Parse(body);
            var grupos = doc.RootElement.TryGetProperty("grupos", out var g)
                ? g.EnumerateArray().Select(x => (object)new { id = x.GetProperty("id").GetString(), nombre = x.GetProperty("nombre").GetString() }).ToList()
                : new List<object>();
            return Ok(new { ok = true, grupos });
        }
        catch (Exception ex) { return Ok(new { ok = false, error = "No se pudo contactar el sidecar WhatsAppSender: " + ex.Message }); }
    }

    // ── POST /apipdfpres/whatsapp/desvincular — cierra la sesión actual del sidecar (borra
    // su auth/) para poder escanear un número distinto. Pedido explícito 2026-09-20.
    [HttpPost("whatsapp/desvincular")]
    public async Task<IActionResult> WhatsAppDesvincular()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        using var cn = NewConn(); cn.Open();
        if (!EsGerencia(cn)) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede desvincular WhatsApp." });
        var sidecarUrl = (config["Smtp:WhatsApp:SidecarUrl"] ?? "http://localhost:5099").TrimEnd('/');
        try
        {
            using var resp = await _whatsAppStatusHttp.PostAsync($"{sidecarUrl}/logout", new StringContent(""));
            return Ok(new { ok = resp.IsSuccessStatusCode });
        }
        catch (Exception ex) { return Ok(new { ok = false, error = "No se pudo contactar el sidecar WhatsAppSender: " + ex.Message }); }
    }

    // ── POST /apipdfpres/whatsapp/enviar-ahora — envío AD-HOC del Cierre Diario por
    // WhatsApp (imagen compacta), a UN contacto (Telefono) o UN grupo (GrupoId), con los
    // mismos filtros que el reporte que la persona tiene en pantalla. Pedido explícito
    // 2026-09-20: "en principio quiero probarlo enviando on demand". Regenera el reporte
    // del lado del servidor (mismo motor que /cierre-diario) en vez de confiar en el JSON
    // que ya tiene el navegador — una sola fuente de verdad, mismo criterio que el envío
    // programado.
    // 'Puntos' = lista separada por coma con las claves marcadas en el checklist del modal
    // (ventasDia/activacionesDia/ventasMes/activacionesMes/resumenEjecutivo/pensamiento) —
    // pedido explícito 2026-09-20 ("decidir cual de los puntos se envían"). 'Mensaje' es el
    // texto de introducción editable (vacío = usa el default).
    public sealed record WhatsAppEnviarReq(string Fecha, string? Suplidor, string? Categoria, string? Centros, string? Canales, string? Divisiones, string? Supervisores, string? Vendedores, string? Telefono, string? GrupoId, string? Puntos, string? Mensaje);
    private static readonly HttpClient _whatsAppEnviarHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    [HttpPost("whatsapp/enviar-ahora")]
    public async Task<IActionResult> WhatsAppEnviarAhora([FromBody] WhatsAppEnviarReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(req?.Telefono) && string.IsNullOrWhiteSpace(req?.GrupoId))
            return BadRequest(new { ok = false, error = "Indica un teléfono o un grupo." });
        if (!DateTime.TryParse(req!.Fecha, out var fechaDt)) return BadRequest(new { ok = false, error = "Fecha inválida" });
        bool activo = bool.TryParse(config["Smtp:WhatsApp:Activo"], out var a) && a;
        if (!activo) return Ok(new { ok = false, error = "WhatsApp está desactivado (Smtp:WhatsApp:Activo=false)." });
        try
        {
            using var cn = NewConn(); cn.Open();
            dynamic resultado = GenerarCierreDiario(cn, fechaDt, req.Suplidor, req.Categoria, req.Centros, req.Canales, req.Divisiones, req.Supervisores, req.Vendedores);
            var claves = (req.Puntos ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var puntos = new ReportesProgramadosSchedulerService.WhatsAppPuntos(
                VentasDia: claves.Contains("ventasDia"), ActivacionesDia: claves.Contains("activacionesDia"),
                VentasMes: claves.Contains("ventasMes"), ActivacionesMes: claves.Contains("activacionesMes"),
                ResumenEjecutivo: claves.Contains("resumenEjecutivo"), Pensamiento: claves.Contains("pensamiento"));
            string html = ReportesProgramadosSchedulerService.ArmarHtmlWhatsAppCierreDiario(resultado, puntos);
            string intro = string.IsNullOrWhiteSpace(req.Mensaje) ? ReportesProgramadosSchedulerService.MensajeIntroDefault : req.Mensaje;
            string cierre = "Feliz resto del día,\nAtentamente,\nGerencia Comercial,\nAM Comercial";
            var sidecarUrl = (config["Smtp:WhatsApp:SidecarUrl"] ?? "http://localhost:5099").TrimEnd('/');
            var body = JsonSerializer.Serialize(new { telefono = req.Telefono, grupoId = req.GrupoId, html, caption = cierre, textoIntro = intro });
            using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var resp = await _whatsAppEnviarHttp.PostAsync($"{sidecarUrl}/enviar", content);
            var respBody = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode) return Ok(new { ok = true });
            string? motivo = null;
            try { using var doc = JsonDocument.Parse(respBody); motivo = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null; } catch { }
            return Ok(new { ok = false, error = motivo ?? ("El sidecar respondió " + resp.StatusCode) });
        }
        // ex.ToString() (no solo ex.Message) mientras se depura el bug real de
        // RuntimeBinderException ("'double' does not contain a definition for 'HasValue'")
        // reportado 2026-09-20 — así la línea exacta se ve en el propio modal sin otra
        // ronda de "pégame la traza".
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.ToString() }); }
    }

    [HttpPost("cierres/destinatarios/eliminar")]
    public IActionResult EliminarDestinatario([FromQuery] int id)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            if (!EsGerencia(cn)) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede administrar destinatarios." });
            using var cmd = new SqlCommand("DELETE FROM dbo.CierreDiarioDestinatarios_RM WHERE DesID=@id", cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── Historial (auditoría de informes generados) ─────────────────────────────
    public class GuardarHistorialBody
    {
        public string Reporte { get; set; } = "";
        public JsonElement Filtros { get; set; }
        public JsonElement Resultado { get; set; }
    }

    [HttpPost("historial/guardar")]
    public IActionResult GuardarHistorial([FromBody] GuardarHistorialBody body)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(body?.Reporte)) return BadRequest(new { ok = false, error = "Falta 'reporte'" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            using var cmd = new SqlCommand(@"
INSERT INTO PdfPresHistorial_RM (PphReporte, PphUsuarioLogin, PphUsuarioNombre, PphFiltrosJSON, PphResultadoJSON)
OUTPUT INSERTED.PphID, INSERTED.PphFechaGeneracion
VALUES (@rep, @login, @nombre, @filtros, @resultado)", cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@rep", body.Reporte);
            cmd.Parameters.AddWithValue("@login", HttpContext.Session.GetString("user_login") ?? "");
            cmd.Parameters.AddWithValue("@nombre", (object?)HttpContext.Session.GetString("user_nombre") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@filtros", body.Filtros.GetRawText());
            cmd.Parameters.AddWithValue("@resultado", body.Resultado.GetRawText());
            using var rd = cmd.ExecuteReader();
            rd.Read();
            return Ok(new { ok = true, id = rd.GetInt32(0), fecha = rd.GetDateTime(1).ToString("yyyy-MM-dd HH:mm") });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = "No se pudo guardar en el historial: " + ex.Message }); }
    }

    [HttpGet("historial")]
    public IActionResult ListarHistorial([FromQuery] string? reporte)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            using var cmd = new SqlCommand(@"
SELECT TOP 100 PphID, PphReporte, PphUsuarioNombre, PphUsuarioLogin, PphFechaGeneracion, PphFiltrosJSON
FROM PdfPresHistorial_RM WITH (NOLOCK)
WHERE (@rep IS NULL OR PphReporte=@rep)
ORDER BY PphFechaGeneracion DESC", cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@rep", (object?)reporte ?? DBNull.Value);
            var items = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                items.Add(new
                {
                    id = rd.GetInt32(0),
                    reporte = rd.GetString(1),
                    usuarioNombre = rd.IsDBNull(2) ? rd.GetString(3) : rd.GetString(2),
                    fecha = rd.GetDateTime(4).ToString("yyyy-MM-dd HH:mm"),
                    filtros = JsonSerializer.Deserialize<JsonElement>(rd.GetString(5)),
                });
            return Ok(new { ok = true, items });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    [HttpGet("historial/{id:int}")]
    public IActionResult ObtenerHistorial(int id)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            using var cmd = new SqlCommand("SELECT PphReporte, PphUsuarioNombre, PphFechaGeneracion, PphFiltrosJSON, PphResultadoJSON FROM PdfPresHistorial_RM WITH (NOLOCK) WHERE PphID=@id", cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@id", id);
            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return NotFound(new { ok = false, error = "No existe" });
            return Ok(new
            {
                ok = true,
                reporte = rd.GetString(0),
                usuarioNombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                fecha = rd.GetDateTime(2).ToString("yyyy-MM-dd HH:mm"),
                filtros = JsonSerializer.Deserialize<JsonElement>(rd.GetString(3)),
                resultado = JsonSerializer.Deserialize<JsonElement>(rd.GetString(4)),
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }
}
