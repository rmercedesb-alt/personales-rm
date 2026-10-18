using ADEnterprise.Models;

namespace ADEnterprise.Services;

/// <summary>
/// Gestiona apertura/cierre de caja, movimientos y balance en tiempo real.
/// Todas las operaciones del sistema requieren una caja ABIERTA del día actual.
/// </summary>
public class CajaService
{
    private readonly GoogleSheetsService _gs;

    public const string HOJA_CAJA        = "CAJA";
    public const string HOJA_CUADRE      = "CUADRE_CAJA";

    // Prefijos de secuencia
    private const string PFX_MOV    = "MOV";
    private const string PFX_CUADRE = "CUADRE";

    public CajaService(GoogleSheetsService gs) => _gs = gs;

    // ═══════════════════════════════════════════════════════════════════════
    // CUADRE (apertura / cierre)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retorna la caja actualmente ABIERTA, o null si no hay ninguna.
    /// Si existe una abierta con fecha distinta al día actual devuelve
    /// el objeto igual — el llamador debe forzar el cierre primero.
    /// </summary>
    public async Task<CuadreCaja?> GetCajaAbiertaAsync()
    {
        var filas = await _gs.LeerAsync(HOJA_CUADRE);
        for (int i = filas.Count - 1; i >= 1; i--)
        {
            var r = filas[i];
            if (Val(r, 1) == "ABIERTA")
                return MapCuadre(r, i + 1);
        }
        return null;
    }

    /// <summary>
    /// Valida si hay caja abierta HOY.
    /// Devuelve (ok, caja, mensaje):
    ///   ok=true  → todo bien
    ///   ok=false, caja!=null → hay caja de DIA ANTERIOR que debe cerrarse
    ///   ok=false, caja==null → no hay caja abierta
    /// </summary>
    public async Task<(bool Ok, CuadreCaja? Caja, string Mensaje)> ValidarCajaAsync()
    {
        var caja = await GetCajaAbiertaAsync();
        if (caja == null)
            return (false, null, "No hay caja abierta. Debe abrir la caja del día para operar.");

        if (caja.FechaApertura.Date != DateTime.Today)
            return (false, caja,
                $"La caja {caja.SeqCuadre} del {caja.FechaApertura:dd/MM/yyyy} está abierta. " +
                "Debe cerrarla antes de comenzar el día de hoy.");

        return (true, caja, "");
    }

    /// <summary>Abre la caja del día. Retorna el SeqCuadre nuevo.</summary>
    public async Task<string> AbrirCajaAsync(decimal balanceInicial, string usuario)
    {
        // Verificar que no haya caja abierta de hoy
        var (ok, cajaExistente, _) = await ValidarCajaAsync();
        if (ok && cajaExistente != null)
            throw new InvalidOperationException(
                $"Ya existe una caja abierta hoy: {cajaExistente.SeqCuadre}");

        var seq = await GenerarSeqCuadreAsync();
        var ahora = DateTime.Now;

        var fila = new List<object>
        {
            seq,                        // SeqCuadre
            "ABIERTA",                  // Estado
            ahora.ToString("o"),        // FechaApertura
            "",                         // FechaCierre
            usuario,                    // UsuarioApertura — auto
            "",                         // UsuarioCierre
            balanceInicial.ToString("F2"), // BalanceInicial
            "",                         // BalanceFinal
            "0",                        // CantFacturas
            "0",                        // MontoFacturado
            "0",                        // MontoPagadoFacturas
            "0",                        // CantCobros
            "0",                        // MontoCobros
            "",                         // Notas
            ahora.ToString("o"),        // FechaCreacion
        };

        await _gs.AgregarFilaAsync(HOJA_CUADRE, fila);
        return seq;
    }

