namespace InmueblesAPI.Services;

public class ProcesoNocturnoService(IServiceScopeFactory scopeFactory, ILogger<ProcesoNocturnoService> logger) : BackgroundService
{
    private static readonly TimeSpan HoraEjecucion = new(1, 0, 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var ahora = DateTime.Now;
            var proximaEjecucion = ahora.Date.Add(HoraEjecucion);
            if (proximaEjecucion <= ahora) proximaEjecucion = proximaEjecucion.AddDays(1);

            try { await Task.Delay(proximaEjecucion - ahora, stoppingToken); }
            catch (TaskCanceledException) { break; }

            await EjecutarProcesosAsync();
        }
    }

    private async Task EjecutarProcesosAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<InmueblesService>();

        try
        {
            var generados = await svc.GenerarCargosRecurrentesAsync();
            logger.LogInformation("Proceso nocturno: {n} cargo(s) generado(s)", generados);
        }
        catch (Exception ex) { logger.LogError(ex, "Error generando cargos recurrentes"); }

        try
        {
            var actualizados = await svc.RecalcularMorasMasivoAsync();
            logger.LogInformation("Proceso nocturno: {n} CxC con mora recalculada", actualizados);
        }
        catch (Exception ex) { logger.LogError(ex, "Error recalculando moras"); }

        // V6 #8 — respaldo nocturno (BD + espejo Excel + Drive)
        try
        {
            var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
            var res = await backup.EjecutarAsync("proceso nocturno");
            logger.LogInformation("Respaldo nocturno: {msg}", res.Mensaje);
        }
        catch (Exception ex) { logger.LogError(ex, "Error en respaldo nocturno"); }
    }
}
