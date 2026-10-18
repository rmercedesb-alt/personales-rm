using InmueblesAPI.Data;
using InmueblesAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace InmueblesAPI.Services;

public class InmueblesService(AppDbContext db)
{
    // ─── Utilidades ──────────────────────────────────────────────────────────

    private async Task<string> GenerarIdAsync(string prefijo, IQueryable<string> existentes)
    {
        var ids = await existentes.ToListAsync();
        int next = 1;
        while (ids.Contains($"{prefijo}-{next:D3}")) next++;
        return $"{prefijo}-{next:D3}";
    }

    public Task<string?> GetConfigAsync(string clave) =>
        db.Configuracion.Where(c => c.Clave == clave).Select(c => (string?)c.Valor).FirstOrDefaultAsync();

    public async Task SetConfigAsync(string clave, string valor)
    {
        var cfg = await db.Configuracion.FindAsync(clave);
        if (cfg == null) db.Configuracion.Add(new ConfigSistema { Clave = clave, Valor = valor });
        else cfg.Valor = valor;
        await db.SaveChangesAsync();
    }

    // ─── Propietarios ────────────────────────────────────────────────────────

    public Task<List<Propietario>> GetPropietariosAsync() =>
        db.Propietarios.OrderBy(p => p.Nombre).ToListAsync();

    public Task<Propietario?> GetPropietarioAsync(string id) =>
        db.Propietarios.FindAsync(id).AsTask();

