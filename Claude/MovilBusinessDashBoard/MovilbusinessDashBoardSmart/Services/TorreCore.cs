using Microsoft.Data.SqlClient;

namespace DashVisitas.Services;

// ── Núcleo compartido de Torre de Control ───────────────────────────────────
// Carga las métricas crudas por vendedor (hoy) y evalúa los monitores.
// Usado por ApiTorreController (monitor en vivo) y TorreSnapshotService (snapshots).
// Diseño: cada métrica es una consulta rápida y se unen en memoria por RepCodigo
// (unir todo en un solo SQL producía un mal plan de 14-27s; separadas ~0.5s).
public static class TorreCore
{
    // Parseo del código desde VwUltimaSincronizacion.Nombre = "CODIGO - Nombre"
    private const string SyncParse = "LTRIM(RTRIM(LEFT(Nombre, CHARINDEX(' - ', Nombre + ' - ')-1)))";

    // Monitores que, una vez ATENDIDOS (contactados), NO vuelven a encenderse el resto del día:
    // son un evento único de la jornada (el 1er pedido tardío ya pasó, no se repite). El resto SÍ
    // puede reactivarse tras el mute, con el reloj contando desde el contacto.
    public static readonly HashSet<string> UnaVezAlDia =
        new(StringComparer.OrdinalIgnoreCase) { "PRIMERA_VISITA_PEDIDO" };

    public class RepMetric
    {
        public string RepCodigo = "", RepNombre = "", Centro = "", SupCod = "", Supervisor = "", Division = "";
        public int? PrimVisMin, PrimPedMin, UltPedMin, PromSeg, AVisitar, Visitados, CliConPed, MinSinSync, FueraDia;
        public double? KmEntorno, Lat, Lng, CumplimientoPct;
        public bool CuadreAbierto, CuadreCerrado;
        public bool Reseteado;   // true = contadores recalculados desde la reactivación (post-contacto)

        // true = ya visitó todos sus clientes asignados del día → terminó su ruta.
        // Al terminar dejamos de medirlo en la torre (no genera alarmas de movimiento),
        // solo queda pendiente el cierre del cuadre. Cualquier pedido posterior al cierre
        // de su última visita ya no cuenta como "fuera de día".
        public bool Terminado => AVisitar is > 0 && (Visitados ?? 0) >= AVisitar.Value;
    }

