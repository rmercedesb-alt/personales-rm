using System.Security.Cryptography;
using System.Text;
using InmueblesAPI.Models;

namespace InmueblesAPI.Services;

public class PagoService
{
    private readonly GoogleSheetsService _sheets;

    public PagoService(GoogleSheetsService sheets) => _sheets = sheets;

    public async Task<CxCPago> RegistrarPagoAsync(RegistrarPagoRequest req, string usuario)
    {
        var cxc = await _sheets.GetCxCCargoAsync(req.IdCxC)
            ?? throw new InvalidOperationException("CxC no encontrada: " + req.IdCxC);

        var sesion = await _sheets.ObtenerOCrearSesionAsync(usuario);
        var contrato = await _sheets.GetContratoAsync(cxc.IdContrato);

        var idPago = await GenerarIdPago();
        var total = req.MontoAlquiler + req.MontoMora;

        var pago = new CxCPago
        {
            Id = idPago,
            IdCxC = req.IdCxC,
            IdContrato = cxc.IdContrato,
            IdInquilino = cxc.IdInquilino,
            NombreInquilino = cxc.NombreInquilino,
            IdInmueble = cxc.IdInmueble,
            IdSesionCaja = sesion.Id,
            FechaPago = DateTime.Now,
            MontoAlquiler = req.MontoAlquiler,
            MontoMora = req.MontoMora,
            TotalPagado = total,
            FormaPago = req.FormaPago,
            Referencia = req.Referencia,
            EstadoCustodia = "COBRADO",
            Notas = req.Notas ?? ""
        };

        await _sheets.InsertarPagoAsync(pago);

        // Update CxC state
        var nuevoEstado = total >= cxc.SaldoPendiente ? "PAGADO" : "PAGADO_PARCIAL";
        await _sheets.ActualizarCxCAsync(cxc.Id, req.MontoAlquiler, req.MontoMora, nuevoEstado);

        // Update session totals
        await _sheets.ActualizarTotalesSesionAsync(sesion.Id, req.FormaPago, total);

        // Insert audit log
        await InsertarLogCustodiaAsync(pago, contrato, "NUEVO_PAGO → COBRADO", usuario);

        return pago;
    }

