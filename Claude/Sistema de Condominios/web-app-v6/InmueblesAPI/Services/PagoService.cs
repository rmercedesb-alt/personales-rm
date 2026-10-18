using System.Security.Cryptography;
using System.Text;
using InmueblesAPI.Data;
using InmueblesAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace InmueblesAPI.Services;

public class PagoService(InmueblesService svc, AppDbContext db)
{
    // Zona horaria de República Dominicana (UTC-4, sin horario de verano)
    private static readonly TimeZoneInfo TzRD = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "SA Western Standard Time" : "America/Santo_Domingo");

    private static DateTime HoyLocal() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TzRD).Date;

    // ─── Registro de pago individual (endpoint legado) ────────────────────────

    public async Task<CxCPago> RegistrarPagoAsync(RegistrarPagoRequest req, string usuario)
    {
        var cxc = await svc.GetCxCCargoAsync(req.IdCxC)
            ?? throw new InvalidOperationException("CxC no encontrada: " + req.IdCxC);

        var sesion = await ObtenerSesionActivaAsync();
        var contrato = await svc.GetContratoAsync(cxc.IdContrato);
        var total = req.MontoAlquiler + req.MontoMora;

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(42001)");
            var pago = new CxCPago
            {
                Id          = await GenerarIdPagoAsync(),
                IdRecibo    = await GenerarIdReciboAsync(),
                IdCxC       = req.IdCxC, IdContrato = cxc.IdContrato,
                IdInquilino = cxc.IdInquilino, NombreInquilino = cxc.NombreInquilino,
                IdInmueble  = cxc.IdInmueble, IdSesionCaja = sesion.Id,
                FechaPago   = sesion.Fecha, MontoAlquiler = req.MontoAlquiler,
                MontoMora   = req.MontoMora, TotalPagado = total,
                FormaPago   = req.FormaPago, Referencia = req.Referencia,
                EstadoCustodia = "COBRADO", Notas = req.Notas ?? "",
                EsDeposito = cxc.TipoCargo == "DEPOSITO",
                FechaRegistro  = DateTime.Now
            };
            await svc.CrearPagoAsync(pago);
            await svc.AplicarPagoCxCAsync(cxc.Id, req.MontoAlquiler, req.MontoMora);
            await ActualizarTotalesSesionAsync(sesion.Id, req.FormaPago, total);
            await InsertarLogAsync(pago, contrato, "NUEVO_PAGO → COBRADO", usuario);
            await tx.CommitAsync();
            return pago;
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    // ─── Registro múltiple (endpoint principal) ───────────────────────────────

    public async Task<List<CxCPago>> RegistrarPagoMultipleAsync(RegistrarPagoMultipleRequest req, string usuario)
    {
        if (req.IdsCxC.Count == 0 && (req.MontoRecibido ?? 0) <= 0)
            throw new InvalidOperationException("Debe seleccionar al menos una CxC o indicar un monto recibido");

        // Validar CxC
        var cargos = new List<CxCCargo>();
        foreach (var idCxC in req.IdsCxC)
        {
            var cxc = await svc.GetCxCCargoAsync(idCxC)
                ?? throw new InvalidOperationException("CxC no encontrada: " + idCxC);
            if (cxc.IdInquilino != req.IdInquilino)
                throw new InvalidOperationException($"La CxC {idCxC} no pertenece al inquilino seleccionado");
            if (cxc.Estado == "PAGADO")
                throw new InvalidOperationException($"La CxC {idCxC} ya está pagada");
            cargos.Add(cxc);
        }
        cargos = cargos.OrderBy(c => c.FechaVencimiento).ToList();

        var sesion = await ObtenerSesionActivaAsync();

        // Validar fecha de transacción (FIX #7: bloquear fechas futuras)
        var fechaTx = req.FechaTransaccion.HasValue
            ? req.FechaTransaccion.Value.Date
            : sesion.Fecha.Date;
        if (fechaTx > HoyLocal())
            throw new InvalidOperationException(
                $"La fecha de transacción ({fechaTx:dd/MM/yyyy}) no puede ser mayor al día de hoy.");

        // Validar y normalizar descuentos de mora
        var descuentosMora = req.DescuentosMora ?? new Dictionary<string, decimal>();
        foreach (var cxc in cargos)
        {
            var desc = descuentosMora.GetValueOrDefault(cxc.Id, 0m);
            if (desc < 0)
                throw new InvalidOperationException($"El descuento de mora para {cxc.Id} no puede ser negativo.");
            if (desc > cxc.MontoMora)
                throw new InvalidOperationException($"El descuento de mora ({desc:N2}) supera la mora ({cxc.MontoMora:N2}) de {cxc.Id}.");
        }

        // Monto necesario respeta los descuentos de mora acordados
        var montoNecesario = cargos.Sum(c =>
            Math.Max(0, c.MontoOriginal - c.MontoPagado) +
            Math.Max(0, c.MontoMora - descuentosMora.GetValueOrDefault(c.Id, 0m)));
        var montoRecibido  = req.MontoRecibido ?? montoNecesario;
        var faltaCubrir    = Math.Max(0, montoNecesario - montoRecibido);

        // Calcular abonos a aplicar (fuera de transacción para leer disponibilidad actual)
        var abonosUsados = new List<(AbonoCuenta abono, decimal monto)>();
        if (faltaCubrir > 0 && req.IdsAbonosAplicar?.Count > 0)
        {
            var abonosDisp = await svc.GetAbonosCuentaAsync(req.IdInquilino);
            foreach (var idAbono in req.IdsAbonosAplicar)
            {
                if (faltaCubrir <= 0) break;
                var abono = abonosDisp.FirstOrDefault(a => a.Id == idAbono && a.Estado != "AGOTADO");
                if (abono == null) continue;
                var usar = Math.Min(abono.SaldoDisponible, faltaCubrir);
                if (usar <= 0) continue;
                abonosUsados.Add((abono, usar));
                faltaCubrir -= usar;
            }
        }

        // FIX #1: Toda la operación dentro de una sola transacción de DB
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // FIX #4: Lock de asesoría para evitar condición de carrera en IDs
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(42001)");

            var idRecibo = await GenerarIdReciboAsync();
            var fondos   = montoRecibido + abonosUsados.Sum(a => a.monto);
            var pagos    = new List<CxCPago>();
            var primera  = true;

            foreach (var cxc in cargos)
            {
                if (fondos <= 0) break;
                var contrato = await svc.GetContratoAsync(cxc.IdContrato);

                // FIX #8: Alquiler primero, luego mora (respetando descuento acordado)
                var moraEfectiva  = Math.Max(0, cxc.MontoMora - descuentosMora.GetValueOrDefault(cxc.Id, 0m));
                var pagarAlquiler = Math.Min(fondos, Math.Max(0, cxc.MontoOriginal - cxc.MontoPagado));
                fondos -= pagarAlquiler;
                var pagarMora = Math.Min(fondos, moraEfectiva);
                fondos -= pagarMora;
                var total = pagarAlquiler + pagarMora;
                if (total <= 0) continue;

                var pago = new CxCPago
                {
                    Id = await GenerarIdPagoAsync(), IdCxC = cxc.Id,
                    IdContrato = cxc.IdContrato, IdInquilino = cxc.IdInquilino,
                    NombreInquilino = cxc.NombreInquilino, IdInmueble = cxc.IdInmueble,
                    IdSesionCaja = sesion.Id, FechaPago = fechaTx,
                    MontoAlquiler = pagarAlquiler, MontoMora = pagarMora, TotalPagado = total,
                    FormaPago = req.FormaPago, Referencia = req.Referencia,
                    EstadoCustodia = "COBRADO", Notas = req.Notas ?? "",
                    EsDeposito = cxc.TipoCargo == "DEPOSITO",
                    IdRecibo = idRecibo,
                    MontoAbonoAplicado = primera ? abonosUsados.Sum(a => a.monto) : 0,
                    FechaRegistro = DateTime.Now
                };
                primera = false;

                await svc.CrearPagoAsync(pago);
                await svc.AplicarPagoCxCAsync(cxc.Id, pagarAlquiler, pagarMora);
                await InsertarLogAsync(pago, contrato, "NUEVO_PAGO → COBRADO", usuario);
                pagos.Add(pago);
            }

            // Excedente → abono a cuenta
            if (fondos > 0.01m)
            {
                var nombre = cargos.FirstOrDefault()?.NombreInquilino
                    ?? (await svc.GetInquilinoAsync(req.IdInquilino))?.Nombre ?? "";
                await CrearAbonoCuentaAsync(req.IdInquilino, nombre, fondos, idRecibo, usuario,
                    "Excedente generado en recibo " + idRecibo);

                var pagoAbono = new CxCPago
                {
                    Id = await GenerarIdPagoAsync(), IdCxC = "", IdContrato = "",
                    IdInquilino = req.IdInquilino, NombreInquilino = nombre,
                    IdInmueble = "", IdSesionCaja = sesion.Id, FechaPago = fechaTx,
                    MontoAlquiler = 0, MontoMora = 0, TotalPagado = fondos,
                    FormaPago = req.FormaPago, Referencia = req.Referencia,
                    EstadoCustodia = "COBRADO",
                    Notas = "Abono a cuenta" + (req.Notas != null ? $" — {req.Notas}" : ""),
                    IdRecibo = idRecibo, FechaRegistro = DateTime.Now
                };
                await svc.CrearPagoAsync(pagoAbono);
                pagos.Add(pagoAbono);
            }

            foreach (var (abono, montoUsado) in abonosUsados)
                await AplicarAbonoAsync(abono.Id, montoUsado);

            if (montoRecibido > 0)
                await ActualizarTotalesSesionAsync(sesion.Id, req.FormaPago, montoRecibido);

            await tx.CommitAsync();
            return pagos;
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    // ─── Anulación de recibo ──────────────────────────────────────────────────

    public async Task AnularReciboAsync(string idRecibo, string usuario, string motivo)
    {
        var delRecibo = await db.CxCPagos
            .Where(p => p.IdRecibo == idRecibo && !p.Anulado)
            .ToListAsync();
        if (delRecibo.Count == 0)
            throw new InvalidOperationException("Recibo no encontrado o ya anulado");

        // FIX #3: Bloquear anulación si ya fue procesado en administración
        var estado = delRecibo.First().EstadoCustodia;
        if (estado is "EN_ADMINISTRACION" or "LIQUIDADO")
            throw new InvalidOperationException(
                $"No se puede anular el recibo {idRecibo}: ya fue procesado (estado: {estado}). " +
                "Contacte al administrador para revertirlo.");

        // FIX #2: Revertir totales de caja si la sesión sigue abierta
        var sesionActiva = await svc.GetSesionActivaAsync();
        if (sesionActiva != null && delRecibo.Any(p => p.IdSesionCaja == sesionActiva.Id))
        {
            var formaPago     = delRecibo.First().FormaPago;
            var totalAbonoApl = delRecibo.Sum(p => p.MontoAbonoAplicado);
            var montoRevertir = delRecibo.Sum(p => p.TotalPagado) - totalAbonoApl;
            if (montoRevertir > 0)
                await RevertirTotalesSesionAsync(sesionActiva.Id, formaPago, montoRevertir);
        }

        foreach (var pago in delRecibo)
        {
            pago.Anulado = true;
            pago.MotivoAnulacion = motivo;
            if (!string.IsNullOrEmpty(pago.IdCxC))
                await svc.RestaurarSaldoCxCAsync(pago.IdCxC, pago.MontoAlquiler, pago.MontoMora);

            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{pago.Id}|ANULADO|{DateTime.Now:O}")));
            db.LogCustodia.Add(new LogCustodia
            {
                Id = $"LOG-{DateTime.Now:yyyyMMddHHmmssfff}-{pago.Id}",
                IdPago = pago.Id, IdContrato = pago.IdContrato, IdInquilino = pago.IdInquilino,
                NombreInquilino = pago.NombreInquilino, IdInmueble = pago.IdInmueble,
                Monto = pago.TotalPagado, EstadoAnterior = "COBRADO", EstadoNuevo = "ANULADO",
                Motivo = $"Anulación. Motivo: {motivo}", Usuario = usuario,
                FechaMovimiento = DateTime.Now, HashSHA256 = hash
            });
        }

        // Restaurar abonos de cuenta que se generaron como excedente de este recibo
        var abonosDelRecibo = await db.AbonosCuenta
            .Where(a => a.IdReciboOrigen == idRecibo && a.Estado != "AGOTADO")
            .ToListAsync();
        foreach (var abono in abonosDelRecibo)
            await AplicarAbonoAsync(abono.Id, abono.SaldoDisponible);

        await db.SaveChangesAsync();
    }

    // ─── Privados ─────────────────────────────────────────────────────────────

    // ─── Pago Vario / Otro Ingreso ────────────────────────────────────────────
    public async Task<CxCPago> CrearPagoVarioAsync(PagoVarioRequest req, string usuario)
    {
        if (req.Monto <= 0) throw new InvalidOperationException("El monto debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(req.Nombre)) throw new InvalidOperationException("Indique el nombre de la persona.");
        var sesion = await ObtenerSesionActivaAsync();
        var forma = string.IsNullOrWhiteSpace(req.FormaPago) ? "EFECTIVO" : req.FormaPago;

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(42001)");
            var pago = new CxCPago
            {
                Id = await GenerarIdPagoAsync(), IdRecibo = await GenerarIdReciboAsync(),
                IdCxC = "", IdContrato = "", IdInquilino = "",
                NombreInquilino = req.Nombre, IdInmueble = "", IdSesionCaja = sesion.Id,
                FechaPago = sesion.Fecha, MontoAlquiler = 0, MontoMora = 0, TotalPagado = req.Monto,
                FormaPago = forma, Referencia = req.Cedula ?? "", EstadoCustodia = "COBRADO",
                Notas = req.Concepto ?? "", FechaRegistro = DateTime.Now, EsOtroIngreso = true
            };
            await svc.CrearPagoAsync(pago);
            await ActualizarTotalesSesionAsync(sesion.Id, forma, req.Monto);
            await tx.CommitAsync();
            return pago;
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    // ─── V6 #7: pago/devolución de un adelanto por el propietario → ingreso a caja ──
    public async Task<CxCPago> PagarAdelantoAsync(string idAdelanto, string formaPago, string referencia, string usuario)
    {
        var adelanto = await svc.GetAdelantoAsync(idAdelanto)
            ?? throw new InvalidOperationException("Adelanto no encontrado");
        if (adelanto.Estado != "PENDIENTE")
            throw new InvalidOperationException("Solo se puede registrar el pago de un adelanto en estado PENDIENTE.");

        var sesion = await ObtenerSesionActivaAsync();
        var forma = string.IsNullOrWhiteSpace(formaPago) ? "EFECTIVO" : formaPago;

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(42001)");
            var pago = new CxCPago
            {
                Id = await GenerarIdPagoAsync(), IdRecibo = await GenerarIdReciboAsync(),
                IdCxC = "", IdContrato = "", IdInquilino = "",
                NombreInquilino = adelanto.NombrePropietario, IdInmueble = "", IdSesionCaja = sesion.Id,
                FechaPago = sesion.Fecha, MontoAlquiler = 0, MontoMora = 0, TotalPagado = adelanto.Monto,
                FormaPago = forma, Referencia = referencia ?? "", EstadoCustodia = "COBRADO",
                Notas = $"Devolución de adelanto {adelanto.Id} — {adelanto.Concepto}".Trim(),
                FechaRegistro = DateTime.Now, EsOtroIngreso = true
            };
            await svc.CrearPagoAsync(pago);
            await ActualizarTotalesSesionAsync(sesion.Id, forma, adelanto.Monto);

            var adl = await db.Adelantos.FindAsync(idAdelanto);
            if (adl != null)
            {
                adl.Estado = "PAGADO";
                adl.IdSesionCajaPago = sesion.Id;
                adl.FechaPago = DateTime.Now;
            }

            db.LogCustodia.Add(new LogCustodia
            {
                Id = $"LOG-{DateTime.Now:yyyyMMddHHmmssfff}-{pago.Id}",
                IdPago = pago.Id, IdContrato = "", IdInquilino = adelanto.IdPropietario,
                NombreInquilino = adelanto.NombrePropietario, IdInmueble = "",
                Monto = adelanto.Monto, EstadoAnterior = "PENDIENTE", EstadoNuevo = "PAGADO",
                Motivo = $"Pago/devolución de adelanto {adelanto.Id} (ingreso a caja {sesion.Id})",
                Usuario = usuario, FechaMovimiento = DateTime.Now
            });

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return pago;
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    private async Task<SesionCaja> ObtenerSesionActivaAsync() =>
        await svc.GetSesionActivaAsync()
            ?? throw new InvalidOperationException(
                "La caja está cerrada. El administrador debe abrirla antes de registrar pagos.");

    private async Task ActualizarTotalesSesionAsync(string idSesion, string formaPago, decimal monto)
    {
        var sesion = await db.SesionesCaja.FindAsync(idSesion);
        if (sesion == null) return;
        AplicarMontoPorForma(sesion, formaPago, monto);
        sesion.TotalGeneral += monto;
        sesion.CantidadTransacciones++;
        await db.SaveChangesAsync();
    }

    // FIX #2: Versión específica para reversar (evita que CantidadTransacciones quede negativa)
    private async Task RevertirTotalesSesionAsync(string idSesion, string formaPago, decimal monto)
    {
        var sesion = await db.SesionesCaja.FindAsync(idSesion);
        if (sesion == null) return;
        AplicarMontoPorForma(sesion, formaPago, -monto);
        sesion.TotalGeneral = Math.Max(0, sesion.TotalGeneral - monto);
        sesion.CantidadTransacciones = Math.Max(0, sesion.CantidadTransacciones - 1);
        await db.SaveChangesAsync();
    }

    private static void AplicarMontoPorForma(SesionCaja sesion, string formaPago, decimal delta)
    {
        switch (formaPago)
        {
            case "EFECTIVO":      sesion.TotalEfectivo      += delta; break;
            case "TRANSFERENCIA": sesion.TotalTransferencia += delta; break;
            case "TARJETA":       sesion.TotalTarjeta       += delta; break;
            case "CHEQUE":        sesion.TotalCheque        += delta; break;
        }
    }

    // FIX #5: Usar fecha local (UTC-4) en vez de DateTime.Today (UTC)
    private async Task CrearAbonoCuentaAsync(
        string idInquilino, string nombre, decimal monto,
        string idRecibo, string usuario, string notas)
    {
        var id = $"ABONO-{DateTime.Now:yyyyMMddHHmmssfff}";
        db.AbonosCuenta.Add(new AbonoCuenta
        {
            Id = id, IdInquilino = idInquilino, NombreInquilino = nombre,
            Fecha = HoyLocal(), MontoOriginal = monto, SaldoDisponible = monto,
            Estado = "DISPONIBLE", IdReciboOrigen = idRecibo, Usuario = usuario,
            Notas = notas, FechaRegistro = DateTime.Now
        });
        await db.SaveChangesAsync();
    }

    private async Task AplicarAbonoAsync(string id, decimal montoUsado)
    {
        var abono = await db.AbonosCuenta.FindAsync(id);
        if (abono == null) return;
        abono.SaldoDisponible -= montoUsado;
        abono.Estado = abono.SaldoDisponible <= 0 ? "AGOTADO" : "PARCIAL";
        await db.SaveChangesAsync();
    }

    private async Task InsertarLogAsync(CxCPago pago, Contrato? contrato, string motivo, string usuario)
    {
        var contenido = $"{pago.Id}|{pago.TotalPagado}|{pago.FechaPago:yyyy-MM-dd}|{motivo}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contenido)));
        db.LogCustodia.Add(new LogCustodia
        {
            Id = $"LOG-{DateTime.Now:yyyyMMddHHmmssfff}-{pago.Id}",
            IdPago = pago.Id, IdContrato = pago.IdContrato, IdInquilino = pago.IdInquilino,
            NombreInquilino = pago.NombreInquilino, IdInmueble = pago.IdInmueble,
            Monto = pago.TotalPagado, EstadoAnterior = "", EstadoNuevo = "COBRADO",
            Motivo = motivo, Usuario = usuario, FechaMovimiento = DateTime.Now, HashSHA256 = hash
        });
        await db.SaveChangesAsync();
    }

    // FIX #4: Los métodos de generación de ID son seguros solo cuando se llaman
    // dentro de una transacción con pg_advisory_xact_lock(42001) activo.
    private async Task<string> GenerarIdPagoAsync()
    {
        var ids = await db.CxCPagos
            .Where(p => p.Id.StartsWith("PAG-"))
            .Select(p => p.Id)
            .ToListAsync();
        int n = ids.Select(id => int.TryParse(id[4..], out int v) ? v : 0).DefaultIfEmpty(0).Max();
        return $"PAG-{(n + 1):D4}";
    }

    private async Task<string> GenerarIdReciboAsync()
    {
        var ids = await db.CxCPagos
            .Where(p => p.IdRecibo.StartsWith("REC-"))
            .Select(p => p.IdRecibo)
            .Distinct()
            .ToListAsync();
        int n = ids.Select(id => int.TryParse(id[4..], out int v) ? v : 0).DefaultIfEmpty(0).Max();
        return $"REC-{(n + 1):D4}";
    }
}
