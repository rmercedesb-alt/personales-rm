using ADEnterprise.Models;
using System.Text.Json;

namespace ADEnterprise.Services;

public class FacturaService
{
    private readonly GoogleSheetsService _gs;
    private readonly CompaniaService     _companias;
    private readonly CajaService         _caja;

    // Una sola hoja para todas las facturas
    public const string HOJA_FACTURAS = "FACTURAS";

    public FacturaService(GoogleSheetsService gs, CompaniaService companias, CajaService caja)
    {
        _gs        = gs;
        _companias = companias;
        _caja      = caja;
    }

    // ─── LECTURA ─────────────────────────────────────────────────────────────

    public async Task<List<Factura>> ObtenerTodasAsync()
    {
        var filas     = await _gs.LeerAsync(HOJA_FACTURAS);
        var companias = await _companias.ObtenerTodasAsync();

        return filas.Skip(1)
            .Where(f => f.Count >= 6 && !string.IsNullOrWhiteSpace(GoogleSheetsService.Str(f, 0)))
            .Select(f =>
            {
                var fac = Mapear(f);
                // Calcular vencimiento según condición de pago de la compañía
                var comp = companias.FirstOrDefault(c =>
                    c.Nombre.Equals(fac.EmpresaNombre, StringComparison.OrdinalIgnoreCase));
                int dias = comp?.CondicionPagoDias ?? 30;
                // No marcar como Vencida si ya es Credito o Pagada
                if (fac.Estado != EstadoFactura.Pagada &&
                    fac.Estado != EstadoFactura.Credito &&
                    fac.EstaVencida(dias))
                    fac.Estado = EstadoFactura.Vencida;
                return fac;
            }).ToList();
    }

    public async Task<Factura?> BuscarPorTicketAsync(int ticket)
    {
        var todas = await ObtenerTodasAsync();
        return todas.FirstOrDefault(f => f.NumTicket == ticket);
    }

    public async Task<Factura?> BuscarPorTransaccionAsync(string numTransaccion)
    {
        var todas = await ObtenerTodasAsync();
        return todas.FirstOrDefault(f => f.NumTransaccion == numTransaccion);
    }

    // ─── ESCRITURA ────────────────────────────────────────────────────────────

