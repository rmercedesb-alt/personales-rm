namespace InmueblesAPI.Services;

public class ProcesoNocturnoService : BackgroundService
{
    private readonly GoogleSheetsService _sheets;
    private readonly ILogger<ProcesoNocturnoService> _logger;
    private static readonly TimeSpan HoraEjecucion = new(1, 0, 0); // 1:00 AM

    public ProcesoNocturnoService(GoogleSheetsService sheets, ILogger<ProcesoNocturnoService> logger)
    {
        _sheets = sheets;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var ahora = DateTime.Now;
            var proximaEjecucion = ahora.Date.Add(HoraEjecucion);
            if (proximaEjecucion <= ahora) proximaEjecucion = proximaEjecucion.AddDays(1);
            var espera = proximaEjecucion - ahora;

            try { await Task.Delay(espera, stoppingToken); }
            catch (TaskCanceledException) { break; }

            await EjecutarProcesosAsync();
        }
    }

    private async Task EjecutarProcesosAsync()
    {
        try
        {
            var generados = await _sheets.GenerarCargosRecurrentesAsync();
            _logger.LogInformation("Proceso nocturno: {Generados} cargo(s) de alquiler generado(s)", generados);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando cargos recurrentes en el proceso nocturno");
        }

        try
        {
            var actualizados = await _sheets.RecalcularMorasMasivoAsync();
            _logger.LogInformation("Proceso nocturno: {Actualizados} CxC con mora recalculada", actualizados);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recalculando moras en el proceso nocturno");
        }
    }
}
