using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace DashVisitas.Services;

// ── Precalentado de la curva de estacionalidad "General" (Presupuestos) ────────
// Pedido del usuario 2026-08-29 ("Dashboard Gráficos dura varios minutos"): verificado
// en vivo con Puppeteer + Chrome real contra la BD real que la causa de fondo es que
// PresupuestosCore.CurvaPorFranjas (escaneo de VWVentas/VWVentasDetalle, sin filtro, 6
// meses atrás) tarda ~17s la PRIMERA vez que el mes actual no está en caché ese día —
// y mientras corre, satura el servidor SQL compartido y atrasa TODO lo demás que se
// consulte al mismo tiempo (incluidas las consultas de Dashboard Gráficos, que en sí
// mismas ya son rápidas). Ese costo es inevitable la primera vez que se calcula para
// un (año,mes) dado — pero no hace falta que sea el PRIMER USUARIO quien lo pague: este
// servicio la calcula sola, en segundo plano, apenas arranca la app (y cada vez que
// cruza medianoche), para que ya esté tibia cuando el primer usuario real haga clic en
// "Consultar". Desde la 27ª parte (2026-08-29) además la GUARDA en
// PresupuestosCurvas_RM (tabla creada por TI, ver SQL/PresupuestosCurvas_RM.sql) — una
// curva ya calculada para un (año,mes) es estable para siempre (promedio de 6 meses YA
// CERRADOS), así que una vez guardada, ni este servicio ni ningún request futuro vuelve
// a pagar el costo de ~17s para ese mismo mes — solo la LEE de la tabla (rápido). Misma
// clave que usa ApiPresupuestosController (curva:{tipo}:{claveRef}:{anio}{mes:00}),
// para que la reutilice tal cual.
public class PresupuestosWarmupService(IConfiguration config, IMemoryCache cache, ILogger<PresupuestosWarmupService> log) : BackgroundService
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));
    private const string Tipo = "general", ClaveRef = "GENERAL";

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { Precalentar(); }
            catch (Exception ex) { log.LogWarning(ex, "Precalentado de CurvaGeneral falló"); }

            // Solo hace falta revisar 1 vez por día (mes) — poco después de medianoche,
            // cuando el mes/año de "hoy" pueda haber cambiado.
            var ahora = DateTime.Now;
            var manana = ahora.Date.AddDays(1).AddMinutes(5);
            try { await Task.Delay(manana - ahora, stop); } catch { break; }
        }
    }

    private void Precalentar()
    {
        var hoy = DateTime.Today;
        // canId=0 fijo ("todos los canales") — mismo formato de clave que
        // ApiPresupuestosController.ObtenerCurvaConPersistencia desde que se agregó el eje
        // Canal (2026-09-05); si no coincide letra por letra, este precalentado escribe bajo
        // una clave que el controller nunca vuelve a leer (se degrada solo, sin romper nada,
        // pero pierde el beneficio de "ya está tibia").
        var claveMem = $"curva:{Tipo}:{ClaveRef}:can0:{hoy.Year}{hoy.Month:00}";
        if (cache.TryGetValue(claveMem, out _)) return; // ya está tibia en memoria

        using var cn = NewConn();
        cn.Open();

        var enTabla = PresupuestosCore.LeerCurvaDeTabla(cn, Tipo, ClaveRef, hoy.Year, hoy.Month);
        if (enTabla != null)
        {
            cache.Set(claveMem, enTabla, TimeSpan.FromHours(24));
            log.LogInformation("Curva general {Anio}{Mes:00} cargada desde PresupuestosCurvas_RM (ya calculada, no hizo falta VWVentas)", hoy.Year, hoy.Month);
            return;
        }

        var curva = PresupuestosCore.CurvaPorFranjas(cn, hoy.Year, hoy.Month, null, null, 0);
        PresupuestosCore.GuardarCurvaEnTabla(cn, Tipo, ClaveRef, hoy.Year, hoy.Month, curva);
        cache.Set(claveMem, curva, TimeSpan.FromHours(24));
        log.LogInformation("Precalentado (calculado contra VWVentas y guardado) curva:general:{Anio}{Mes:00}", hoy.Year, hoy.Month);
    }
}