    public async Task<Factura> CrearAsync(Factura f, string usuario = "Sistema")
    {
        // Crear hoja FACTURAS con encabezados si no existe
        await _gs.CrearHojaSiNoExisteAsync(HOJA_FACTURAS);
        var existentes = await _gs.LeerAsync(HOJA_FACTURAS, "A1:A1");
        if (existentes.Count == 0)
            await _gs.AgregarFilaAsync(HOJA_FACTURAS, Encabezados());

        f.NumTransaccion = $"F{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
        if (f.NumTicket == 0)
            f.NumTicket = await SiguienteTicketAsync();

        // Solo forzar Pendiente/0 si no viene de migración con estado ya definido
        if (f.Origen != OrigenDato.Migracion)
        {
            f.Estado          = EstadoFactura.Pendiente;
            f.PagadoALaFecha  = 0;
            f.FechaMigracion  = DateTime.Now;
            f.FechaCreacion   = DateTime.Now;  // v2.0: timestamp automático
        }

        // ── v2.0: SeqCuadre + validación de balance ───────────────────────────
        bool esFactoringConPago = f.Origen != OrigenDato.Migracion
                               && f.Estado != EstadoFactura.Credito
                               && f.MontoPagado > 0;

        if (esFactoringConPago)
        {
            // Obtener caja abierta y su balance en tiempo real
            var balanceDisponible = await _caja.GetBalanceDisponibleAsync();

            if (balanceDisponible == null)
                throw new InvalidOperationException(
                    "No hay una caja abierta. Debe abrir el Cuadre de Caja antes de registrar un Pago Factoring.");

            if (f.MontoPagado > balanceDisponible.Value)
            {
                var faltante = f.MontoPagado - balanceDisponible.Value;
                throw new InvalidOperationException(
                    $"Fondos insuficientes en caja.\n" +
                    $"  • Balance disponible:  RD$ {balanceDisponible.Value:N2}\n" +
                    $"  • Monto solicitado:    RD$ {f.MontoPagado:N2}\n" +
                    $"  • Diferencia faltante: RD$ {faltante:N2}\n\n" +
                    $"Ingrese capital a caja o reduzca el monto del pago factoring.");
            }

            // Registrar SeqCuadre desde la caja abierta
            var caja = await _caja.GetCajaAbiertaAsync();
            if (caja != null) f.SeqCuadre = caja.SeqCuadre;
        }
        else
        {
            // Sin pago factoring: capturar SeqCuadre si existe (sin bloquear)
            try
            {
                var (ok, cajaAbierta, _) = await _caja.ValidarCajaAsync();
                if (ok && cajaAbierta != null) f.SeqCuadre = cajaAbierta.SeqCuadre;
            }
            catch { /* Caja opcional para registros sin movimiento */ }
        }

        await _gs.AgregarFilaAsync(HOJA_FACTURAS, Serializar(f));

        // ── v2.0: Double-entry en CAJA ────────────────────────────────────────
        // Lógica de factoring:
        //   DÉBITO   = adelanto al cliente (MontoPagado — dinero que SALE de caja)
        //   CRÉDITO  = comisión ganada   (Monto - MontoPagado — entra a caja)
        if (esFactoringConPago && !string.IsNullOrEmpty(f.SeqCuadre))
        {
            // DÉBITO: dinero adelantado al cliente (sale de caja)
            await _caja.RegistrarMovimientoAsync(
                seqCuadre:     f.SeqCuadre,
                tipoMov:       "DEBITO",
                categoria:     CategoriaMovCaja.PagoFactoring,
                monto:         f.MontoPagado,
                concepto:      $"Pago Factoring #{f.NumTicket} — {f.EmpresaNombre}",
                refDocumento:  f.NumTransaccion,
                empresaNombre: f.EmpresaNombre,
                clienteNombre: f.ClienteNombre,
                numTicket:     f.NumTicket.ToString(),
                usuario:       f.UsuarioMigracion ?? usuario
            );

            // CRÉDITO: comisión ganada (entra a caja)
            var comision = f.Monto - f.MontoPagado;
            if (comision > 0)
            {
                await _caja.RegistrarMovimientoAsync(
                    seqCuadre:     f.SeqCuadre,
                    tipoMov:       "CREDITO",
                    categoria:     CategoriaMovCaja.ComisionFactoring,
                    monto:         comision,
                    concepto:      $"Comisión factoring #{f.NumTicket} — {f.EmpresaNombre}",
                    refDocumento:  f.NumTransaccion,
                    empresaNombre: f.EmpresaNombre,
                    clienteNombre: f.ClienteNombre,
                    numTicket:     f.NumTicket.ToString(),
                    usuario:       f.UsuarioMigracion ?? usuario
                );
            }
        }

        return f;
    }

    /// <summary>
    /// Registra un abono: actualiza PagadoALaFecha y Estado en la hoja FACTURAS.
    /// </summary>
    public async Task AplicarAbonoAsync(string numTransaccion, decimal montoPagado)
    {
        var filas = await _gs.LeerAsync(HOJA_FACTURAS);
        for (int i = 1; i < filas.Count; i++)
        {
            if (GoogleSheetsService.Str(filas[i], 0) != numTransaccion) continue;

            var fac = Mapear(filas[i]);
            var snapshot = JsonSerializer.Serialize(new
            {
                fac.Estado,
                fac.PagadoALaFecha
            });

            fac.PagadoALaFecha += montoPagado;
            if (fac.PagadoALaFecha < 0) fac.PagadoALaFecha = 0;

            // Actualizar estado según lo pagado
            if (fac.PagadoALaFecha >= fac.Monto)
            {
                fac.PagadoALaFecha = fac.Monto; // no exceder el monto
                fac.Estado = EstadoFactura.Pagada;
            }
            else if (fac.PagadoALaFecha > 0)
            {
                fac.Estado = EstadoFactura.PagadoParcial;
            }
            else
            {
                fac.Estado = EstadoFactura.Pendiente;
            }

            fac.ValorAnterior = snapshot;
            await _gs.ActualizarFilaAsync(HOJA_FACTURAS, i + 1, Serializar(fac));
            return;
        }
    }

    public async Task ActualizarAsync(Factura f, string snapshotAnterior = "")
    {
        var filas = await _gs.LeerAsync(HOJA_FACTURAS);
        for (int i = 1; i < filas.Count; i++)
        {
            if (GoogleSheetsService.Str(filas[i], 0) != f.NumTransaccion) continue;
            if (!string.IsNullOrEmpty(snapshotAnterior))
                f.ValorAnterior = snapshotAnterior;
            await _gs.ActualizarFilaAsync(HOJA_FACTURAS, i + 1, Serializar(f));
            return;
        }
    }