    /// <summary>Cierra la caja del día con los totales calculados.</summary>
    public async Task CerrarCajaAsync(string seqCuadre, string usuario)
    {
        var filas = await _gs.LeerAsync(HOJA_CUADRE);
        int numFila = -1;
        for (int i = 1; i < filas.Count; i++)
            if (Val(filas[i], 0) == seqCuadre) { numFila = i + 1; break; }

        if (numFila < 0)
            throw new InvalidOperationException($"Cuadre {seqCuadre} no encontrado.");

        // Calcular totales del día desde CAJA
        var movs = await GetMovimientosDelDiaAsync(seqCuadre);

        decimal balInicial  = ParseDec(filas[numFila - 1], 6);
        decimal totalCred   = movs.Where(m => m.TipoMov == "CREDITO").Sum(m => m.Monto);
        decimal totalDeb    = movs.Where(m => m.TipoMov == "DEBITO").Sum(m => m.Monto);
        decimal balFinal    = balInicial + totalCred - totalDeb;

        // PagoFactoring = DÉBITO (lo que salió a clientes); ComisionFactoring = CRÉDITO (ganancia)
        int     cantFact     = movs.Count(m => m.Categoria == CategoriaMovCaja.PagoFactoring);
        decimal montoFact    = movs.Where(m => m.Categoria == CategoriaMovCaja.PagoFactoring).Sum(m => m.Monto);
        decimal montoComis   = movs.Where(m => m.Categoria == CategoriaMovCaja.ComisionFactoring).Sum(m => m.Monto);
        // MontoPagadoFacturas = adelantado - comisión = desembolso neto
        decimal montoPagFact = montoFact - montoComis;
        int    cantCobros    = movs.Count(m => m.Categoria == CategoriaMovCaja.CobroCxC);
        decimal montoCobros  = movs.Where(m => m.Categoria == CategoriaMovCaja.CobroCxC).Sum(m => m.Monto);

        // Actualizar fila en CUADRE_CAJA (columnas B-M)
        var actualizaciones = new Dictionary<string, object>
        {
            ["B"] = "CERRADA",
            ["D"] = DateTime.Now.ToString("o"),
            ["F"] = usuario,
            ["H"] = balFinal.ToString("F2"),
            ["I"] = cantFact.ToString(),
            ["J"] = montoFact.ToString("F2"),
            ["K"] = montoPagFact.ToString("F2"),
            ["L"] = cantCobros.ToString(),
            ["M"] = montoCobros.ToString("F2"),
        };

        foreach (var kv in actualizaciones)
        {
            await _gs.ActualizarCeldaAsync(HOJA_CUADRE, numFila, kv.Key, kv.Value.ToString()!);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MOVIMIENTOS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Registra un movimiento en la hoja CAJA.
    /// TipoMov: "DEBITO" (salida) | "CREDITO" (entrada).
    /// </summary>
    public async Task<string> RegistrarMovimientoAsync(
        string seqCuadre, string tipoMov, string categoria,
        decimal monto, string concepto, string refDocumento,
        string empresaNombre, string clienteNombre, string numTicket,
        string usuario)
    {
        var seq   = await GenerarSeqMovAsync();
        var ahora = DateTime.Now;

        var fila = new List<object>
        {
            seq,                           // SeqMovCaja
            seqCuadre,                     // SeqCuadre
            tipoMov,                       // TipoMov
            categoria,                     // Categoria
            refDocumento,                  // RefDocumento
            concepto,                      // Concepto
            monto.ToString("F2"),          // Monto
            ahora.ToString("yyyy-MM-dd"),  // FechaMovimiento
            empresaNombre,                 // EmpresaNombre
            clienteNombre,                 // ClienteNombre
            numTicket,                     // NumTicket
            usuario,                       // UsuarioRegistro — auto
            ahora.ToString("o"),           // FechaCreacion — auto
        };

        await _gs.AgregarFilaAsync(HOJA_CAJA, fila);
        return seq;
    }

    /// <summary>Movimientos de una caja específica (por SeqCuadre).</summary>
    public async Task<List<MovimientoCaja>> GetMovimientosDelDiaAsync(string seqCuadre)
    {
        var filas = await _gs.LeerAsync(HOJA_CAJA);
        return filas.Skip(1)
            .Where(r => Val(r, 1) == seqCuadre)
            .Select(MapMov)
            .ToList();
    }

    /// <summary>
    /// Balance disponible en tiempo real del cuadre actualmente ABIERTO:
    ///   BalanceInicial + TotalCréditos − TotalDébitos del cuadre.
    /// Retorna null si no hay caja abierta.
    /// </summary>
    public async Task<decimal?> GetBalanceDisponibleAsync()
    {
        var caja = await GetCajaAbiertaAsync();
        if (caja == null) return null;

        var movs      = await GetMovimientosDelDiaAsync(caja.SeqCuadre);
        decimal cred  = movs.Where(m => m.TipoMov == "CREDITO").Sum(m => m.Monto);
        decimal deb   = movs.Where(m => m.TipoMov == "DEBITO").Sum(m => m.Monto);
        return caja.BalanceInicial + cred - deb;
    }

    /// <summary>
    /// Balance actual de caja = suma de CRÉDITOs - suma de DÉBITOs de toda la historia.
    /// </summary>
    public async Task<decimal> GetBalanceActualAsync()
    {
        var filas = await _gs.LeerAsync(HOJA_CAJA);
        decimal balance = 0;
        foreach (var r in filas.Skip(1))
        {
            var monto = ParseDec(r, 6);
            balance += Val(r, 2) == "CREDITO" ? monto : -monto;
        }
        return balance;
    }

    /// <summary>Resumen de todas las cajas (historial de cuadres).</summary>
    public async Task<List<CuadreCaja>> GetHistorialCuadresAsync()
    {
        var filas = await _gs.LeerAsync(HOJA_CUADRE);
        return filas.Skip(1).Select((r, i) => MapCuadre(r, i + 2))
                    .OrderByDescending(c => c.FechaApertura).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private static string Val(IList<object> r, int i) =>
        i < r.Count ? r[i]?.ToString()?.Trim() ?? "" : "";

    private static decimal ParseDec(IList<object> r, int i) =>
        decimal.TryParse(Val(r, i), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static DateTime ParseDate(IList<object> r, int i) =>
        DateTime.TryParse(Val(r, i), out var d) ? d : DateTime.MinValue;

    private CuadreCaja MapCuadre(IList<object> r, int fila) => new()
    {
        SeqCuadre           = Val(r, 0),
        Estado              = Val(r, 1),
        FechaApertura       = ParseDate(r, 2),
        FechaCierre         = string.IsNullOrEmpty(Val(r, 3)) ? null : ParseDate(r, 3),
        UsuarioApertura     = Val(r, 4),
        UsuarioCierre       = Val(r, 5),
        BalanceInicial      = ParseDec(r, 6),
        BalanceFinal        = ParseDec(r, 7),
        CantFacturas        = (int)ParseDec(r, 8),
        MontoFacturado      = ParseDec(r, 9),
        MontoPagadoFacturas = ParseDec(r, 10),
        CantCobros          = (int)ParseDec(r, 11),
        MontoCobros         = ParseDec(r, 12),
        Notas               = Val(r, 13),
        FechaCreacion       = ParseDate(r, 14),
        NumFila             = fila,
    };

    private static MovimientoCaja MapMov(IList<object> r) => new()
    {
        SeqMovCaja      = Val(r, 0),
        SeqCuadre       = Val(r, 1),
        TipoMov         = Val(r, 2),
        Categoria       = Val(r, 3),
        RefDocumento    = Val(r, 4),
        Concepto        = Val(r, 5),
        Monto           = ParseDec(r, 6),
        FechaMovimiento = ParseDate(r, 7),
        EmpresaNombre   = Val(r, 8),
        ClienteNombre   = Val(r, 9),
        NumTicket       = Val(r, 10),
        UsuarioRegistro = Val(r, 11),
        FechaCreacion   = ParseDate(r, 12),
    };

    private async Task<string> GenerarSeqCuadreAsync()
    {
        var filas = await _gs.LeerAsync(HOJA_CUADRE);
        var hoy   = DateTime.Today.ToString("yyyyMMdd");
        int n = filas.Skip(1).Count(r => Val(r, 0).Contains(hoy)) + 1;
        return $"{PFX_CUADRE}-{hoy}-{n:D4}";
    }

    private async Task<string> GenerarSeqMovAsync()
    {
        var filas = await _gs.LeerAsync(HOJA_CAJA);
        var hoy   = DateTime.Today.ToString("yyyyMMdd");
        int n = filas.Skip(1).Count(r => Val(r, 0).Contains(hoy)) + 1;
        return $"{PFX_MOV}-{hoy}-{n:D4}";
    }
}
