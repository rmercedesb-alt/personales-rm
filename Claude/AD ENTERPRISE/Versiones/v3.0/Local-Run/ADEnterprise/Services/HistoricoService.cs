namespace ADEnterprise.Services;

/// <summary>
/// Lee datos de las hojas _HISTORICOS (datos archivados por el proceso Reiniciar Datos).
/// Devuelve datos genéricos (encabezados + filas) para mostrarlos en páginas de solo lectura.
/// </summary>
public class HistoricoService
{
    private readonly GoogleSheetsService _gs;

    public const string HOJA_FACTURAS_H          = "FACTURAS_HISTORICOS";
    public const string HOJA_PAGOS_H             = "PAGOS_HISTORICOS";
    public const string HOJA_COMPANIAS_H         = "COMPANIAS_HISTORICOS";
    public const string HOJA_CLIENTES_H          = "CLIENTES_HISTORICOS";
    public const string HOJA_CAJA_H              = "CAJA_HISTORICOS";
    public const string HOJA_CUADRE_H            = "CUADRE_CAJA_HISTORICOS";
    public const string HOJA_EGRESOS_H           = "EGRESOS_HISTORICOS";
    public const string HOJA_INGRESOS_CAPITAL_H  = "INGRESOS_CAPITAL_HISTORICOS";

    public HistoricoService(GoogleSheetsService gs) => _gs = gs;

    public async Task<bool> ExisteAsync(string hoja)
    {
        var nombres = await _gs.ObtenerNombresHojasAsync();
        return nombres.Contains(hoja, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lee una hoja histórica. Devuelve (encabezados, filas de datos).
    /// Si la hoja no existe devuelve listas vacías.
    /// </summary>
    public async Task<(List<string> Encabezados, IList<IList<object>> Filas)> LeerAsync(string hoja)
    {
        if (!await ExisteAsync(hoja))
            return (new List<string>(), new List<IList<object>>());

        var datos = await _gs.LeerAsync(hoja);
        if (datos.Count == 0)
            return (new List<string>(), new List<IList<object>>());

        var encabezados = datos[0].Select(c => c?.ToString() ?? "").ToList();
        var filas       = datos.Skip(1).ToList();
        return (encabezados, filas);
    }
}
