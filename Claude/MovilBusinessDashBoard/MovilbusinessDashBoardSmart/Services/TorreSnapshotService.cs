using Microsoft.Data.SqlClient;

namespace DashVisitas.Services;

// ── Servicio de snapshots de Torre de Control ───────────────────────────────
// Cada 30 min (en los cortes :00 y :30), dentro de la ventana horaria configurable
// (por defecto 08:20–16:00), calcula los monitores y guarda los casos EN ALARMA
// en TorreControlSnapshots_RM. Corre en el servidor aunque nadie tenga la pantalla.
public class TorreSnapshotService(IConfiguration config, ILogger<TorreSnapshotService> log) : BackgroundService
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // pequeño respiro al arrancar
        try { await Task.Delay(TimeSpan.FromSeconds(20), stop); } catch { }

        while (!stop.IsCancellationRequested)
        {
            // esperar hasta el próximo corte :00 o :30
            var now = DateTime.Now;
            var mins = now.Minute < 30 ? 30 - now.Minute : 60 - now.Minute;
            var next = now.AddMinutes(mins).AddSeconds(-now.Second);
            var wait = next - DateTime.Now;
            if (wait > TimeSpan.Zero)
                try { await Task.Delay(wait, stop); } catch { break; }

            try { TomarSnapshot(); }
            catch (Exception ex) { log.LogWarning(ex, "Torre snapshot falló"); }
        }
    }

    private void TomarSnapshot()
    {
        using var cn = NewConn();
        cn.Open();

        // Ventana horaria configurable: fila _VENTANA_ en Monitores (ParametrosJSON), o default 08:20-16:00
        var (desde, hasta) = LeerVentana(cn);
        var ahora = DateTime.Now.TimeOfDay;
        if (ahora < desde || ahora > hasta) return;  // fuera de la ventana → no guardar

        var todos = TorreCore.CargarMonitores(cn, soloActivos: true);
        var monitores = todos.Where(m => m.Tipo == "nucleo").ToList();
        var custom = TorrePersonalizados.Parse(todos);
        if (monitores.Count == 0 && custom.Count == 0) return;
        var metricas = TorreCore.CargarMetricas(cn, "", new Dictionary<string, object>());
        if (metricas.Count == 0) return;
        var customVals = custom.ToDictionary(s => s.MonID, s => { try { return TorrePersonalizados.ValoresHoy(cn, s); } catch { return new Dictionary<string, double[]>(); } });

        // Mute tras contacto (default 60 min). En mute => nada. Pasado el mute => se reinician los
        // contadores desde (contacto+mute) y las alertas que se repiten vuelven; la de inicio tardío
        // (UnaVezAlDia) NO reaparece una vez contactado. El recálculo va ANTES de la transacción.
        var muteMin = TorreCore.LeerMuteMin(cn);
        var ultimoContacto = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        using (var cc = new SqlCommand("SELECT RepCodigo, MAX(FechaContacto) FROM dbo.TorreControlContactos_RM WHERE FechaContacto >= CAST(CAST(GETDATE() AS date) AS datetime) GROUP BY RepCodigo", cn))
        using (var rd = cc.ExecuteReader())
            while (rd.Read()) ultimoContacto[rd.GetString(0)] = rd.GetDateTime(1);
        var muteados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in metricas.Values)
            if (ultimoContacto.TryGetValue(m.RepCodigo, out var lc))
            {
                if ((DateTime.Now - lc).TotalMinutes < muteMin) muteados.Add(m.RepCodigo);
                else { try { TorreCore.RecomputarDesde(cn, m, lc.AddMinutes(muteMin)); } catch { } }
            }
        // Cumplimiento (ritmo por hora) + representantes sin alerta
        var (vDesde, vHasta) = TorreCore.LeerVentanaHoraria(cn);
        var repsOff = TorreCore.LeerRepsOff(cn);
        foreach (var m in metricas.Values) TorreCore.CalcularCumplimiento(m, vDesde, vHasta, DateTime.Now);

        var corte = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute >= 30 ? 30 : 0, 0);

        int guardados = 0;
        using var tx = cn.BeginTransaction();
        try
        {
            void Guardar(int monId, TorreCore.Celda c, TorreCore.RepMetric m)
            {
                using var cmd = new SqlCommand(@"
INSERT INTO dbo.TorreControlSnapshots_RM
  (FechaCorte, MonID, Codigo, RepCodigo, RepNombre, SupCod, SupervisorNombre, Centro, Division,
   Valor, ValorTexto, EnAlarma, Severidad, Detalle)
VALUES (@corte,@mon,@cod,@rep,@nom,@sc,@sup,@cen,@div,@val,@txt,1,@sev,@det)", cn, tx);
                cmd.Parameters.AddWithValue("@corte", corte);
                cmd.Parameters.AddWithValue("@mon", monId);
                cmd.Parameters.AddWithValue("@cod", c.Codigo);
                cmd.Parameters.AddWithValue("@rep", m.RepCodigo);
                cmd.Parameters.AddWithValue("@nom", (object?)m.RepNombre ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sc", (object?)m.SupCod ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sup", (object?)m.Supervisor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cen", (object?)m.Centro ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@div", (object?)m.Division ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@val", (object?)c.Valor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@txt", (object?)c.Texto ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sev", c.Severidad);
                cmd.Parameters.AddWithValue("@det", (object?)c.Detalle ?? DBNull.Value);
                cmd.ExecuteNonQuery();
                guardados++;
            }
            foreach (var m in metricas.Values)
            {
                if (muteados.Contains(m.RepCodigo) || repsOff.Contains(m.RepCodigo)) continue;  // en mute o sin-alerta → fuera
                bool contactadoHoy = ultimoContacto.ContainsKey(m.RepCodigo);
                foreach (var mon in monitores)
                {
                    if (contactadoHoy && TorreCore.UnaVezAlDia.Contains(mon.Codigo)) continue;  // inicio tardío no reaparece
                    var c = TorreCore.Evaluar(mon, m);
                    if (c.Alarma) Guardar(mon.MonID, c, m);
                }
                foreach (var s in custom)
                {
                    var c = TorrePersonalizados.Celda(s, m, customVals[s.MonID]);
                    if (c.Alarma) Guardar(s.MonID, c, m);
                }
            }
            tx.Commit();
            log.LogInformation("Torre snapshot {corte}: {n} alarmas guardadas", corte, guardados);
        }
        catch { tx.Rollback(); throw; }
    }

    // Ventana horaria desde la fila _VENTANA_ (Tipo='config') o default 08:20–16:00
    private (TimeSpan desde, TimeSpan hasta) LeerVentana(SqlConnection cn)
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
}
