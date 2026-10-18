using ADEnterprise.Models;

namespace ADEnterprise.Services;

public class PagoService
{
    private readonly GoogleSheetsService _gs;
    private readonly FacturaService      _facturas;

    public PagoService(GoogleSheetsService gs, FacturaService facturas)
    {
        _gs       = gs;
        _facturas = facturas;
    }

    // ── LECTURA ──────────────────────────────────────────────────────────────

    public async Task<List<Pago>> ObtenerTodosAsync()
    {
        var filas = await _gs.LeerAsync(GoogleSheetsService.HOJA_PAGOS);
        return filas.Skip(1).Where(f => f.Count >= 6).Select(Mapear).ToList();
    }

    public async Task<List<Pago>> ObtenerPorFacturaAsync(string numTransaccionFactura)
    {
        var todos = await ObtenerTodosAsync();
        return todos.Where(p => p.NumTransaccionFactura == numTransaccionFactura)
                    .OrderBy(p => p.FechaPago).ToList();
    }

    public async Task<List<Pago>> ObtenerPorReciboAsync(string numRecibo)
    {
        var todos = await ObtenerTodosAsync();
        return todos.Where(p => p.NumRecibo == numRecibo)
                    .OrderBy(p => p.NumTicket).ToList();
    }

    // ── ID / RECIBO helpers ──────────────────────────────────────────────────

    /// <summary>Genera el próximo NumRecibo del día: REC-YYYYMMDD-NNNN</summary>
    public async Task<string> SiguienteNumReciboAsync()
    {
        var todos = await ObtenerTodosAsync();
        string hoy    = DateTime.Today.ToString("yyyyMMdd");
        string prefix = $"REC-{hoy}-";
        int max = 0;
        foreach (var p in todos)
            if (p.NumRecibo.StartsWith(prefix) &&
                int.TryParse(p.NumRecibo[prefix.Length..], out int n) && n > max)
                max = n;
        return $"{prefix}{(max + 1):D4}";
    }

    // ── ESCRITURA ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Registra UN recibo completo (varios pagos) en una sola operación batch.
    /// Actualiza PagadoALaFecha en cada factura afectada.
    /// Devuelve el NumRecibo asignado.
    /// </summary>
    public async Task<string> RegistrarReciboAsync(
        string numRecibo,
        decimal montoInstrumento,
        List<Pago> pagos,
        string usuario)
    {
        var ahora = DateTime.Now;
        var filas = new List<IList<object>>();

        for (int i = 0; i < pagos.Count; i++)
        {
            var p = pagos[i];
            p.NumTransaccionPago = $"P{ahora:yyyyMMddHHmmss}{i:D3}";
            p.FechaRegistro      = ahora;
            p.NumRecibo          = numRecibo;
            p.MontoInstrumento   = montoInstrumento;
            p.UsuarioRegistro    = usuario;
            filas.Add(Serializar(p));
        }

        // Una sola llamada batch — todos los pagos del recibo
        await _gs.AgregarMultiplesFilasAsync(GoogleSheetsService.HOJA_PAGOS, filas);

        // Actualizar cada factura (no podemos batching aquí porque son facturas distintas)
        foreach (var p in pagos.Where(p => !string.IsNullOrEmpty(p.NumTransaccionFactura)))
            await _facturas.AplicarAbonoAsync(p.NumTransaccionFactura, p.MontoPagado);

        return numRecibo;
    }

    /// <summary>
    /// Registra un pago individual (retrocompat con el flujo antiguo).
    /// Copia IdCompania e IdCliente desde la factura asociada.
    /// </summary>
    public async Task<Pago> RegistrarAsync(Pago p)
    {
        // Generar NumRecibo si no viene asignado
        if (string.IsNullOrEmpty(p.NumRecibo))
            p.NumRecibo = await SiguienteNumReciboAsync();

        p.NumTransaccionPago = $"P{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
        p.FechaRegistro      = DateTime.Now;

        // Copiar IDs y nombres desde la factura
        var factura = await _facturas.BuscarPorTransaccionAsync(p.NumTransaccionFactura);
        if (factura != null)
        {
            p.IdCompania    = factura.IdCompania;
            p.IdCliente     = factura.IdCliente;
            p.EmpresaNombre = factura.EmpresaNombre;
            p.ClienteNombre = factura.ClienteNombre;
            if (p.MontoInstrumento == 0) p.MontoInstrumento = p.MontoPagado;
        }

        await _gs.AgregarFilaAsync(GoogleSheetsService.HOJA_PAGOS, Serializar(p));
        await _facturas.AplicarAbonoAsync(p.NumTransaccionFactura, p.MontoPagado);
        return p;
    }

