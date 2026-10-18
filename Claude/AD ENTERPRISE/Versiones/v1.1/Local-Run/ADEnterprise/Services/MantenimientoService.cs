namespace ADEnterprise.Services;

/// <summary>
/// Operaciones de mantenimiento sobre las hojas de Google Sheets:
/// corrección de encabezados, formato de tabla, integridad de datos.
/// </summary>
public class MantenimientoService
{
    private readonly GoogleSheetsService _gs;

    public MantenimientoService(GoogleSheetsService gs)
    {
        _gs = gs;
    }

    // ── Definición completa de encabezados por hoja ──────────────────────────

    private static readonly string[] EncabezadosPagos = new[]
    {
        "NumTransaccionPago",     // A  col 0
        "NumTransaccionFactura",  // B  col 1
        "NumTicket",              // C  col 2
        "FechaPago",              // D  col 3
        "MontoPagado",            // E  col 4
        "FormaPago",              // F  col 5
        "NumChequeReferencia",    // G  col 6
        "Notas",                  // H  col 7
        "UsuarioRegistro",        // I  col 8
        "FechaRegistro",          // J  col 9
        "FechaModificacion",      // K  col 10
        "UsuarioModificacion",    // L  col 11
        "IdCompania",             // M  col 12
        "IdCliente",              // N  col 13
        "NumRecibo",              // O  col 14  ← v1.1
        "MontoInstrumento",       // P  col 15  ← v1.1
        "EmpresaNombre",          // Q  col 16  ← v1.1
        "ClienteNombre"           // R  col 17  ← v1.1
    };

    private static readonly string[] EncabezadosFacturas = new[]
    {
        "NumTransaccion",         // A  col 0
        "NumTicket",              // B  col 1
        "EmpresaNombre",          // C  col 2
        "ClienteNombre",          // D  col 3
        "Monto",                  // E  col 4
        "FechaFactura",           // F  col 5
        "Estado",                 // G  col 6
        "PagadoALaFecha",         // H  col 7
        "Notas",                  // I  col 8
        "ValorAnterior",          // J  col 9
        "Origen",                 // K  col 10
        "FechaMigracion",         // L  col 11
        "UsuarioMigracion",       // M  col 12
        "FechaModificacion",      // N  col 13
        "UsuarioModificacion",    // O  col 14
        "IdCompania",             // P  col 15
        "IdCliente"               // Q  col 16
    };

    private static readonly string[] EncabezadosCompanias = new[]
    {
        "ID",                     // A
        "Nombre",                 // B
        "RNC",                    // C
        "Telefono",               // D
        "Direccion",              // E
        "Correo",                 // F
        "CondicionPagoDias",      // G
        "Activo"                  // H
    };

    private static readonly string[] EncabezadosClientes = new[]
    {
        "ID",                     // A
        "NombreCompleto",         // B
        "Cedula",                 // C
        "Telefono",               // D
        "Correo",                 // E
        "Activo"                  // F
    };

    private static readonly string[] EncabezadosUsuarios = new[]
    {
        "ID",                     // A
        "NombreCompleto",         // B
        "NombreUsuario",          // C
        "PasswordHash",           // D
        "Correo",                 // E
        "Perfil",                 // F
        "Estado",                 // G
        "FechaCreacion"           // H
    };

    // ── Operación principal ──────────────────────────────────────────────────

    /// <summary>
    /// Corrige los encabezados de todas las hojas y aplica formato de tabla profesional.
    /// Seguro para ejecutar múltiples veces (idempotente).
    /// </summary>
    public async Task<string> FormatearTodasLasHojasAsync()
    {
        var hojas = new (string Nombre, string[] Encabezados)[]
        {
            (GoogleSheetsService.HOJA_PAGOS,    EncabezadosPagos),
            (FacturaService.HOJA_FACTURAS,       EncabezadosFacturas),
            (GoogleSheetsService.HOJA_COMPANIAS, EncabezadosCompanias),
            (GoogleSheetsService.HOJA_CLIENTES,  EncabezadosClientes),
            (GoogleSheetsService.HOJA_USUARIOS,  EncabezadosUsuarios),
        };

        var procesadas = new List<string>();
        var errores    = new List<string>();

        foreach (var (nombre, enc) in hojas)
        {
            try
            {
                await _gs.FormatearHojaAsync(nombre, enc);
                procesadas.Add(nombre);
            }
            catch (Exception ex)
            {
                errores.Add($"{nombre}: {ex.Message}");
            }
        }

        var msg = $"✅ Hojas formateadas: {string.Join(", ", procesadas)}.";
        if (errores.Any())
            msg += $" ⚠️ Errores en: {string.Join("; ", errores)}";

        return msg;
    }
}