    /// <summary>
    /// Recalcula PagadoALaFecha y Estado a partir del total pagado real (suma de todos los pagos).
    /// Usado cuando se edita o elimina un pago.
    /// </summary>
    public async Task RecalcularPagadoAsync(string numTransaccion, decimal totalPagado, string usuario = "")
    {
        var filas = await _gs.LeerAsync(HOJA_FACTURAS);
        for (int i = 1; i < filas.Count; i++)
        {
            if (GoogleSheetsService.Str(filas[i], 0) != numTransaccion) continue;
            var fac = Mapear(filas[i]);

            fac.PagadoALaFecha = totalPagado < 0 ? 0 : totalPagado;
            if (fac.PagadoALaFecha >= fac.Monto)
            {
                fac.PagadoALaFecha = fac.Monto;
                fac.Estado         = EstadoFactura.Pagada;
            }
            else if (fac.PagadoALaFecha > 0)
                fac.Estado = EstadoFactura.PagadoParcial;
            else
                fac.Estado = EstadoFactura.Pendiente;

            if (!string.IsNullOrEmpty(usuario))
            {
                fac.FechaModificacion   = DateTime.Now;
                fac.UsuarioModificacion = usuario;
            }
            await _gs.ActualizarFilaAsync(HOJA_FACTURAS, i + 1, Serializar(fac));
            return;
        }
    }

    /// <summary>
    /// Crea una entrada de tipo Crédito/Abono a cuenta en FACTURAS.
    /// El Monto es negativo (crédito disponible), SaldoPendiente será negativo.
    /// </summary>
    public async Task<Factura> CrearCreditoAsync(
        decimal montoCredito,
        int numTicket,
        string empresaNombre, string clienteNombre,
        string idCompania,   string idCliente,
        DateTime fecha,      string usuario)
    {
        var credito = new Factura
        {
            NumTicket        = numTicket,
            EmpresaNombre    = empresaNombre,
            ClienteNombre    = clienteNombre,
            IdCompania       = idCompania,
            IdCliente        = idCliente,
            Monto            = -montoCredito,   // negativo = crédito
            PagadoALaFecha   = 0,
            FechaFactura     = fecha,
            Estado           = EstadoFactura.Credito,
            Origen           = OrigenDato.Sistema,
            FechaMigracion   = DateTime.Now,
            UsuarioMigracion = usuario,
            Notas            = $"Crédito/Abono a cuenta. Instrumento: {numTicket}"
        };
        return await CrearAsync(credito);
    }

    // ─── ANULACIÓN ───────────────────────────────────────────────────────────

    /// <summary>
    /// Elimina físicamente la fila de una factura de Crédito/Abono al anular un recibo.
    /// </summary>
    public async Task EliminarPorTransaccionAsync(string numTransaccion)
    {
        if (string.IsNullOrWhiteSpace(numTransaccion)) return;
        var filas = await _gs.LeerAsync(HOJA_FACTURAS);
        var indices = new List<int>();
        for (int i = 1; i < filas.Count; i++)
            if (GoogleSheetsService.Str(filas[i], 0) == numTransaccion)
                indices.Add(i + 1);
        if (indices.Any())
            await _gs.EliminarFilasAsync(HOJA_FACTURAS, indices);
    }