    public async Task<List<CxCPago>> RegistrarPagoMultipleAsync(RegistrarPagoMultipleRequest req, string usuario)
    {
        if (req.IdsCxC.Count == 0 && (req.MontoRecibido ?? 0) <= 0)
            throw new InvalidOperationException("Debe seleccionar al menos una CxC o indicar un monto recibido para abono a cuenta");

        // Cargar y validar las CxC seleccionadas, ordenadas por vencimiento (más antiguas primero)
        var cargos = new List<CxCCargo>();
        foreach (var idCxC in req.IdsCxC)
        {
            var cxc = await _sheets.GetCxCCargoAsync(idCxC)
                ?? throw new InvalidOperationException("CxC no encontrada: " + idCxC);
            if (cxc.IdInquilino != req.IdInquilino)
                throw new InvalidOperationException($"La CxC {idCxC} no pertenece al inquilino seleccionado");
            if (cxc.Estado == "PAGADO")
                throw new InvalidOperationException($"La CxC {idCxC} ya está pagada");
            cargos.Add(cxc);
        }
        cargos = cargos.OrderBy(c => c.FechaVencimiento).ToList();
        var montoNecesarioCxC = cargos.Sum(c => c.SaldoPendiente);

        var sesion = await _sheets.ObtenerOCrearSesionAsync(usuario);
        var idRecibo = await GenerarIdRecibo();

        // Aplicar abonos a cuenta seleccionados, hasta cubrir lo que falte tras el monto recibido
        var montoRecibido = req.MontoRecibido ?? montoNecesarioCxC;
        var faltaCubrir = Math.Max(0, montoNecesarioCxC - montoRecibido);
        var abonosUsados = new List<(AbonoCuenta abono, decimal montoUsado)>();
        if (faltaCubrir > 0 && req.IdsAbonosAplicar?.Count > 0)
        {
            var abonosDisponibles = await _sheets.GetAbonosCuentaAsync(req.IdInquilino);
            foreach (var idAbono in req.IdsAbonosAplicar)
            {
                if (faltaCubrir <= 0) break;
                var abono = abonosDisponibles.FirstOrDefault(a => a.Id == idAbono && a.Estado != "AGOTADO");
                if (abono == null) continue;
                var usar = Math.Min(abono.SaldoDisponible, faltaCubrir);
                if (usar <= 0) continue;
                abonosUsados.Add((abono, usar));
                faltaCubrir -= usar;
            }
        }
        var montoAbonosAplicados = abonosUsados.Sum(a => a.montoUsado);
        var fondosDisponibles = montoRecibido + montoAbonosAplicados;

        var pagos = new List<CxCPago>();
        var primeraLinea = true;
        foreach (var cxc in cargos)
        {
            if (fondosDisponibles <= 0) break;
            var contrato = await _sheets.GetContratoAsync(cxc.IdContrato);

            var pagarMora = Math.Min(fondosDisponibles, cxc.MontoMora);
            fondosDisponibles -= pagarMora;
            var deudaAlquiler = Math.Max(0, cxc.MontoOriginal - cxc.MontoPagado);
            var pagarAlquiler = Math.Min(fondosDisponibles, deudaAlquiler);
            fondosDisponibles -= pagarAlquiler;
            var total = pagarMora + pagarAlquiler;
            if (total <= 0) continue;

            var idPago = await GenerarIdPago();
            var pago = new CxCPago
            {
                Id = idPago,
                IdCxC = cxc.Id,
                IdContrato = cxc.IdContrato,
                IdInquilino = cxc.IdInquilino,
                NombreInquilino = cxc.NombreInquilino,
                IdInmueble = cxc.IdInmueble,
                IdSesionCaja = sesion.Id,
                FechaPago = DateTime.Now,
                MontoAlquiler = pagarAlquiler,
                MontoMora = pagarMora,
                TotalPagado = total,
                FormaPago = req.FormaPago,
                Referencia = req.Referencia,
                EstadoCustodia = "COBRADO",
                Notas = req.Notas ?? "",
                IdRecibo = idRecibo,
                MontoAbonoAplicado = primeraLinea ? montoAbonosAplicados : 0,
            };
            primeraLinea = false;

            await _sheets.InsertarPagoAsync(pago);
            await _sheets.AplicarPagoCxCAsync(cxc.Id, pagarAlquiler, pagarMora);
            await InsertarLogCustodiaAsync(pago, contrato, "NUEVO_PAGO → COBRADO", usuario);

            pagos.Add(pago);
        }

        // Sobrante después de cubrir todas las CxC seleccionadas (o depósito puro sin CxC) → nuevo abono a cuenta
        if (fondosDisponibles > 0.01m)
        {
            var inquilinoNombre = cargos.FirstOrDefault()?.NombreInquilino ?? "";
            if (string.IsNullOrEmpty(inquilinoNombre))
            {
                var inq = await _sheets.GetInquilinoAsync(req.IdInquilino);
                inquilinoNombre = inq?.Nombre ?? "";
            }
            await _sheets.CrearAbonoCuentaAsync(req.IdInquilino, inquilinoNombre, fondosDisponibles, idRecibo, usuario,
                "Excedente generado en recibo " + idRecibo);

            // Se registra también como línea visible/anulable del recibo
            var idPagoAbono = await GenerarIdPago();
            var pagoAbono = new CxCPago
            {
                Id = idPagoAbono,
                IdCxC = "",
                IdContrato = "",
                IdInquilino = req.IdInquilino,
                NombreInquilino = inquilinoNombre,
                IdInmueble = "",
                IdSesionCaja = sesion.Id,
                FechaPago = DateTime.Now,
                MontoAlquiler = 0,
                MontoMora = 0,
                TotalPagado = fondosDisponibles,
                FormaPago = req.FormaPago,
                Referencia = req.Referencia,
                EstadoCustodia = "COBRADO",
                Notas = "Abono a cuenta" + (req.Notas != null ? $" — {req.Notas}" : ""),
                IdRecibo = idRecibo,
            };
            await _sheets.InsertarPagoAsync(pagoAbono);
            pagos.Add(pagoAbono);
        }

        foreach (var (abono, montoUsado) in abonosUsados)
            await _sheets.AplicarAbonoAsync(abono.Id, montoUsado);

        if (montoRecibido > 0)
            await _sheets.ActualizarTotalesSesionAsync(sesion.Id, req.FormaPago, montoRecibido);

        return pagos;
    }