    /// <summary>Actualiza un pago existente y recalcula el balance de la factura relacionada.</summary>
    public async Task ActualizarAsync(Pago p, string usuario = "")
    {
        var filas = await _gs.LeerAsync(GoogleSheetsService.HOJA_PAGOS);
        for (int i = 1; i < filas.Count; i++)
        {
            if (GoogleSheetsService.Str(filas[i], 0) != p.NumTransaccionPago) continue;
            p.FechaModificacion   = DateTime.Now;
            p.UsuarioModificacion = usuario;
            await _gs.ActualizarFilaAsync(GoogleSheetsService.HOJA_PAGOS, i + 1, Serializar(p));
            await RecalcularBalanceFacturaAsync(p.NumTransaccionFactura, usuario);
            return;
        }
    }

    /// <summary>Suma todos los pagos de una factura y actualiza PagadoALaFecha + Estado.</summary>
    public async Task RecalcularBalanceFacturaAsync(string numTransaccionFactura, string usuario = "")
    {
        if (string.IsNullOrEmpty(numTransaccionFactura)) return;
        var pagosFactura = await ObtenerPorFacturaAsync(numTransaccionFactura);
        var totalPagado  = pagosFactura.Sum(p => p.MontoPagado);
        await _facturas.RecalcularPagadoAsync(numTransaccionFactura, totalPagado, usuario);
    }

    // ── SERIALIZACIÓN ─────────────────────────────────────────────────────────

    private static IList<object> Serializar(Pago p) => new object[]
    {
        p.NumTransaccionPago,                                         // col 0
        p.NumTransaccionFactura,                                      // col 1
        p.NumTicket,                                                  // col 2
        p.FechaPago.ToString("yyyy-MM-dd"),                          // col 3
        p.MontoPagado.ToString("F2"),                                 // col 4
        p.FormaPago.ToString(),                                       // col 5
        p.NumChequeReferencia,                                        // col 6
        p.Notas,                                                      // col 7
        p.UsuarioRegistro,                                            // col 8
        p.FechaRegistro.ToString("yyyy-MM-dd HH:mm:ss"),             // col 9
        p.FechaModificacion?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",  // col 10
        p.UsuarioModificacion,                                        // col 11
        p.IdCompania,                                                 // col 12
        p.IdCliente,                                                  // col 13
        p.NumRecibo,                                                  // col 14  ← v1.1
        p.MontoInstrumento.ToString("F2"),                            // col 15  ← v1.1
        p.EmpresaNombre,                                              // col 16  ← v1.1
        p.ClienteNombre                                               // col 17  ← v1.1
    };

    private static Pago Mapear(IList<object> f) => new()
    {
        NumTransaccionPago    = GoogleSheetsService.Str(f, 0),
        NumTransaccionFactura = GoogleSheetsService.Str(f, 1),
        NumTicket             = GoogleSheetsService.Int(f, 2),
        FechaPago             = GoogleSheetsService.Fecha(f, 3) ?? DateTime.Today,
        MontoPagado           = GoogleSheetsService.Dec(f, 4),
        FormaPago             = Enum.TryParse<FormaPago>(GoogleSheetsService.Str(f, 5), out var fp)
                                    ? fp : FormaPago.Efectivo,
        NumChequeReferencia   = GoogleSheetsService.Str(f, 6),
        Notas                 = GoogleSheetsService.Str(f, 7),
        UsuarioRegistro       = GoogleSheetsService.Str(f, 8),
        FechaRegistro         = GoogleSheetsService.Fecha(f, 9) ?? DateTime.Now,
        FechaModificacion     = GoogleSheetsService.Fecha(f, 10),
        UsuarioModificacion   = GoogleSheetsService.Str(f, 11),
        IdCompania            = GoogleSheetsService.Str(f, 12),
        IdCliente             = GoogleSheetsService.Str(f, 13),
        NumRecibo             = GoogleSheetsService.Str(f, 14),       // v1.1
        MontoInstrumento      = GoogleSheetsService.Dec(f, 15),       // v1.1
        EmpresaNombre         = GoogleSheetsService.Str(f, 16),       // v1.1
        ClienteNombre         = GoogleSheetsService.Str(f, 17)        // v1.1
    };
}