    public async Task<Propietario> CrearPropietarioAsync(CrearPropietarioRequest req)
    {
        var id = await GenerarIdAsync("PROP", db.Propietarios.Select(p => p.Id));
        var p = new Propietario
        {
            Id = id, Nombre = req.Nombre, Cedula = req.Cedula, Telefono = req.Telefono,
            Pasaporte = req.Pasaporte ?? "",
            Email = req.Email, Banco = req.Banco, NumeroCuenta = req.NumeroCuenta,
            TipoCuenta = req.TipoCuenta, TipoComision = req.TipoComision,
            ComisionPorcentaje = req.ComisionPorcentaje, ComisionFija = req.ComisionFija,
            Estado = "ACTIVO", Notas = req.Notas ?? "", TipoDueno = req.TipoDueno ?? "ÚNICO",
            FechaRegistro = DateTime.Now
        };
        db.Propietarios.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    public async Task ActualizarPropietarioAsync(string id, ActualizarPropietarioRequest req)
    {
        var p = await db.Propietarios.FindAsync(id) ?? throw new Exception("Propietario no encontrado");
        p.Nombre = req.Nombre; p.Cedula = req.Cedula; p.Telefono = req.Telefono;
        p.Pasaporte = req.Pasaporte ?? "";
        p.Email = req.Email; p.Banco = req.Banco; p.NumeroCuenta = req.NumeroCuenta;
        p.TipoCuenta = req.TipoCuenta; p.TipoComision = req.TipoComision;
        p.ComisionPorcentaje = req.ComisionPorcentaje; p.ComisionFija = req.ComisionFija;
        p.Estado = req.Estado; p.Notas = req.Notas ?? ""; p.TipoDueno = req.TipoDueno ?? p.TipoDueno;
        await db.SaveChangesAsync();
    }

    // ─── Co-Propietarios ─────────────────────────────────────────────────────

    public Task<List<CoPropietario>> GetCoPropietariosAsync(string idPropietario) =>
        db.CoPropietarios.Where(c => c.IdPropietario == idPropietario).ToListAsync();

    public async Task<CoPropietario> CrearCoPropietarioAsync(CrearCoPropietarioRequest req)
    {
        var id = await GenerarIdAsync("COPROP", db.CoPropietarios.Select(c => c.Id));
        var c = new CoPropietario
        {
            Id = id, IdPropietario = req.IdPropietario, Nombre = req.Nombre,
            Cedula = req.Cedula, Telefono = req.Telefono, Email = req.Email,
            Banco = req.Banco, NumeroCuenta = req.NumeroCuenta, TipoCuenta = req.TipoCuenta,
            PesoPorc = req.PesoPorc, Estado = "ACTIVO", Notas = req.Notas ?? "",
            FechaRegistro = DateTime.Now
        };
        db.CoPropietarios.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    public async Task ActualizarCoPropietarioAsync(string id, ActualizarCoPropietarioRequest req)
    {
        var c = await db.CoPropietarios.FindAsync(id) ?? throw new Exception("Co-propietario no encontrado");
        c.Nombre = req.Nombre; c.Cedula = req.Cedula; c.Telefono = req.Telefono;
        c.Email = req.Email; c.Banco = req.Banco; c.NumeroCuenta = req.NumeroCuenta;
        c.TipoCuenta = req.TipoCuenta; c.PesoPorc = req.PesoPorc;
        c.Estado = req.Estado; c.Notas = req.Notas ?? "";
        await db.SaveChangesAsync();
    }

    public async Task EliminarCoPropietarioAsync(string id)
    {
        var c = await db.CoPropietarios.FindAsync(id);
        if (c != null) { db.CoPropietarios.Remove(c); await db.SaveChangesAsync(); }
    }

    // ─── Inmuebles ───────────────────────────────────────────────────────────

    public Task<List<Inmueble>> GetInmueblesAsync(bool soloDisponibles = false)
    {
        var q = db.Inmuebles.AsQueryable();
        if (soloDisponibles) q = q.Where(i => i.Estado == "DISPONIBLE");
        return q.OrderBy(i => i.Descripcion).ToListAsync();
    }

    public Task<Inmueble?> GetInmuebleAsync(string id) =>
        db.Inmuebles.FindAsync(id).AsTask();

    public async Task<Inmueble> CrearInmuebleAsync(CrearInmuebleRequest req)
    {
        var id = await GenerarIdAsync("INM", db.Inmuebles.Select(i => i.Id));
        var propietario = await GetPropietarioAsync(req.IdPropietario);
        var i = new Inmueble
        {
            Id = id, IdPropietario = req.IdPropietario,
            NombrePropietario = propietario?.Nombre ?? "",
            Tipo = req.Tipo, Descripcion = req.Descripcion, Direccion = req.Direccion,
            Sector = req.Sector, Ciudad = req.Ciudad, Latitud = req.Latitud,
            Longitud = req.Longitud, MontoAlquiler = req.MontoAlquiler,
            Estado = "DISPONIBLE", NivelMorosidad = "VERDE", Notas = req.Notas ?? "",
            FechaRegistro = DateTime.Now
        };
        db.Inmuebles.Add(i);
        await db.SaveChangesAsync();
        return i;
    }

    public async Task ActualizarInmuebleAsync(string id, ActualizarInmuebleRequest req)
    {
        var i = await db.Inmuebles.FindAsync(id) ?? throw new Exception("Inmueble no encontrado");
        var propietario = await GetPropietarioAsync(req.IdPropietario);
        i.IdPropietario = req.IdPropietario; i.NombrePropietario = propietario?.Nombre ?? "";
        i.Tipo = req.Tipo; i.Descripcion = req.Descripcion; i.Direccion = req.Direccion;
        i.Sector = req.Sector; i.Ciudad = req.Ciudad; i.Latitud = req.Latitud;
        i.Longitud = req.Longitud; i.MontoAlquiler = req.MontoAlquiler;
        i.Notas = req.Notas ?? "";
        await db.SaveChangesAsync();
    }

    public async Task ActualizarEstadoInmuebleAsync(string id, string estado)
    {
        var i = await db.Inmuebles.FindAsync(id);
        if (i != null) { i.Estado = estado; await db.SaveChangesAsync(); }
    }

    // ─── Inquilinos ──────────────────────────────────────────────────────────

    public Task<List<Inquilino>> GetInquilinosAsync() =>
        db.Inquilinos.OrderBy(i => i.Nombre).ToListAsync();

    public Task<Inquilino?> GetInquilinoAsync(string id) =>
        db.Inquilinos.FindAsync(id).AsTask();

    public async Task<Inquilino> CrearInquilinoAsync(CrearInquilinoRequest req)
    {
        var id = await GenerarIdAsync("INQ", db.Inquilinos.Select(i => i.Id));
        var i = new Inquilino
        {
            Id = id, Nombre = req.Nombre, Cedula = req.Cedula, Telefono = req.Telefono,
            Pasaporte = req.Pasaporte ?? "",
            Email = req.Email, Ocupacion = req.Ocupacion ?? "",
            ReferenciaPersonal = req.ReferenciaPersonal ?? "",
            TelefonoReferencia = req.TelefonoReferencia ?? "",
            Estado = "ACTIVO", Notas = req.Notas ?? "", FechaRegistro = DateTime.Now,
            EnviarWhatsApp = req.EnviarWhatsApp
        };
        db.Inquilinos.Add(i);
        await db.SaveChangesAsync();
        return i;
    }

    public async Task ActualizarInquilinoAsync(string id, ActualizarInquilinoRequest req)
    {
        var i = await db.Inquilinos.FindAsync(id) ?? throw new Exception("Inquilino no encontrado");
        i.Nombre = req.Nombre; i.Cedula = req.Cedula; i.Telefono = req.Telefono;
        i.Pasaporte = req.Pasaporte ?? "";
        i.Email = req.Email; i.Ocupacion = req.Ocupacion ?? ""; i.Estado = req.Estado;
        i.ReferenciaPersonal = req.ReferenciaPersonal ?? "";
        i.TelefonoReferencia = req.TelefonoReferencia ?? "";
        i.Notas = req.Notas ?? "";
        i.EnviarWhatsApp = req.EnviarWhatsApp;
        await db.SaveChangesAsync();
    }

    // ─── Contratos ───────────────────────────────────────────────────────────

    public Task<List<Contrato>> GetContratosAsync(bool soloVigentes = false)
    {
        var q = db.Contratos.AsQueryable();
        if (soloVigentes) q = q.Where(c => c.Estado == "VIGENTE");
        return q.OrderByDescending(c => c.FechaInicio).ToListAsync();
    }

    public Task<Contrato?> GetContratoAsync(string id) =>
        db.Contratos.FindAsync(id).AsTask();

    public async Task<CrearContratoResult> CrearContratoAsync(CrearContratoRequest req)
    {
        var id = await GenerarIdAsync("CTR", db.Contratos.Select(c => c.Id));
        var inmueble = await GetInmuebleAsync(req.IdInmueble);
        var inquilino = await GetInquilinoAsync(req.IdInquilino);
        var c = new Contrato
        {
            Id = id, IdInmueble = req.IdInmueble, NombreInmueble = inmueble?.Descripcion ?? "",
            IdInquilino = req.IdInquilino, NombreInquilino = inquilino?.Nombre ?? "",
            IdPropietario = inmueble?.IdPropietario ?? "",
            FechaInicio = req.FechaInicio, FechaFin = req.FechaFin,
            MontoAlquiler = req.MontoAlquiler, Periodicidad = req.Periodicidad,
            DiaCobro = req.DiaCobro, DiasGracia = req.DiasGracia,
            TipoMora = req.TipoMora, MoraPorcentaje = req.MoraPorcentaje,
            MontoMoraFija = req.MontoMoraFija, Deposito = req.Deposito,
            Estado = "VIGENTE", Notas = req.Notas ?? "", FechaRegistro = DateTime.Now
        };
        db.Contratos.Add(c);
        if (inmueble != null) { inmueble.Estado = "OCUPADO"; }

        // V6 #5 — Al crear el contrato, generar automáticamente una CxC por el DEPÓSITO
        // (vencimiento el mismo día) para poder cobrarla de inmediato. Sigue el flujo
        // normal de custodia y debe entrar en la próxima liquidación del propietario.
        CxCCargo? cxcDeposito = null;
        if (req.Deposito > 0)
        {
            var cxcId = await GenerarIdAsync("CXC", db.CxCCargos.Select(x => x.Id));
            var hoy = DateTime.Today;
            cxcDeposito = new CxCCargo
            {
                Id = cxcId, IdContrato = c.Id, IdInquilino = c.IdInquilino,
                NombreInquilino = c.NombreInquilino, IdInmueble = c.IdInmueble,
                TipoCargo = "DEPOSITO",
                Descripcion = $"Depósito de garantía - Contrato {c.Id}",
                PeriodoInicio = hoy, PeriodoFin = hoy, FechaVencimiento = hoy,
                MontoOriginal = req.Deposito, MontoMora = 0, MontoPagado = 0,
                SaldoPendiente = req.Deposito, Estado = "PENDIENTE", DiasMora = 0,
                Notas = "Depósito generado automáticamente al crear el contrato",
                FechaRegistro = DateTime.Now
            };
            db.CxCCargos.Add(cxcDeposito);
        }

        await db.SaveChangesAsync();
        return new CrearContratoResult(c, cxcDeposito?.Id, req.Deposito, c.IdInquilino);
    }

    public async Task ActualizarContratoAsync(string id, ActualizarContratoRequest req)
    {
        var c = await db.Contratos.FindAsync(id) ?? throw new Exception("Contrato no encontrado");
        var estadoAnterior = c.Estado;
        c.FechaFin = req.FechaFin; c.MontoAlquiler = req.MontoAlquiler;
        c.Periodicidad = req.Periodicidad; c.DiaCobro = req.DiaCobro;
        c.DiasGracia = req.DiasGracia; c.TipoMora = req.TipoMora;
        c.MoraPorcentaje = req.MoraPorcentaje; c.MontoMoraFija = req.MontoMoraFija;
        c.Deposito = req.Deposito; c.Estado = req.Estado; c.Notas = req.Notas ?? "";
        if (estadoAnterior == "VIGENTE" && req.Estado != "VIGENTE")
            await ActualizarEstadoInmuebleAsync(c.IdInmueble, "DISPONIBLE");
        await db.SaveChangesAsync();
    }

    // ─── CxC Cargos ──────────────────────────────────────────────────────────

    public Task<List<CxCCargo>> GetCxCCargosAsync(string? idInquilino = null, string? estado = null)
    {
        var q = db.CxCCargos.AsQueryable();
        if (idInquilino != null) q = q.Where(c => c.IdInquilino == idInquilino);
        if (estado != null)
        {
            var estados = estado.Split(',', StringSplitOptions.RemoveEmptyEntries);
            q = q.Where(c => estados.Contains(c.Estado));
        }
        return q.OrderByDescending(c => c.FechaVencimiento).ToListAsync();
    }

    public Task<CxCCargo?> GetCxCCargoAsync(string id) =>
        db.CxCCargos.FindAsync(id).AsTask();

    public async Task CrearCxCAsync(CxCCargo cxc)
    {
        db.CxCCargos.Add(cxc);
        await db.SaveChangesAsync();
    }

    public async Task<CxCCargo> CrearCxCManualAsync(CrearCxCManualRequest req)
    {
        var id = await GenerarIdAsync("CXC", db.CxCCargos.Select(c => c.Id));
        var inquilino = await GetInquilinoAsync(req.IdInquilino);
        var cargo = new CxCCargo
        {
            Id = id, IdContrato = req.IdContrato, IdInquilino = req.IdInquilino,
            NombreInquilino = inquilino?.Nombre ?? "", IdInmueble = req.IdInmueble,
            TipoCargo = req.TipoCargo, Descripcion = req.Descripcion,
            PeriodoInicio = req.PeriodoInicio, PeriodoFin = req.PeriodoFin,
            FechaVencimiento = req.FechaVencimiento, MontoOriginal = req.MontoOriginal,
            MontoMora = 0, MontoPagado = 0, SaldoPendiente = req.MontoOriginal,
            Estado = "PENDIENTE", DiasMora = 0, Notas = req.Notas ?? "",
            FechaRegistro = DateTime.Now
        };
        db.CxCCargos.Add(cargo);
        await db.SaveChangesAsync();
        return cargo;
    }

    public async Task AjustarCxCConLogAsync(string id, AjustarCxCRequest req, string usuario)
    {
        var cargo = await db.CxCCargos.FindAsync(id) ?? throw new Exception("CxC no encontrada");
        var montoAnterior = cargo.MontoOriginal;
        cargo.MontoOriginal = req.MontoOriginal;
        cargo.FechaVencimiento = req.FechaVencimiento;
        cargo.SaldoPendiente = Math.Max(0, req.MontoOriginal - cargo.MontoPagado);
        cargo.Estado = cargo.SaldoPendiente <= 0 ? "PAGADO" : cargo.MontoPagado > 0 ? "PAGADO_PARCIAL" : "PENDIENTE";

        var logId = await GenerarIdAsync("LOG", db.LogCustodia.Select(l => l.Id));
        db.LogCustodia.Add(new LogCustodia
        {
            Id = logId, IdPago = "", IdContrato = cargo.IdContrato,
            IdInquilino = cargo.IdInquilino, NombreInquilino = cargo.NombreInquilino,
            IdInmueble = cargo.IdInmueble, Monto = req.MontoOriginal,
            EstadoAnterior = "AJUSTE_CXC", EstadoNuevo = "AJUSTE_CXC",
            Motivo = $"Monto: {montoAnterior}→{req.MontoOriginal} | Venc: {cargo.FechaVencimiento:yyyy-MM-dd} | Motivo: {req.MotivoAjuste}",
            Usuario = usuario, FechaMovimiento = DateTime.Now
        });
        await db.SaveChangesAsync();
    }

    public async Task AjustarMoraCxCAsync(string id, AjustarMoraCxCRequest req, string usuario)
    {
        var cargo = await db.CxCCargos.FindAsync(id) ?? throw new Exception("CxC no encontrada");
        cargo.MontoMora = req.MontoMora;
        cargo.SaldoPendiente = Math.Max(0, cargo.MontoOriginal + req.MontoMora - cargo.MontoPagado);
        cargo.Estado = cargo.SaldoPendiente <= 0 ? "PAGADO" : cargo.MontoPagado > 0 ? "PAGADO_PARCIAL" : cargo.Estado;
        cargo.FechaUltimaActualizacionMora = DateTime.Now;

        var logId = await GenerarIdAsync("LOG", db.LogCustodia.Select(l => l.Id));
        db.LogCustodia.Add(new LogCustodia
        {
            Id = logId, IdPago = "", IdContrato = cargo.IdContrato,
            IdInquilino = cargo.IdInquilino, NombreInquilino = cargo.NombreInquilino,
            IdInmueble = cargo.IdInmueble, Monto = req.MontoMora,
            EstadoAnterior = "AJUSTE_MORA", EstadoNuevo = "AJUSTE_MORA",
            Motivo = req.MotivoAjuste, Usuario = usuario, FechaMovimiento = DateTime.Now
        });
        await db.SaveChangesAsync();
    }

    // ─── Pagos ───────────────────────────────────────────────────────────────

    public Task<List<CxCPago>> GetPagosAsync(string? idInquilino = null, string? estadoCustodia = null, string? idSesionCaja = null)
    {
        var q = db.CxCPagos.AsQueryable();
        if (idInquilino != null) q = q.Where(p => p.IdInquilino == idInquilino);
        if (estadoCustodia != null) q = q.Where(p => p.EstadoCustodia == estadoCustodia);
        if (idSesionCaja != null) q = q.Where(p => p.IdSesionCaja == idSesionCaja);
        return q.OrderByDescending(p => p.FechaPago).ToListAsync();
    }

    public Task<CxCPago?> GetPagoAsync(string id) =>
        db.CxCPagos.FindAsync(id).AsTask();

    public async Task<CxCPago> CrearPagoAsync(CxCPago pago)
    {
        db.CxCPagos.Add(pago);
        await db.SaveChangesAsync();
        return pago;
    }

    public async Task AplicarPagoCxCAsync(string idCxC, decimal montoAlquiler, decimal montoMora)
    {
        var cargo = await db.CxCCargos.FindAsync(idCxC) ?? throw new Exception("CxC no encontrada");
        cargo.MontoPagado += montoAlquiler + montoMora;
        cargo.SaldoPendiente = Math.Max(0, cargo.MontoOriginal + cargo.MontoMora - cargo.MontoPagado);
        cargo.Estado = cargo.SaldoPendiente <= 0 ? "PAGADO" : "PAGADO_PARCIAL";
        cargo.FechaUltimoPago = DateTime.Today;
        await db.SaveChangesAsync();
    }

    public async Task RestaurarSaldoCxCAsync(string idCxC, decimal montoAlquiler, decimal montoMora)
    {
        var cargo = await db.CxCCargos.FindAsync(idCxC);
        if (cargo == null) return;
        cargo.MontoPagado = Math.Max(0, cargo.MontoPagado - (montoAlquiler + montoMora));
        cargo.SaldoPendiente = Math.Max(0, cargo.MontoOriginal + cargo.MontoMora - cargo.MontoPagado);
        cargo.Estado = cargo.MontoPagado <= 0 ? "PENDIENTE" : "PAGADO_PARCIAL";
        await db.SaveChangesAsync();
    }

    public async Task AnularPagoAsync(string id, string motivo, string usuario)
    {
        var pago = await db.CxCPagos.FindAsync(id) ?? throw new Exception("Pago no encontrado");
        pago.Anulado = true; pago.MotivoAnulacion = motivo;
        await RestaurarSaldoCxCAsync(pago.IdCxC, pago.MontoAlquiler, pago.MontoMora);

        var logId = await GenerarIdAsync("LOG", db.LogCustodia.Select(l => l.Id));
        db.LogCustodia.Add(new LogCustodia
        {
            Id = logId, IdPago = id, IdContrato = pago.IdContrato,
            IdInquilino = pago.IdInquilino, NombreInquilino = pago.NombreInquilino,
            IdInmueble = pago.IdInmueble, Monto = pago.TotalPagado,
            EstadoAnterior = "COBRADO", EstadoNuevo = "ANULADO",
            Motivo = motivo, Usuario = usuario, FechaMovimiento = DateTime.Now
        });
        await db.SaveChangesAsync();
    }

    public async Task ActualizarEstadoCustodiaAsync(string idPago, string nuevoEstado, string? idLiq = null)
    {
        var pago = await db.CxCPagos.FindAsync(idPago);
        if (pago == null) return;
        pago.EstadoCustodia = nuevoEstado;
        if (idLiq != null) pago.IdLiquidacion = idLiq;
        await db.SaveChangesAsync();
    }

    // ─── Abonos Cuenta ───────────────────────────────────────────────────────

    public Task<List<AbonoCuenta>> GetAbonosCuentaAsync(string? idInquilino = null)
    {
        var q = db.AbonosCuenta.AsQueryable();
        if (idInquilino != null) q = q.Where(a => a.IdInquilino == idInquilino);
        return q.OrderByDescending(a => a.Fecha).ToListAsync();
    }

    public Task<AbonoCuenta?> GetAbonoAsync(string id) =>
        db.AbonosCuenta.FindAsync(id).AsTask();

    public async Task<AbonoCuenta> CrearAbonoAsync(AbonoCuenta abono)
    {
        db.AbonosCuenta.Add(abono);
        await db.SaveChangesAsync();
        return abono;
    }

    public async Task ActualizarAbonoAsync(string id, decimal nuevoSaldo, string estado)
    {
        var a = await db.AbonosCuenta.FindAsync(id);
        if (a == null) return;
        a.SaldoDisponible = nuevoSaldo; a.Estado = estado;
        await db.SaveChangesAsync();
    }

    // ─── Sesiones de Caja ────────────────────────────────────────────────────

    public async Task<SesionCaja?> GetSesionActivaAsync() =>
        await db.SesionesCaja.Where(s => s.Estado == "ABIERTA").FirstOrDefaultAsync();

    // Alias mantenido para compatibilidad con DashboardService
    public Task<SesionCaja?> GetSesionHoyAsync() => GetSesionActivaAsync();

    public Task<SesionCaja?> GetSesionAsync(string id) =>
        db.SesionesCaja.FindAsync(id).AsTask();

    public async Task<SesionCaja> CrearSesionAsync(string usuario, DateTime? fechaTransaccion = null)
    {
        var fecha = (fechaTransaccion ?? DateTime.Today).Date;
        if (fecha > DateTime.Today) fecha = fecha.AddDays(-1);

        var id = await GenerarIdAsync("CAJA", db.SesionesCaja.Select(s => s.Id));
        var s = new SesionCaja
        {
            Id = id,
            Fecha = fecha,
            FechaRegistro = DateTime.Now,
            Estado = "ABIERTA",
            UsuarioApertura = usuario,
            Notas = ""
        };
        db.SesionesCaja.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    public async Task AprobarCuadreAsync(string idSesion, string usuario)
    {
        var sesion = await db.SesionesCaja.FindAsync(idSesion) ?? throw new Exception("Sesión no encontrada");
        var pagos = await db.CxCPagos.Where(p => p.IdSesionCaja == idSesion && !p.Anulado).ToListAsync();
        sesion.TotalEfectivo      = pagos.Where(p => p.FormaPago == "EFECTIVO").Sum(p => p.TotalPagado);
        sesion.TotalTransferencia = pagos.Where(p => p.FormaPago == "TRANSFERENCIA").Sum(p => p.TotalPagado);
        sesion.TotalTarjeta       = pagos.Where(p => p.FormaPago == "TARJETA").Sum(p => p.TotalPagado);
        sesion.TotalCheque        = pagos.Where(p => p.FormaPago == "CHEQUE").Sum(p => p.TotalPagado);
        sesion.TotalGeneral       = pagos.Sum(p => p.TotalPagado);
        sesion.CantidadTransacciones = pagos.Count;
        sesion.Estado = "CERRADA"; sesion.UsuarioCierre = usuario; sesion.FechaCierre = DateTime.Now;

        // Mover todos los pagos COBRADO → EN_ADMINISTRACION
        foreach (var pago in pagos.Where(p => p.EstadoCustodia == "COBRADO"))
            pago.EstadoCustodia = "EN_ADMINISTRACION";

        await db.SaveChangesAsync();
    }

    // ─── Otros Ingresos (Pagos Varios) ────────────────────────────────────────
    public Task<List<CxCPago>> GetOtrosIngresosPendientesAsync() =>
        db.CxCPagos.Where(p => p.EsOtroIngreso && !p.Anulado && p.EstadoCustodia == "EN_ADMINISTRACION")
                   .OrderBy(p => p.FechaPago).ToListAsync();

    private async Task<string> GenerarIdOtiAsync()
    {
        var ids = await db.CxCPagos.Where(p => p.IdLiquidacion != null && p.IdLiquidacion.StartsWith("OTI-"))
                                   .Select(p => p.IdLiquidacion!).ToListAsync();
        int n = ids.Select(id => int.TryParse(id[4..], out int v) ? v : 0).DefaultIfEmpty(0).Max();
        return $"OTI-{(n + 1):D4}";
    }

    public async Task<OtrosIngresosLiquidacion> LiquidarOtrosIngresosAsync(List<string> idsPagos, string usuario)
    {
        if (idsPagos == null || idsPagos.Count == 0)
            throw new InvalidOperationException("Debe seleccionar al menos un ingreso.");
        var oti = await GenerarIdOtiAsync();
        var ahora = DateTime.Now;
        var pagos = await db.CxCPagos.Where(p => idsPagos.Contains(p.Id) && p.EsOtroIngreso
            && !p.Anulado && p.EstadoCustodia == "EN_ADMINISTRACION").ToListAsync();
        if (pagos.Count == 0) throw new InvalidOperationException("Ningún ingreso seleccionado está disponible para liquidar.");

        foreach (var p in pagos) { p.EstadoCustodia = "LIQUIDADO"; p.IdLiquidacion = oti; }
        await db.SaveChangesAsync();

        var items = pagos.Select(p => new OtroIngresoItem(
            p.Id, p.NombreInquilino, p.Referencia, p.Notas, p.FechaPago, p.TotalPagado, p.FormaPago)).ToList();
        return new OtrosIngresosLiquidacion(oti, ahora, items.Sum(i => i.Monto), items);
    }

    // ─── Liquidaciones ───────────────────────────────────────────────────────

    public Task<List<Liquidacion>> GetLiquidacionesAsync(string? idPropietario = null)
    {
        var q = db.Liquidaciones.AsQueryable();
        if (idPropietario != null) q = q.Where(l => l.IdPropietario == idPropietario);
        return q.OrderByDescending(l => l.FechaDesde).ToListAsync();
    }

    public async Task<string> GenerarIdLiqAsync() =>
        await GenerarIdAsync("LIQ", db.Liquidaciones.Select(l => l.Id));

    public async Task CrearLiquidacionAsync(Liquidacion liq)
    {
        db.Liquidaciones.Add(liq);
        await db.SaveChangesAsync();
    }

    public async Task<Liquidacion?> GetLiquidacionAsync(string id) =>
        await db.Liquidaciones.FindAsync(id);

    public async Task ActualizarLiquidacionAsync(Liquidacion liq)
    {
        db.Liquidaciones.Update(liq);
        await db.SaveChangesAsync();
    }

    public Task<List<Liquidacion>> BuscarLiquidacionesAsync(
        string? idPropietario, string? idLiq, DateTime? desde, DateTime? hasta)
    {
        var q = db.Liquidaciones.Where(l => !l.Anulado).AsQueryable();
        if (idPropietario != null) q = q.Where(l => l.IdPropietario == idPropietario);
        if (idLiq        != null) q = q.Where(l => l.Id == idLiq);
        if (desde        != null) q = q.Where(l => l.FechaRegistro >= desde);
        if (hasta        != null) q = q.Where(l => l.FechaRegistro <= hasta.Value.AddDays(1));
        return q.OrderByDescending(l => l.FechaRegistro).ToListAsync();
    }

    public async Task AnularLiquidacionAsync(string id, string motivo, string usuario)
    {
        var liq = await db.Liquidaciones.FindAsync(id)
            ?? throw new InvalidOperationException("Liquidación no encontrada");
        if (liq.Anulado) throw new InvalidOperationException("La liquidación ya está anulada");

        liq.Anulado          = true;
        liq.Estado           = "ANULADA";
        liq.MotivoAnulacion  = motivo;
        liq.FechaAnulacion   = DateTime.UtcNow;
        liq.UsuarioAnulacion = usuario;

        // Revertir pagos a EN_ADMINISTRACION
        var pagos = await db.CxCPagos.Where(p => p.IdLiquidacion == id).ToListAsync();
        foreach (var p in pagos)
        {
            p.EstadoCustodia = "EN_ADMINISTRACION";
            p.IdLiquidacion  = null;
        }

        // V6 #9: devolver los adelantos descontados a estado PENDIENTE
        var adelantos = await db.Adelantos.Where(a => a.IdLiquidacion == id).ToListAsync();
        foreach (var a in adelantos)
        {
            a.Estado = "PENDIENTE";
            a.IdLiquidacion = "";
        }

        await db.SaveChangesAsync();
    }

    // V6 #9: marca los adelantos como descontados en una liquidación y devuelve la suma.
    public async Task<decimal> AplicarAdelantosLiquidacionAsync(List<string> idsAdelantos, string idLiquidacion)
    {
        if (idsAdelantos == null || idsAdelantos.Count == 0) return 0m;
        var adelantos = await db.Adelantos
            .Where(a => idsAdelantos.Contains(a.Id) && a.Estado == "PENDIENTE" && (a.IdLiquidacion == null || a.IdLiquidacion == ""))
            .ToListAsync();
        decimal suma = 0m;
        foreach (var a in adelantos)
        {
            a.Estado = "LIQUIDADO";
            a.IdLiquidacion = idLiquidacion;
            suma += a.Monto;
        }
        await db.SaveChangesAsync();
        return suma;
    }

    // ─── Adelantos ───────────────────────────────────────────────────────────

    public Task<List<Adelanto>> GetAdelantosAsync(string? idPropietario = null)
    {
        var q = db.Adelantos.AsQueryable();
        if (idPropietario != null) q = q.Where(a => a.IdPropietario == idPropietario);
        return q.OrderByDescending(a => a.Fecha).ToListAsync();
    }

    public Task<Adelanto?> GetAdelantoAsync(string id) =>
        db.Adelantos.FindAsync(id).AsTask();

    public async Task<Adelanto> CrearAdelantoAsync(CrearAdelantoRequest req)
    {
        var id = await GenerarIdAsync("ADL", db.Adelantos.Select(a => a.Id));
        var propietario = await GetPropietarioAsync(req.IdPropietario);
        var a = new Adelanto
        {
            Id = id, IdPropietario = req.IdPropietario,
            NombrePropietario = propietario?.Nombre ?? "",
            Fecha = req.Fecha, Concepto = req.Concepto, Monto = req.Monto,
            FormaPago = req.FormaPago ?? "EFECTIVO",
            Estado = "PENDIENTE", Notas = req.Notas ?? "", FechaRegistro = DateTime.Now
        };
        db.Adelantos.Add(a);
        await db.SaveChangesAsync();
        return a;
    }

    // V6 #7: editar un adelanto (solo PENDIENTE)
    public async Task<Adelanto> ActualizarAdelantoAsync(string id, ActualizarAdelantoRequest req)
    {
        var a = await db.Adelantos.FindAsync(id) ?? throw new InvalidOperationException("Adelanto no encontrado");
        if (a.Estado != "PENDIENTE")
            throw new InvalidOperationException("Solo se puede editar un adelanto en estado PENDIENTE.");
        a.Fecha = req.Fecha; a.Concepto = req.Concepto; a.Monto = req.Monto;
        a.Notas = req.Notas ?? ""; a.FormaPago = req.FormaPago ?? "EFECTIVO";
        await db.SaveChangesAsync();
        return a;
    }

    // V6 #7: anular un adelanto (justificación obligatoria; NO toca caja; queda en el log)
    public async Task<Adelanto> AnularAdelantoAsync(string id, string motivo, string usuario)
    {
        var a = await db.Adelantos.FindAsync(id) ?? throw new InvalidOperationException("Adelanto no encontrado");
        if (a.Estado == "ANULADO") throw new InvalidOperationException("El adelanto ya está anulado.");
        if (a.Estado == "LIQUIDADO") throw new InvalidOperationException("No se puede anular un adelanto ya liquidado. Primero anule la liquidación.");
        if (a.Estado == "PAGADO") throw new InvalidOperationException("No se puede anular un adelanto ya pagado por el propietario.");
        if (string.IsNullOrWhiteSpace(motivo)) throw new InvalidOperationException("Debe indicar la justificación de la anulación.");

        a.Estado = "ANULADO";
        a.MotivoAnulacion = motivo;
        a.UsuarioAnulacion = usuario;
        a.FechaAnulacion = DateTime.UtcNow;

        var logId = await GenerarIdAsync("LOG", db.LogCustodia.Select(l => l.Id));
        db.LogCustodia.Add(new LogCustodia
        {
            Id = logId, IdPago = a.Id, IdContrato = "", IdInquilino = a.IdPropietario,
            NombreInquilino = a.NombrePropietario, IdInmueble = "",
            Monto = a.Monto, EstadoAnterior = "PENDIENTE", EstadoNuevo = "ANULADO",
            Motivo = $"Anulación de adelanto {a.Id}: {motivo}",
            Usuario = usuario, FechaMovimiento = DateTime.Now
        });
        await db.SaveChangesAsync();
        return a;
    }

    // ─── Documentos de adelantos (V6 #7) ──────────────────────────────────────
    public Task<List<AdelantoDocumento>> GetAdelantoDocumentosAsync(string idAdelanto) =>
        db.AdelantoDocumentos.Where(d => d.IdAdelanto == idAdelanto)
            .OrderBy(d => d.FechaSubida).ToListAsync();

    public Task<AdelantoDocumento?> GetAdelantoDocumentoAsync(string id) =>
        db.AdelantoDocumentos.FindAsync(id).AsTask();

    public async Task<AdelantoDocumento> AgregarAdelantoDocumentoAsync(
        string idAdelanto, string nombre, string mime, byte[] contenido, string usuario)
    {
        var adelanto = await db.Adelantos.FindAsync(idAdelanto)
            ?? throw new InvalidOperationException("Adelanto no encontrado");
        if (adelanto.Estado != "PENDIENTE")
            throw new InvalidOperationException("Solo se pueden adjuntar documentos a un adelanto en estado PENDIENTE.");

        var id = await GenerarIdAsync("ADOC", db.AdelantoDocumentos.Select(d => d.Id));
        var doc = new AdelantoDocumento
        {
            Id = id, IdAdelanto = idAdelanto, NombreArchivo = nombre,
            TipoMime = mime, Tamano = contenido.LongLength, Contenido = contenido,
            Usuario = usuario, FechaSubida = DateTime.Now
        };
        db.AdelantoDocumentos.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task EliminarAdelantoDocumentoAsync(string id)
    {
        var doc = await db.AdelantoDocumentos.FindAsync(id)
            ?? throw new InvalidOperationException("Documento no encontrado");
        var adelanto = await db.Adelantos.FindAsync(doc.IdAdelanto);
        if (adelanto != null && adelanto.Estado != "PENDIENTE")
            throw new InvalidOperationException("Solo se pueden eliminar documentos de un adelanto en estado PENDIENTE.");
        db.AdelantoDocumentos.Remove(doc);
        await db.SaveChangesAsync();
    }

    // V6 #7: anular un cargo CxC (solo PENDIENTE y sin pagos)
    public async Task<CxCCargo> AnularCxCAsync(string id, string motivo, string usuario)
    {
        var cargo = await db.CxCCargos.FindAsync(id) ?? throw new InvalidOperationException("Cargo no encontrado");
        if (cargo.Estado == "ANULADO") throw new InvalidOperationException("El cargo ya está anulado.");
        if (cargo.MontoPagado > 0) throw new InvalidOperationException("No se puede anular un cargo con pagos aplicados. Primero anule los pagos.");
        if (string.IsNullOrWhiteSpace(motivo)) throw new InvalidOperationException("Debe indicar la justificación de la anulación.");

        var estadoAnterior = cargo.Estado;
        cargo.Estado = "ANULADO";
        cargo.SaldoPendiente = 0;
        cargo.MotivoAnulacion = motivo;
        cargo.UsuarioAnulacion = usuario;
        cargo.FechaAnulacion = DateTime.UtcNow;

        var logId = await GenerarIdAsync("LOG", db.LogCustodia.Select(l => l.Id));
        db.LogCustodia.Add(new LogCustodia
        {
            Id = logId, IdPago = "", IdContrato = cargo.IdContrato,
            IdInquilino = cargo.IdInquilino, NombreInquilino = cargo.NombreInquilino,
            IdInmueble = cargo.IdInmueble, Monto = cargo.MontoOriginal,
            EstadoAnterior = estadoAnterior, EstadoNuevo = "ANULADO",
            Motivo = $"Anulación de cargo {cargo.Id}: {motivo}",
            Usuario = usuario, FechaMovimiento = DateTime.Now
        });
        await db.SaveChangesAsync();
        return cargo;
    }

    // ─── Usuarios ────────────────────────────────────────────────────────────

    public Task<UsuarioSistema?> GetUsuarioAsync(string email) =>
        db.Usuarios.FindAsync(email).AsTask();

    public Task<List<UsuarioSistema>> GetUsuariosAsync() =>
        db.Usuarios.OrderBy(u => u.Nombre).ToListAsync();

    public async Task CrearUsuarioAsync(string email, string nombre, string rol, string activo, string passwordHash)
    {
        db.Usuarios.Add(new UsuarioSistema { Email = email, Nombre = nombre, Rol = rol, Estado = activo, PasswordHash = passwordHash });
        await db.SaveChangesAsync();
    }

    public async Task ActualizarUsuarioAsync(string email, string nombre, string rol, string activo)
    {
        var u = await db.Usuarios.FindAsync(email);
        if (u == null) return;
        u.Nombre = nombre; u.Rol = rol; u.Estado = activo;
        await db.SaveChangesAsync();
    }

    public async Task ActualizarPasswordAsync(string email, string hash)
    {
        var u = await db.Usuarios.FindAsync(email);
        if (u == null) return;
        u.PasswordHash = hash;
        await db.SaveChangesAsync();
    }

    // ─── Motor de Mora ───────────────────────────────────────────────────────

    public async Task<int> RecalcularMorasMasivoAsync()
    {
        var hoy = DateTime.Today;
        var nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var cargos = await db.CxCCargos.Where(c => c.Estado != "PAGADO").ToListAsync();
        var contratos = await db.Contratos.ToDictionaryAsync(c => c.Id);
        int actualizados = 0;

        foreach (var cargo in cargos)
        {
            if (!contratos.TryGetValue(cargo.IdContrato, out var ctr)) continue;
            // Días vencidos = hoy - fin del ciclo (con signo).
            // Negativo mientras el ciclo no ha cerrado; positivo desde el día siguiente al cierre.
            var diasVencidos = (hoy - cargo.PeriodoFin.Date).Days;
            cargo.DiasMora = diasVencidos;                                  // se guarda con signo
            cargo.EnGracia = diasVencidos > 0 && diasVencidos <= ctr.DiasGracia;

            if (diasVencidos <= ctr.DiasGracia) { cargo.MontoMora = 0; }    // aún no vence o en gracia
            else
            {
                var diasEnMora = diasVencidos - ctr.DiasGracia;
                var tamanoPeriodo = ctr.Periodicidad == "QUINCENAL" ? 15 : 30;
                var periodosAtrasados = (int)Math.Floor((decimal)diasEnMora / tamanoPeriodo) + 1;
                cargo.MontoMora = ctr.TipoMora == "MONTO_FIJO"
                    ? ctr.MontoMoraFija * periodosAtrasados
                    : cargo.MontoOriginal * (ctr.MoraPorcentaje / 100) * periodosAtrasados;
            }
            // Aplica el descuento/condonacion de mora (monto fijo), sin bajar de 0
            if (cargo.DescuentoMora > 0)
                cargo.MontoMora = Math.Max(0, cargo.MontoMora - cargo.DescuentoMora);
            cargo.SaldoPendiente = Math.Max(0, cargo.MontoOriginal + cargo.MontoMora - cargo.MontoPagado);
            cargo.Estado = cargo.SaldoPendiente <= 0 ? "PAGADO"
                : diasVencidos > ctr.DiasGracia ? "EN_MORA"
                : cargo.MontoPagado > 0 ? "PAGADO_PARCIAL" : "PENDIENTE";
            cargo.FechaUltimaActualizacionMora = DateTime.Now;
            actualizados++;
        }
        await db.SaveChangesAsync();
        await SetConfigAsync("ULTIMA_ACTUALIZACION_MORA", nowStr);
        return actualizados;
    }

    // Aplica un descuento (monto fijo) sobre la mora de una CxC y recalcula esa cuenta.
    public async Task<CxCCargo> AplicarDescuentoMoraAsync(string idCxC, decimal monto, string motivo, string usuario)
    {
        var cargo = await db.CxCCargos.FindAsync(idCxC) ?? throw new Exception("Cuenta por cobrar no encontrada");
        if (monto < 0) throw new InvalidOperationException("El monto del descuento no puede ser negativo.");
        var ctr = await db.Contratos.FindAsync(cargo.IdContrato);

        cargo.DescuentoMora        = monto;
        cargo.MotivoDescuentoMora  = motivo ?? "";
        cargo.UsuarioDescuentoMora = usuario ?? "";
        cargo.FechaDescuentoMora   = DateTime.Now;

        // Recalcula la mora de esta cuenta respetando el descuento (días vencidos = hoy - fin del ciclo)
        if (ctr != null && cargo.Estado != "PAGADO")
        {
            var diasVencidos = (DateTime.Today - cargo.PeriodoFin.Date).Days;
            cargo.DiasMora = diasVencidos;
            cargo.EnGracia = diasVencidos > 0 && diasVencidos <= ctr.DiasGracia;
            if (diasVencidos <= ctr.DiasGracia) cargo.MontoMora = 0;
            else
            {
                var diasEnMora = diasVencidos - ctr.DiasGracia;
                var tamanoPeriodo = ctr.Periodicidad == "QUINCENAL" ? 15 : 30;
                var periodosAtrasados = (int)Math.Floor((decimal)diasEnMora / tamanoPeriodo) + 1;
                cargo.MontoMora = ctr.TipoMora == "MONTO_FIJO"
                    ? ctr.MontoMoraFija * periodosAtrasados
                    : cargo.MontoOriginal * (ctr.MoraPorcentaje / 100) * periodosAtrasados;
            }
            if (cargo.DescuentoMora > 0)
                cargo.MontoMora = Math.Max(0, cargo.MontoMora - cargo.DescuentoMora);
            cargo.SaldoPendiente = Math.Max(0, cargo.MontoOriginal + cargo.MontoMora - cargo.MontoPagado);
            cargo.Estado = cargo.SaldoPendiente <= 0 ? "PAGADO"
                : diasVencidos > ctr.DiasGracia ? "EN_MORA"
                : cargo.MontoPagado > 0 ? "PAGADO_PARCIAL" : "PENDIENTE";
        }
        await db.SaveChangesAsync();
        return cargo;
    }

    private static readonly string[] MESES =
        { "", "Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };

    public async Task<int> GenerarCargosRecurrentesAsync()
    {
        // Modo de generacion: INICIO_CICLO (def.) | FIN_CICLO | DIA_DESPUES
        var modo = await GetConfigAsync("GENERACION_CARGOS_MODO") ?? "INICIO_CICLO";
        var contratos = await db.Contratos.Where(c => c.Estado == "VIGENTE").ToListAsync();
        var hoy = DateTime.Today;
        int generados = 0;

        // Generador de IDs propio (evita duplicados al crear varios en una sola corrida)
        var idsCxc = await db.CxCCargos.Where(c => c.Id.StartsWith("CXC-")).Select(c => c.Id).ToListAsync();
        int maxNum = idsCxc.Select(id => int.TryParse(new string(id.Where(char.IsDigit).ToArray()), out var v) ? v : 0)
                           .DefaultIfEmpty(0).Max();
        string SiguienteId() => $"CXC-{++maxNum:D4}";

        // Fecha de vencimiento segun el modo elegido
        DateTime Vencimiento(DateTime cs, DateTime ce) => modo switch {
            "FIN_CICLO"   => ce,
            "DIA_DESPUES" => ce.AddDays(1),
            _             => cs,   // INICIO_CICLO
        };

        foreach (var ctr in contratos)
        {
            bool quincenal = ctr.Periodicidad == "QUINCENAL";
            var ini = ctr.FechaInicio.Date;
            var finCtr = ctr.FechaFin.Date;

            // Periodos ya existentes (para no duplicar). Se compara por PeriodoInicio.
            var existentes = await db.CxCCargos
                .Where(c => c.IdContrato == ctr.Id && c.TipoCargo == "ALQUILER")
                .Select(c => c.PeriodoInicio).ToListAsync();
            var existSet = existentes.Select(d => d.Date).ToHashSet();

            // Punto de partida: desde el ultimo periodo existente; si no hay, desde el inicio del contrato
            var ancla = existSet.Count > 0 ? existSet.Max() : new DateTime(ini.Year, ini.Month, 1);
            var cursor = new DateTime(ancla.Year, ancla.Month, 1);

            while (cursor <= hoy.AddMonths(1) && cursor <= finCtr.AddMonths(1))
            {
                int y = cursor.Year, mo = cursor.Month, dim = DateTime.DaysInMonth(y, mo);
                var ciclos = quincenal
                    ? new[] {
                        (cs: new DateTime(y,mo,1),  ce: new DateTime(y,mo,15),  desc: $"Cargo correspondiente a la 1ra Quincena del mes {MESES[mo]} {y}"),
                        (cs: new DateTime(y,mo,16), ce: new DateTime(y,mo,dim), desc: $"Cargo correspondiente a la 2da Quincena del mes {MESES[mo]} {y}")
                      }
                    : new[] {
                        (cs: new DateTime(y,mo,1),  ce: new DateTime(y,mo,dim), desc: $"Alquiler {MESES[mo]} {y}")
                      };

                foreach (var (cs, ce, desc) in ciclos)
                {
                    if (cs < ancla) continue;            // no retroceder antes del ancla
                    if (cs < ini || cs > finCtr) continue;
                    if (existSet.Contains(cs)) continue; // ya generado
                    var venc = Vencimiento(cs, ce);
                    if (venc > hoy) continue;            // aun no toca generarlo
                    db.CxCCargos.Add(new CxCCargo
                    {
                        Id = SiguienteId(), IdContrato = ctr.Id, IdInquilino = ctr.IdInquilino,
                        NombreInquilino = ctr.NombreInquilino, IdInmueble = ctr.IdInmueble,
                        TipoCargo = "ALQUILER", Descripcion = desc,
                        PeriodoInicio = cs, PeriodoFin = ce, FechaVencimiento = venc,
                        MontoOriginal = ctr.MontoAlquiler, SaldoPendiente = ctr.MontoAlquiler,
                        Estado = "PENDIENTE", Notas = "Generado automáticamente", FechaRegistro = DateTime.Now
                    });
                    existSet.Add(cs);
                    generados++;
                }
                cursor = cursor.AddMonths(1);
            }
        }
        if (generados > 0) await db.SaveChangesAsync();
        await SetConfigAsync("ULTIMA_GENERACION_CARGOS", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return generados;
    }

    public async Task<ProcesosEstado> GetProcesosEstadoAsync()
    {
        var mora   = await GetConfigAsync("ULTIMA_ACTUALIZACION_MORA");
        var cargos = await GetConfigAsync("ULTIMA_GENERACION_CARGOS");
        DateTime? dtMora   = mora   != null && DateTime.TryParse(mora,   out var m) ? m : null;
        DateTime? dtCargos = cargos != null && DateTime.TryParse(cargos, out var c) ? c : null;
        return new ProcesosEstado(dtMora, dtCargos);
    }

    // ─── Dashboard ───────────────────────────────────────────────────────────

    public Task<int> CountAsync<T>(IQueryable<T> q) => q.CountAsync();
    public IQueryable<Inmueble> InmueblesQuery => db.Inmuebles;
    public IQueryable<CxCCargo> CxCCargosQuery => db.CxCCargos;
    public IQueryable<CxCPago> CxCPagosQuery => db.CxCPagos;
    public IQueryable<Contrato> ContratosQuery => db.Contratos;

    // ─── Log Custodia ────────────────────────────────────────────────────────

    public Task<List<LogCustodia>> GetLogCustodiaAsync() =>
        db.LogCustodia.OrderByDescending(l => l.FechaMovimiento).Take(500).ToListAsync();
}