    public async Task AnularReciboAsync(string idRecibo, string usuario, string motivo)
    {
        var pagos = await _sheets.GetPagosAsync();
        var delRecibo = pagos.Where(p => p.IdRecibo == idRecibo && !p.Anulado).ToList();
        if (delRecibo.Count == 0) throw new InvalidOperationException("Recibo no encontrado o ya anulado");

        foreach (var pago in delRecibo)
        {
            await _sheets.AnularPagoAsync(pago.Id, motivo);
            await _sheets.RestaurarSaldoCxCAsync(pago.IdCxC, pago.MontoAlquiler, pago.MontoMora);

            var log = new LogCustodia
            {
                Id = $"LOG-{DateTime.Now:yyyyMMddHHmmss}-{pago.Id}",
                IdPago = pago.Id, IdContrato = pago.IdContrato, IdInquilino = pago.IdInquilino,
                NombreInquilino = pago.NombreInquilino, IdInmueble = pago.IdInmueble,
                Monto = pago.TotalPagado, EstadoAnterior = "COBRADO", EstadoNuevo = "ANULADO",
                Motivo = $"Anulación de pago. Motivo: {motivo}", Usuario = usuario,
                FechaMovimiento = DateTime.Now,
                HashSHA256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{pago.Id}|ANULADO|{DateTime.Now:O}")))
            };
            await _sheets.InsertarLogCustodiaAsync(log);
        }

        // Invalida cualquier abono a cuenta generado por este recibo (no se reutiliza)
        var abonosDelRecibo = await _sheets.GetAbonosCuentaAsync();
        foreach (var abono in abonosDelRecibo.Where(a => a.IdReciboOrigen == idRecibo && a.Estado != "AGOTADO"))
            await _sheets.AplicarAbonoAsync(abono.Id, abono.SaldoDisponible);
    }

    private async Task<string> GenerarIdPago()
    {
        var data = await _sheets.ReadRangeAsync($"{GoogleSheetsService.TAB_CXC_PAGOS}!A:A");
        int max = 0;
        foreach (var row in data.Skip(1))
        {
            var cell = row.Count > 0 ? row[0]?.ToString() : null;
            if (cell != null && cell.StartsWith("PAG-") && int.TryParse(cell[4..], out int n) && n > max) max = n;
        }
        return $"PAG-{(max + 1).ToString().PadLeft(4, '0')}";
    }

    private async Task<string> GenerarIdRecibo()
    {
        var data = await _sheets.ReadRangeAsync($"{GoogleSheetsService.TAB_CXC_PAGOS}!S:S");
        int max = 0;
        foreach (var row in data.Skip(1))
        {
            var cell = row.Count > 0 ? row[0]?.ToString() : null;
            if (cell != null && cell.StartsWith("REC-") && int.TryParse(cell[4..], out int n) && n > max) max = n;
        }
        return $"REC-{(max + 1).ToString().PadLeft(4, '0')}";
    }

    private async Task InsertarLogCustodiaAsync(CxCPago pago, Contrato? contrato, string motivo, string usuario)
    {
        var contenido = $"{pago.Id}|{pago.TotalPagado}|{pago.FechaPago:yyyy-MM-dd HH:mm:ss}|{motivo}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contenido)));
        var log = new LogCustodia
        {
            Id = $"LOG-{DateTime.Now:yyyyMMddHHmmss}-{pago.Id}",
            IdPago = pago.Id,
            IdContrato = pago.IdContrato,
            IdInquilino = pago.IdInquilino,
            NombreInquilino = pago.NombreInquilino,
            IdInmueble = pago.IdInmueble,
            Monto = pago.TotalPagado,
            EstadoAnterior = "",
            EstadoNuevo = "COBRADO",
            Motivo = motivo,
            Usuario = usuario,
            FechaMovimiento = DateTime.Now,
            HashSHA256 = hash
        };
        await _sheets.InsertarLogCustodiaAsync(log);
    }
}
