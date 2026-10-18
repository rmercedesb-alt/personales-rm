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

    // ── Tablas v2.0 — Finanzas ───────────────────────────────────────────────

    private static readonly string[] EncabezadosCuadreCaja = new[]
    {
        "SeqCuadre",              // A  col 0
        "Estado",                 // B  col 1
        "FechaApertura",          // C  col 2
        "FechaCierre",            // D  col 3
        "UsuarioApertura",        // E  col 4
        "UsuarioCierre",          // F  col 5
        "BalanceInicial",         // G  col 6
        "BalanceFinal",           // H  col 7
        "CantFacturas",           // I  col 8
        "MontoFacturado",         // J  col 9
        "MontoPagadoFacturas",    // K  col 10
        "CantCobros",             // L  col 11
        "MontoCobros",            // M  col 12
        "Notas",                  // N  col 13
        "FechaCreacion"           // O  col 14
    };

    private static readonly string[] EncabezadosCaja = new[]
    {
        "SeqMovCaja",             // A  col 0
        "SeqCuadre",              // B  col 1
        "TipoMov",                // C  col 2
        "Categoria",              // D  col 3
        "RefDocumento",           // E  col 4
        "Concepto",               // F  col 5
        "Monto",                  // G  col 6
        "FechaMovimiento",        // H  col 7
        "EmpresaNombre",          // I  col 8
        "ClienteNombre",          // J  col 9
        "NumTicket",              // K  col 10
        "UsuarioRegistro",        // L  col 11
        "FechaCreacion"           // M  col 12
    };

    private static readonly string[] EncabezadosEgresos = new[]
    {
        "SeqEgreso",              // A  col 0
        "SeqCuadre",              // B  col 1
        "TipoEgreso",             // C  col 2
        "ANombreDe",              // D  col 3
        "Fecha",                  // E  col 4
        "Monto",                  // F  col 5
        "Nota",                   // G  col 6
        "UsuarioRegistro",        // H  col 7
        "FechaCreacion",          // I  col 8
        "SeqMovCaja"              // J  col 9
    };

    private static readonly string[] EncabezadosIngresosCapital = new[]
    {
        "Secuencia",              // A  col 0
        "SeqCuadre",              // B  col 1
        "Nombre",                 // C  col 2
        "Fecha",                  // D  col 3
        "Monto",                  // E  col 4
        "Observaciones",          // F  col 5
        "UsuarioRegistro",        // G  col 6
        "FechaCreacion",          // H  col 7
        "SeqMovCaja"              // I  col 8
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
            // Tablas originales
            (GoogleSheetsService.HOJA_PAGOS,     EncabezadosPagos),
            (FacturaService.HOJA_FACTURAS,        EncabezadosFacturas),
            (GoogleSheetsService.HOJA_COMPANIAS,  EncabezadosCompanias),
            (GoogleSheetsService.HOJA_CLIENTES,   EncabezadosClientes),
            (GoogleSheetsService.HOJA_USUARIOS,   EncabezadosUsuarios),
            // Tablas v2.0 — Finanzas
            (CajaService.HOJA_CUADRE,             EncabezadosCuadreCaja),
            (CajaService.HOJA_CAJA,               EncabezadosCaja),
            (EgresosService.HOJA,                 EncabezadosEgresos),
            (IngresosCapitalService.HOJA,          EncabezadosIngresosCapital),
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

        var msg = $"✅ {procesadas.Count} hojas formateadas: {string.Join(", ", procesadas)}.";
        if (errores.Any())
            msg += $" ⚠️ Errores en: {string.Join("; ", errores)}";

        return msg;
    }
}
