namespace ADEnterprise.Services;

/// <summary>
/// Reinicia la base de datos: renombra las hojas actuales agregándoles el sufijo _HISTORICOS
/// y crea hojas nuevas vacías con los encabezados correctos.
/// USUARIOS se mantiene intacto — no se renombra.
/// </summary>
public class ReiniciarDatosService
{
    private readonly GoogleSheetsService   _gs;
    private readonly MantenimientoService  _mant;

    // Hoja original → nombre histórico
    private static readonly (string Original, string Historico)[] HojasARenombrar =
    {
        (FacturaService.HOJA_FACTURAS,          "FACTURAS_HISTORICOS"),
        (GoogleSheetsService.HOJA_PAGOS,        "PAGOS_HISTORICOS"),
        (GoogleSheetsService.HOJA_COMPANIAS,    "COMPANIAS_HISTORICOS"),
        (GoogleSheetsService.HOJA_CLIENTES,     "CLIENTES_HISTORICOS"),
        (CajaService.HOJA_CAJA,                 "CAJA_HISTORICOS"),
        (CajaService.HOJA_CUADRE,               "CUADRE_CAJA_HISTORICOS"),
        (EgresosService.HOJA,                   "EGRESOS_HISTORICOS"),
        (IngresosCapitalService.HOJA,           "INGRESOS_CAPITAL_HISTORICOS"),
    };

    public ReiniciarDatosService(GoogleSheetsService gs, MantenimientoService mant)
    {
        _gs   = gs;
        _mant = mant;
    }

    /// <summary>
    /// Ejecuta el reinicio completo. Pasos:
    /// 1. Renombra todas las hojas de datos a *_HISTORICOS.
    /// 2. Crea hojas nuevas vacías con los nombres originales.
    /// 3. Aplica encabezados y formato a todas las hojas nuevas.
    /// </summary>
    public async Task<(bool Ok, string Mensaje)> EjecutarAsync()
    {
        var renombradas = new List<string>();
        var omitidas    = new List<string>();
        var errores     = new List<string>();

        var existentes = await _gs.ObtenerNombresHojasAsync();

        // Paso 1 — renombrar hojas existentes
        foreach (var (original, historico) in HojasARenombrar)
        {
            // Si ya existe la versión _HISTORICOS, no renombrar (datos históricos previos)
            if (existentes.Contains(historico, StringComparer.OrdinalIgnoreCase))
            {
                omitidas.Add($"{original} (ya existe {historico})");
                continue;
            }

            if (!existentes.Contains(original, StringComparer.OrdinalIgnoreCase))
            {
                omitidas.Add($"{original} (hoja no encontrada)");
                continue;
            }

            try
            {
                await _gs.RenombrarHojaAsync(original, historico);
                renombradas.Add(original);
            }
            catch (Exception ex)
            {
                errores.Add($"{original}: {ex.Message}");
            }
        }

        if (errores.Any())
            return (false, $"Error al renombrar hojas: {string.Join("; ", errores)}");

        // Paso 2 — crear hojas nuevas
        foreach (var (original, _) in HojasARenombrar)
        {
            try
            {
                await _gs.CrearHojaSiNoExisteAsync(original);
            }
            catch (Exception ex)
            {
                errores.Add($"Crear {original}: {ex.Message}");
            }
        }

        if (errores.Any())
            return (false, $"Error al crear hojas nuevas: {string.Join("; ", errores)}");

        // Paso 3 — encabezados y formato
        await _mant.FormatearTodasLasHojasAsync();

        var msg = $"✅ Reinicio completado. " +
                  $"Hojas archivadas: {renombradas.Count} ({string.Join(", ", renombradas)}). " +
                  (omitidas.Any() ? $"Omitidas: {string.Join(", ", omitidas)}." : "");

        return (true, msg);
    }
}