    // Ventana horaria (desde/hasta) de la config _VENTANA_, default 08:20–16:00.
    public static (TimeSpan desde, TimeSpan hasta) LeerVentanaHoraria(SqlConnection cn)
    {
        var desde = new TimeSpan(8, 20, 0); var hasta = new TimeSpan(16, 0, 0);
        try
        {
            using var cmd = new SqlCommand("SELECT ParametrosJSON FROM dbo.TorreControlMonitores_RM WHERE Codigo='_VENTANA_'", cn);
            var v = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrWhiteSpace(v))
            {
                var p = System.Text.Json.JsonDocument.Parse(v).RootElement;
                if (p.TryGetProperty("desde", out var a) && TimeSpan.TryParse(a.GetString(), out var d)) desde = d;
                if (p.TryGetProperty("hasta", out var b) && TimeSpan.TryParse(b.GetString(), out var h)) hasta = h;
            }
        }
        catch { }
        return (desde, hasta);
    }

    // CUMPLIMIENTO = visitados / (a-visitar × fracción de la jornada transcurrida).
    // Mide si va al ritmo esperado según la hora (jornada = ventana desde→hasta).
    public static void CalcularCumplimiento(RepMetric m, TimeSpan desde, TimeSpan hasta, DateTime ahora)
    {
        if (m.AVisitar is not > 0) { m.CumplimientoPct = null; return; }
        double totalMin = (hasta - desde).TotalMinutes;
        double transc = (ahora.TimeOfDay - desde).TotalMinutes;
        if (transc < 1) transc = 1; if (transc > totalMin) transc = totalMin;
        double frac = totalMin > 0 ? transc / totalMin : 1;
        double debeAhora = m.AVisitar.Value * frac;               // clientes que debería llevar a esta hora
        m.CumplimientoPct = debeAhora > 0 ? 100.0 * (m.Visitados ?? 0) / debeAhora : null;
    }

    // Representantes SIN alerta (config fila _REPS_OFF_, ParametrosJSON {"reps":["132",...]})
    public static HashSet<string> LeerRepsOff(SqlConnection cn)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cmd = new SqlCommand("SELECT ParametrosJSON FROM dbo.TorreControlMonitores_RM WHERE Codigo='_REPS_OFF_'", cn);
            var v = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrWhiteSpace(v))
            {
                var p = System.Text.Json.JsonDocument.Parse(v).RootElement;
                if (p.TryGetProperty("reps", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var e in arr.EnumerateArray()) { var s = e.GetString(); if (!string.IsNullOrEmpty(s)) set.Add(s); }
            }
        }
        catch { }
        return set;
    }

    // Alertas ATENDIDAS hoy por vendedor (códigos en Indicadores {"atendidas":[...]}).
    // Una alerta atendida no vuelve a encenderse el resto del día.
    public static Dictionary<string, HashSet<string>> LeerAtendidasHoy(SqlConnection cn)
    {
        var d = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cmd = new SqlCommand(@"SELECT RepCodigo, Indicadores FROM dbo.TorreControlContactos_RM WITH (NOLOCK)
                                             WHERE FechaContacto >= CAST(CAST(GETDATE() AS date) AS datetime) AND Indicadores IS NOT NULL", cn) { CommandTimeout = 30 };
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var rep = rd.GetString(0); var indic = rd.IsDBNull(1) ? null : rd.GetString(1);
                if (string.IsNullOrWhiteSpace(indic)) continue;
                try
                {
                    var p = System.Text.Json.JsonDocument.Parse(indic).RootElement;
                    if (p.TryGetProperty("atendidas", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        if (!d.TryGetValue(rep, out var set)) { set = new(StringComparer.OrdinalIgnoreCase); d[rep] = set; }
                        foreach (var e in arr.EnumerateArray()) { var cc = e.GetString(); if (!string.IsNullOrEmpty(cc)) set.Add(cc); }
                    }
                }
                catch { }
            }
        }
        catch { }
        return d;
    }

    // Minutos de "mute" tras un contacto (config en fila _VENTANA_, default 60)
    public static int LeerMuteMin(SqlConnection cn)
    {
        try
        {
            using var cmd = new SqlCommand("SELECT ParametrosJSON FROM dbo.TorreControlMonitores_RM WHERE Codigo='_VENTANA_'", cn);
            var v = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrWhiteSpace(v))
            {
                var p = System.Text.Json.JsonDocument.Parse(v).RootElement;
                if (p.TryGetProperty("muteMin", out var m) && m.TryGetInt32(out var mm) && mm > 0) return mm;
            }
        }
        catch { }
        return 60;
    }

    // Estado de "mute" de UN vendedor puntual (mismo criterio que ApiTorreController/TorreSnapshotService:
    // último contacto de HOY para ese rep, ventana = LeerMuteMin). Reutilizado por Supervisión Virtual
    // para que, al guardar un comentario, ese vendedor quede en pausa el mismo tiempo que en Torre.
    public static (bool enMute, DateTime? ultimoContacto, int muteMin) EstadoMute(SqlConnection cn, string repCodigo)
    {
        int muteMin = LeerMuteMin(cn);
        using var cmd = new SqlCommand(@"SELECT MAX(FechaContacto) FROM dbo.TorreControlContactos_RM WITH (NOLOCK)
                                         WHERE RepCodigo=@r AND FechaContacto >= CAST(CAST(GETDATE() AS date) AS datetime)", cn);
        cmd.Parameters.AddWithValue("@r", repCodigo);
        var v = cmd.ExecuteScalar();
        if (v == null || v is DBNull) return (false, null, muteMin);
        var lc = (DateTime)v;
        bool enMute = (DateTime.Now - lc).TotalMinutes < muteMin;
        return (enMute, lc, muteMin);
    }

    // Reinicia SOLO los contadores acumulables (pedidos fuera de día) desde 'desde'.
    // El 1er pedido/visita, el tiempo y la efectividad son del día y NO se recalculan
    // (recalcularlos mostraría "1er pedido tarde" a quien lo hizo temprano en la mañana).
    public static void RecomputarDesde(SqlConnection cn, RepMetric m, DateTime desde)
    {
        using (var cmd = new SqlCommand(@"
SELECT COUNT(*) FROM (
  SELECT DISTINCT p.CliID FROM VWPedidos p WITH (NOLOCK)
  WHERE p.RepCodigo=@rep AND p.PedFecha>=@d
    AND NOT EXISTS (SELECT 1 FROM RutaVisitasFechaHistoricos ru WITH (NOLOCK)
                    WHERE ru.RepCodigo=p.RepCodigo AND ru.CliID=p.CliID AND CAST(ru.RutFecha AS date)=CAST(@d AS date))
) x", cn) { CommandTimeout = 30 })
        {
            cmd.Parameters.AddWithValue("@rep", m.RepCodigo); cmd.Parameters.AddWithValue("@d", desde);
            m.FueraDia = Convert.ToInt32(cmd.ExecuteScalar());
        }
        m.Reseteado = true;
    }

    public class Monitor
    {
        public int MonID; public string Codigo = "", Tipo = "", Nombre = "", Ambito = "vendedor";
        public double? Umbral; public string? Direccion, ParametrosJSON, Color; public int Severidad, Orden;
    }

    public class Celda
    {
        public string Codigo = ""; public double? Valor; public string? Texto, Detalle;
        public bool Alarma; public int Severidad;
    }

    // ── Cargar monitores activos y vigentes ─────────────────────────────────
    public static List<Monitor> CargarMonitores(SqlConnection cn, bool soloActivos = true)
    {
        var lst = new List<Monitor>();
        var w = soloActivos
            ? "WHERE Activo=1 AND (VigenciaDesde IS NULL OR VigenciaDesde<=CAST(GETDATE() AS date)) AND (VigenciaHasta IS NULL OR VigenciaHasta>=CAST(GETDATE() AS date))"
            : "";
        using var cmd = new SqlCommand($@"
SELECT MonID, Codigo, Tipo, Nombre, Ambito, Umbral, Direccion, ParametrosJSON, Severidad, Orden, Color
FROM dbo.TorreControlMonitores_RM {w} ORDER BY Orden, MonID", cn) { CommandTimeout = 30 };
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            lst.Add(new Monitor
            {
                MonID = rd.GetInt32(0), Codigo = rd.GetString(1), Tipo = rd.GetString(2), Nombre = rd.GetString(3),
                Ambito = rd.IsDBNull(4) ? "vendedor" : rd.GetString(4),
                Umbral = rd.IsDBNull(5) ? null : Convert.ToDouble(rd.GetValue(5)),
                Direccion = rd.IsDBNull(6) ? null : rd.GetString(6),
                ParametrosJSON = rd.IsDBNull(7) ? null : rd.GetString(7),
                Severidad = rd.IsDBNull(8) ? 1 : rd.GetInt32(8),
                Orden = rd.IsDBNull(9) ? 0 : rd.GetInt32(9),
                Color = rd.IsDBNull(10) ? null : rd.GetString(10)
            });
        return lst;
    }

    // ── Cargar métricas crudas por vendedor (hoy) ───────────────────────────
    // repFilter: fragmento AND opcional para filtrar reps (centros/sup/etc.).
    public static Dictionary<string, RepMetric> CargarMetricas(SqlConnection cn, string repFilter, Dictionary<string, object> parms)
    {
        const string HOY = "DECLARE @hoy datetime=CAST(CAST(GETDATE() AS date) AS datetime), @man datetime=DATEADD(DAY,1,CAST(CAST(GETDATE() AS date) AS datetime));";
        var reps = new Dictionary<string, RepMetric>(StringComparer.OrdinalIgnoreCase);

        void AddParms(SqlCommand c) { foreach (var (k, v) in parms) c.Parameters.AddWithValue(k, v); }

        // 1) Población: reps activos no-sup con ruta o visita hoy
        using (var cmd = new SqlCommand($@"{HOY}
SELECT r.RepCodigo, r.RepNombre, ISNULL(cd.CedDescripcion,r.CedCodigo) Centro,
       ISNULL(r.RepSupervisor,'') SupCod, ISNULL(sup.RepNombre,r.RepSupervisor) Supervisor, ISNULL(ra.RepTipe,'') Division
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor' {repFilter}
  AND r.RepCodigo IN (SELECT RepCodigo FROM RutaVisitasFechaHistoricos WITH (NOLOCK) WHERE RutFecha>=@hoy AND RutFecha<@man
                      UNION SELECT RepCodigo FROM Visitas WITH (NOLOCK) WHERE VisFechaEntrada>=@hoy AND VisFechaEntrada<@man)", cn) { CommandTimeout = 60 })
        {
            AddParms(cmd);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var m = new RepMetric
                {
                    RepCodigo = rd.GetString(0), RepNombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    Centro = rd.IsDBNull(2) ? "" : rd.GetString(2), SupCod = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    Supervisor = rd.IsDBNull(4) ? "" : rd.GetString(4), Division = rd.IsDBNull(5) ? "" : rd.GetString(5)
                };
                reps[m.RepCodigo] = m;
            }
        }
        if (reps.Count == 0) return reps;

        // 2) Visitas de hoy: 1ra visita, 1er pedido, tiempo promedio
        using (var cmd = new SqlCommand($@"{HOY}
SELECT v.RepCodigo,
       MIN(DATEDIFF(MINUTE,@hoy,v.VisFechaEntrada)) PrimVisMin,
       MIN(CASE WHEN v.VisEstatus=2 THEN DATEDIFF(MINUTE,@hoy,v.VisFechaEntrada) END) PrimPedMin,
       MAX(CASE WHEN v.VisEstatus=2 THEN DATEDIFF(MINUTE,@hoy,v.VisFechaEntrada) END) UltPedMin,
       AVG(CASE WHEN v.VisFechaSalida IS NOT NULL THEN DATEDIFF(SECOND,v.VisFechaEntrada,v.VisFechaSalida) END) PromSeg
FROM Visitas v WITH (NOLOCK) WHERE v.VisFechaEntrada>=@hoy AND v.VisFechaEntrada<@man GROUP BY v.RepCodigo", cn) { CommandTimeout = 60 })
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
                if (reps.TryGetValue(rd.GetString(0), out var m))
                {
                    m.PrimVisMin = rd.IsDBNull(1) ? null : rd.GetInt32(1);
                    m.PrimPedMin = rd.IsDBNull(2) ? null : rd.GetInt32(2);
                    m.UltPedMin = rd.IsDBNull(3) ? null : rd.GetInt32(3);
                    m.PromSeg = rd.IsDBNull(4) ? null : rd.GetInt32(4);
                }

        // 3) Efectividad (ResumenVisitasRm)
        using (var cmd = new SqlCommand($@"{HOY}
SELECT RepCodigo, SUM(ISNULL(TotalAVisitar,0)) AVisitar, SUM(ISNULL(Visitados,0)) Visitados
FROM ResumenVisitasRm WITH (NOLOCK) WHERE FechaVisita>=@hoy AND FechaVisita<@man GROUP BY RepCodigo", cn) { CommandTimeout = 60 })
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
                if (reps.TryGetValue(rd.GetString(0), out var m))
                { m.AVisitar = rd.GetInt32(1); m.Visitados = rd.GetInt32(2); }

        // 3b) Clientes con pedido hoy (Efectividad = con pedido / visitados)
        using (var cmd2 = new SqlCommand($@"{HOY}
SELECT p.RepCodigo, COUNT(DISTINCT p.CliID) CliConPed
FROM VWPedidos p WITH (NOLOCK) WHERE p.PedFecha>=@hoy AND p.PedFecha<@man GROUP BY p.RepCodigo", cn) { CommandTimeout = 60 })
        using (var rd2 = cmd2.ExecuteReader())
            while (rd2.Read())
                if (reps.TryGetValue(rd2.GetString(0), out var m)) m.CliConPed = rd2.GetInt32(1);

        // 3c) Cuadre del día (abierto / cerrado)
        using (var cmd3 = new SqlCommand($@"{HOY}
SELECT c.RepCodigo, COUNT(*) N, MAX(CASE WHEN c.CuaFechaFin IS NOT NULL THEN 1 ELSE 0 END) Cerrado
FROM Cuadres c WITH (NOLOCK) WHERE c.CuaFechaInicio>=@hoy AND c.CuaFechaInicio<@man GROUP BY c.RepCodigo", cn) { CommandTimeout = 60 })
        using (var rd3 = cmd3.ExecuteReader())
            while (rd3.Read())
                if (reps.TryGetValue(rd3.GetString(0), out var m)) { m.CuadreAbierto = rd3.GetInt32(1) > 0; m.CuadreCerrado = rd3.GetInt32(2) == 1; }

        // 4) Sincronización (última pos + minutos sin sincronizar)
        using (var cmd = new SqlCommand($@"
SELECT {SyncParse} RepCodigo, DATEDIFF(MINUTE,RssFechaInicio,GETDATE()) MinSinSync, latitud, longitud
FROM VwUltimaSincronizacion", cn) { CommandTimeout = 60 })
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
            {
                var cod = rd.IsDBNull(0) ? "" : rd.GetString(0);
                if (reps.TryGetValue(cod, out var m))
                {
                    // Si el sello viene en el futuro (reloj del equipo adelantado) => acaban de sincronizar => 0
                    m.MinSinSync = rd.IsDBNull(1) ? null : Math.Max(0, rd.GetInt32(1));
                    m.Lat = rd.IsDBNull(2) ? null : Convert.ToDouble(rd.GetValue(2));
                    m.Lng = rd.IsDBNull(3) ? null : Convert.ToDouble(rd.GetValue(3));
                }
            }

        // 5) Fuera de entorno: km al cliente de ruta más cercano
        using (var cmd = new SqlCommand($@"{HOY}
;WITH Sync AS (SELECT {SyncParse} RepCodigo, latitud, longitud FROM VwUltimaSincronizacion WHERE latitud IS NOT NULL AND latitud<>0)
SELECT s.RepCodigo, MIN(geography::Point(s.latitud,s.longitud,4326).STDistance(geography::Point(c.CliLatitud,c.CliLongitud,4326)))/1000.0 Km
FROM Sync s
INNER JOIN RutaVisitasFechaHistoricos ru WITH (NOLOCK) ON ru.RepCodigo=s.RepCodigo AND ru.RutFecha>=@hoy AND ru.RutFecha<@man
INNER JOIN Clientes c WITH (NOLOCK) ON c.CliID=ru.CliID AND c.CliLatitud IS NOT NULL AND c.CliLatitud<>0
GROUP BY s.RepCodigo", cn) { CommandTimeout = 60 })
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
                if (reps.TryGetValue(rd.GetString(0), out var m))
                    m.KmEntorno = rd.IsDBNull(1) ? null : Convert.ToDouble(rd.GetValue(1));

        // 6) Pedidos fuera del día de visita
        using (var cmd = new SqlCommand($@"{HOY}
SELECT p.RepCodigo, COUNT(*) N
FROM (SELECT DISTINCT RepCodigo,CliID FROM VWPedidos WITH (NOLOCK) WHERE PedFecha>=@hoy AND PedFecha<@man) p
WHERE NOT EXISTS (SELECT 1 FROM RutaVisitasFechaHistoricos ru WITH (NOLOCK)
                  WHERE ru.RepCodigo=p.RepCodigo AND ru.CliID=p.CliID AND ru.RutFecha>=@hoy AND ru.RutFecha<@man)
GROUP BY p.RepCodigo", cn) { CommandTimeout = 60 })
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
                if (reps.TryGetValue(rd.GetString(0), out var m))
                    m.FueraDia = rd.GetInt32(1);

        return reps;
    }

    // ── Evaluar un monitor núcleo sobre un vendedor ─────────────────────────
    public static Celda Evaluar(Monitor mon, RepMetric m)
    {
        var c = new Celda { Codigo = mon.Codigo, Severidad = mon.Severidad };
        string hhmm(int? min) => min == null ? "—" : $"{min.Value / 60:D2}:{min.Value % 60:D2}";
        double u = mon.Umbral ?? 0;

        switch (mon.Codigo)
        {
            case "PRIMERA_VISITA_PEDIDO":
                // Un pedido tardío REAL es cuando ya hubo 1er pedido y fue > umbral (8:20).
                // "Aún no pidió" (null) NO es tardío en la mañana: solo alarma si ya es tarde en el día
                // (pasada la hora de gracia), donde no tener 1er pedido sí es un problema.
                c.Valor = m.PrimPedMin; c.Texto = hhmm(m.PrimPedMin);
                if (m.PrimPedMin != null) { c.Alarma = m.PrimPedMin > u; c.Detalle = $"1er pedido {c.Texto}"; }
                else
                {
                    int nowMin = (int)DateTime.Now.TimeOfDay.TotalMinutes;
                    const int gracia = 600; // 10:00
                    c.Alarma = nowMin >= gracia;
                    c.Detalle = c.Alarma ? "sin 1er pedido (pasadas las 10:00)" : "sin pedido aún";
                }
                break;
            case "TIEMPO_VISITA":
                if (m.PromSeg != null) { double min = m.PromSeg.Value / 60.0; c.Valor = Math.Round(min, 1); c.Texto = $"{min:0.#} min";
                    double lo = 3, hi = 40; try { if (mon.ParametrosJSON != null) { var p = System.Text.Json.JsonDocument.Parse(mon.ParametrosJSON).RootElement; if (p.TryGetProperty("minMin", out var a)) lo = a.GetDouble(); if (p.TryGetProperty("maxMin", out var b)) hi = b.GetDouble(); } } catch { }
                    c.Alarma = min < lo || min > hi; c.Detalle = $"visita prom. {min:0.#} min"; }
                else { c.Texto = "—"; }
                break;
            case "CUMPLIMIENTO":
                // Visitados vs lo que debería llevar a esta hora (ritmo de la jornada)
                if (m.CumplimientoPct != null) { c.Valor = Math.Round(m.CumplimientoPct.Value, 0); c.Texto = $"{m.CumplimientoPct:0}%";
                    c.Alarma = m.CumplimientoPct < u; c.Detalle = $"cumplimiento {m.CumplimientoPct:0}% ({m.Visitados}/{m.AVisitar} al ritmo de la hora)"; }
                else { c.Texto = "—"; }
                break;
            case "EFECTIVIDAD_VISITA":
                // Efectividad = visitas efectivas (con pedido) / clientes visitados
                if (m.Visitados is > 0) { double ef = 100.0 * (m.CliConPed ?? 0) / m.Visitados.Value; c.Valor = Math.Round(ef, 0); c.Texto = $"{ef:0}%";
                    c.Alarma = ef < u; c.Detalle = $"efectividad {ef:0}% ({m.CliConPed}/{m.Visitados} c/pedido)"; }
                else { c.Texto = "—"; }
                break;
            case "SIN_SYNC":
                c.Valor = m.MinSinSync; c.Texto = m.MinSinSync == null ? "—" : $"{m.MinSinSync} min";
                c.Alarma = false; c.Detalle = m.MinSinSync == null ? "sin dato de sync" : $"últ. sync hace {m.MinSinSync} min";
                break;
            case "MUCHO_SIN_SYNC":
                c.Valor = m.MinSinSync; c.Texto = m.MinSinSync == null ? "—" : $"{m.MinSinSync} min";
                c.Alarma = m.MinSinSync != null && m.MinSinSync > u;
                c.Detalle = m.MinSinSync == null ? "sin dato de sync" : $"{m.MinSinSync} min sin sincronizar";
                break;
            case "FUERA_ENTORNO":
                if (m.KmEntorno != null) { c.Valor = Math.Round(m.KmEntorno.Value, 1); c.Texto = $"{m.KmEntorno:0.#} km";
                    c.Alarma = m.KmEntorno > u; c.Detalle = $"a {m.KmEntorno:0.#} km de su ruta"; }
                else { c.Texto = "—"; }
                break;
            case "PEDIDO_FUERA_DIA":
                c.Valor = m.FueraDia ?? 0; c.Texto = (m.FueraDia ?? 0).ToString();
                c.Alarma = (m.FueraDia ?? 0) >= u; c.Detalle = $"{m.FueraDia ?? 0} pedido(s) fuera de su día";
                break;
            case "CUADRE_CERRADO":
                c.Texto = m.CuadreCerrado ? "Cerrado" : (m.CuadreAbierto ? "Abierto" : "Sin cuadre");
                c.Alarma = !m.CuadreCerrado && DateTime.Now.Hour >= 14;   // solo hacia el fin de la jornada
                c.Detalle = m.CuadreCerrado ? "cuadre cerrado" : (m.CuadreAbierto ? "cuadre abierto sin cerrar" : "no abrió cuadre hoy");
                break;
            case "ULTIMO_PEDIDO_TEMPRANO":
                // Se evalúa SOLO cuando el vendedor ya "cerró el día": terminó sus visitas o cerró el cuadre.
                // Alarma si su último pedido fue antes del umbral (hora en minutos desde 00:00) = terminó muy temprano.
                {
                    bool cerroDia = m.Terminado || m.CuadreCerrado;
                    if (!cerroDia) { c.Texto = "en curso"; c.Alarma = false; c.Detalle = "aún con visitas/cuadre pendientes"; }
                    else if (m.UltPedMin == null) { c.Texto = "sin pedido"; c.Alarma = false; c.Detalle = "terminó sin pedidos"; }
                    else { c.Valor = m.UltPedMin; c.Texto = hhmm(m.UltPedMin);
                        c.Alarma = m.UltPedMin < u; c.Detalle = $"último pedido {c.Texto} — {(m.Terminado ? "terminó ruta" : "cuadre cerrado")}"; }
                }
                break;
            default:
                c.Texto = "—"; break; // plantillas (producto/oferta/descuento) se evalúan aparte (fase 2)
        }
        // Si ya terminó sus clientes asignados, dejamos de medirlo (salvo el cierre del cuadre y el
        // último-pedido-temprano, que son precisamente los que se evalúan al terminar la jornada).
        if (m.Terminado && mon.Codigo != "CUADRE_CERRADO" && mon.Codigo != "ULTIMO_PEDIDO_TEMPRANO") c.Alarma = false;
        return c;
    }
}