    /// <summary>
    /// Revierte el efecto de un recibo sobre cada factura afectada:
    ///   - Si la factura es Credito → se elimina la fila entera.
    ///   - Si es normal → se recalcula PagadoALaFecha restando lo que pagó el recibo.
    /// </summary>
    public async Task RevertirPagosReciboAsync(
        IEnumerable<(string NumTransaccionFactura, decimal TotalPagadoPorRecibo)> afectadas,
        string usuario)
    {
        foreach (var (numTrans, totalRecibo) in afectadas)
        {
            if (string.IsNullOrWhiteSpace(numTrans)) continue;

            // Leer fila directamente para no depender de caché
            var filas = await _gs.LeerAsync(HOJA_FACTURAS);
            for (int i = 1; i < filas.Count; i++)
            {
                if (GoogleSheetsService.Str(filas[i], 0) != numTrans) continue;
                var fac = Mapear(filas[i]);

                if (fac.Estado == EstadoFactura.Credito)
                {
                    // Abono a cuenta → eliminar la factura de crédito
                    await _gs.EliminarFilasAsync(HOJA_FACTURAS, new[] { i + 1 });
                }
                else
                {
                    // Factura normal → restar lo pagado por este recibo
                    var nuevoTotal = Math.Max(0, fac.PagadoALaFecha - totalRecibo);
                    fac.PagadoALaFecha = nuevoTotal;

                    if (fac.PagadoALaFecha >= fac.Monto && fac.Monto > 0)
                    {
                        fac.PagadoALaFecha = fac.Monto;
                        fac.Estado = EstadoFactura.Pagada;
                    }
                    else if (fac.PagadoALaFecha > 0)
                        fac.Estado = EstadoFactura.PagadoParcial;
                    else
                        fac.Estado = EstadoFactura.Pendiente;

                    fac.FechaModificacion   = DateTime.Now;
                    fac.UsuarioModificacion = usuario;
                    await _gs.ActualizarFilaAsync(HOJA_FACTURAS, i + 1, Serializar(fac));
                }
                break;
            }
        }
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    public async Task<int> SiguienteTicketAsync()
    {
        var todas = await ObtenerTodasAsync();
        return todas.Count > 0 ? todas.Max(f => f.NumTicket) + 1 : 1;
    }

    private static IList<object> Encabezados() => new object[]
    {
        "NumTransaccion", "NumTicket", "Empresa", "Cliente", "Monto",
        "FechaFactura", "Estado", "PagadoALaFecha", "Notas",
        "ValorAnterior", "Origen", "FechaMigracion", "UsuarioMigracion",
        "FechaModificacion", "UsuarioModificacion",
        "IdCompania", "IdCliente",
        // v2.0
        "MontoPagado", "SeqCuadre", "FechaCreacion"
    };

    private static IList<object> Serializar(Factura f) => new object[]
    {
        f.NumTransaccion,
        f.NumTicket,
        f.EmpresaNombre,
        f.ClienteNombre,
        f.Monto.ToString("F2"),
        f.FechaFactura.ToString("yyyy-MM-dd"),
        f.Estado.ToString(),
        f.PagadoALaFecha.ToString("F2"),
        f.Notas,
        f.ValorAnterior,
        f.Origen.ToString(),
        f.FechaMigracion?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
        f.UsuarioMigracion,
        f.FechaModificacion?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
        f.UsuarioModificacion,
        f.IdCompania,
        f.IdCliente,
        // v2.0
        f.MontoPagado.ToString("F2"),
        f.SeqCuadre,
        f.FechaCreacion?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""
    };

    private static Factura Mapear(IList<object> f) => new()
    {
        NumTransaccion       = GoogleSheetsService.Str(f, 0),
        NumTicket            = GoogleSheetsService.Int(f, 1),
        EmpresaNombre        = GoogleSheetsService.Str(f, 2),
        ClienteNombre        = GoogleSheetsService.Str(f, 3),
        Monto                = GoogleSheetsService.Dec(f, 4),
        FechaFactura         = GoogleSheetsService.Fecha(f, 5) ?? DateTime.Today,
        Estado               = Enum.TryParse<EstadoFactura>(GoogleSheetsService.Str(f, 6), out var e)
                                   ? e : EstadoFactura.Pendiente,
        PagadoALaFecha       = GoogleSheetsService.Dec(f, 7),
        Notas                = GoogleSheetsService.Str(f, 8),
        ValorAnterior        = GoogleSheetsService.Str(f, 9),
        Origen               = Enum.TryParse<OrigenDato>(GoogleSheetsService.Str(f, 10), out var o)
                                   ? o : OrigenDato.Sistema,
        FechaMigracion       = GoogleSheetsService.Fecha(f, 11),
        UsuarioMigracion     = GoogleSheetsService.Str(f, 12),
        FechaModificacion    = GoogleSheetsService.Fecha(f, 13),
        UsuarioModificacion  = GoogleSheetsService.Str(f, 14),
        IdCompania           = GoogleSheetsService.Str(f, 15),
        IdCliente            = GoogleSheetsService.Str(f, 16),
        // v2.0
        MontoPagado          = GoogleSheetsService.Dec(f, 17),
        SeqCuadre            = GoogleSheetsService.Str(f, 18),
        FechaCreacion        = GoogleSheetsService.Fecha(f, 19)
    };
}
