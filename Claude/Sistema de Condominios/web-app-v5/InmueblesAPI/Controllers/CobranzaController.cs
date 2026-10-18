using System.Security.Claims;
using InmueblesAPI.Models;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InmueblesAPI.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class CobranzaController : ControllerBase
{
    private readonly InmueblesService _svc;
    private readonly PagoService _pagos;

    public CobranzaController(InmueblesService sheets, PagoService pagos)
    {
        _svc = sheets;
        _pagos = pagos;
    }

    // ─── CXC ────────────────────────────────────────────────────────────────
    [HttpGet("cxc")]
    public async Task<ActionResult<ApiResponse<List<CxCCargo>>>> GetCxC(
        [FromQuery] string? idInquilino, [FromQuery] string? estado)
    {
        var list = await _svc.GetCxCCargosAsync(idInquilino, estado);
        return Ok(new ApiResponse<List<CxCCargo>>(true, null, list));
    }

    [HttpGet("cxc/{id}")]
    public async Task<ActionResult<ApiResponse<CxCCargo>>> GetCxCById(string id)
    {
        var item = await _svc.GetCxCCargoAsync(id);
        if (item == null) return NotFound(new ApiResponse<CxCCargo>(false, "No encontrado", null));
        return Ok(new ApiResponse<CxCCargo>(true, null, item));
    }

    [HttpPost("cxc")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<CxCCargo>>> CrearCxCManual([FromBody] CrearCxCManualRequest req)
    {
        var cargo = await _svc.CrearCxCManualAsync(req);
        return Ok(new ApiResponse<CxCCargo>(true, "CxC creada correctamente", cargo));
    }

    [HttpPut("cxc/{id}")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<string>>> AjustarCxC(string id, [FromBody] AjustarCxCRequest req)
    {
        var cargo = await _svc.GetCxCCargoAsync(id);
        if (cargo == null) return NotFound(new ApiResponse<string>(false, "CxC no encontrada", null));
        if (cargo.Estado == "PAGADO") return BadRequest(new ApiResponse<string>(false, "No se puede ajustar una CxC completamente pagada", null));
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        await _svc.AjustarCxCConLogAsync(id, req, usuario);
        return Ok(new ApiResponse<string>(true, "Ajuste registrado con log", id));
    }

    [HttpPut("cxc/{id}/mora")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<string>>> AjustarMora(string id, [FromBody] AjustarMoraCxCRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.MotivoAjuste))
            return BadRequest(new ApiResponse<string>(false, "Debe indicar el motivo del ajuste", null));
        var cargo = await _svc.GetCxCCargoAsync(id);
        if (cargo == null) return NotFound(new ApiResponse<string>(false, "CxC no encontrada", null));
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        await _svc.AjustarMoraCxCAsync(id, req, usuario);
        return Ok(new ApiResponse<string>(true, "Mora ajustada", id));
    }

    // Descuento / condonacion de mora (monto fijo), respetado por el motor de mora
    [HttpPut("cxc/{id}/descuento-mora")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<CxCCargo>>> DescontarMora(string id, [FromBody] DescuentoMoraRequest req)
    {
        if (req.Monto < 0)
            return BadRequest(new ApiResponse<CxCCargo>(false, "El monto del descuento no puede ser negativo", null));
        if (string.IsNullOrWhiteSpace(req.Motivo))
            return BadRequest(new ApiResponse<CxCCargo>(false, "Debe indicar el motivo del descuento", null));
        try
        {
            var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
            var cargo = await _svc.AplicarDescuentoMoraAsync(id, req.Monto, req.Motivo, usuario);
            return Ok(new ApiResponse<CxCCargo>(true, "Descuento de mora aplicado", cargo));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<CxCCargo>(false, ex.Message, null));
        }
    }

    // ─── PAGOS ───────────────────────────────────────────────────────────────
    [HttpGet("pagos")]
    public async Task<ActionResult<ApiResponse<List<CxCPago>>>> GetPagos(
        [FromQuery] string? idInquilino, [FromQuery] string? estadoCustodia, [FromQuery] string? idSesionCaja)
    {
        var list = await _svc.GetPagosAsync(idInquilino, estadoCustodia, idSesionCaja);
        return Ok(new ApiResponse<List<CxCPago>>(true, null, list));
    }

    [HttpPost("pagos")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<CxCPago>>> RegistrarPago([FromBody] RegistrarPagoRequest req)
    {
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        var pago = await _pagos.RegistrarPagoAsync(req, usuario);
        return Ok(new ApiResponse<CxCPago>(true, "Pago registrado correctamente", pago));
    }

    [HttpPost("pagos/multiple")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<List<CxCPago>>>> RegistrarPagoMultiple([FromBody] RegistrarPagoMultipleRequest req)
    {
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        var pagos = await _pagos.RegistrarPagoMultipleAsync(req, usuario);
        return Ok(new ApiResponse<List<CxCPago>>(true, $"Recibo {pagos.First().IdRecibo} registrado con {pagos.Count} cargo(s)", pagos));
    }

    [HttpGet("abonos")]
    public async Task<ActionResult<ApiResponse<List<AbonoCuenta>>>> GetAbonos([FromQuery] string? idInquilino)
    {
        var list = await _svc.GetAbonosCuentaAsync(idInquilino);
        return Ok(new ApiResponse<List<AbonoCuenta>>(true, null, list));
    }

    [HttpPost("pagos/recibo/{idRecibo}/anular")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<IActionResult> AnularRecibo(string idRecibo, [FromBody] AnularPagoRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.MotivoAnulacion))
            return BadRequest(new ApiResponse<object>(false, "Debe indicar el motivo de la anulación", null));
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        await _pagos.AnularReciboAsync(idRecibo, usuario, req.MotivoAnulacion);
        return Ok(new ApiResponse<object>(true, "Recibo anulado correctamente", null));
    }

    // ─── PAGOS VARIOS / OTROS INGRESOS ────────────────────────────────────────
    [HttpPost("pagos/otro-ingreso")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<CxCPago>>> CrearPagoVario([FromBody] PagoVarioRequest req)
    {
        try
        {
            var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
            var pago = await _pagos.CrearPagoVarioAsync(req, usuario);
            return Ok(new ApiResponse<CxCPago>(true, "Pago vario registrado", pago));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<CxCPago>(false, ex.Message, null));
        }
    }

    [HttpGet("otros-ingresos/pendientes")]
    public async Task<ActionResult<ApiResponse<List<OtroIngresoItem>>>> GetOtrosIngresosPendientes()
    {
        var pagos = await _svc.GetOtrosIngresosPendientesAsync();
        var items = pagos.Select(p => new OtroIngresoItem(
            p.Id, p.NombreInquilino, p.Referencia, p.Notas, p.FechaPago, p.TotalPagado, p.FormaPago)).ToList();
        return Ok(new ApiResponse<List<OtroIngresoItem>>(true, null, items));
    }

    [HttpPost("otros-ingresos/liquidar")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<OtrosIngresosLiquidacion>>> LiquidarOtrosIngresos([FromBody] LiquidarOtrosIngresosRequest req)
    {
        try
        {
            var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
            var result = await _svc.LiquidarOtrosIngresosAsync(req.IdsPagos, usuario);
            return Ok(new ApiResponse<OtrosIngresosLiquidacion>(true, "Otros ingresos liquidados", result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<OtrosIngresosLiquidacion>(false, ex.Message, null));
        }
    }

    // ─── SESION CAJA ─────────────────────────────────────────────────────────
    [HttpGet("caja/sesion-activa")]
    public async Task<ActionResult<ApiResponse<SesionCaja?>>> GetSesionActiva()
    {
        var sesion = await _svc.GetSesionActivaAsync();
        return Ok(new ApiResponse<SesionCaja?>(true, null, sesion));
    }

    [HttpPost("caja/abrir")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<SesionCaja>>> AbrirCaja([FromBody] AbrirCajaRequest req)
    {
        var activa = await _svc.GetSesionActivaAsync();
        if (activa != null)
            return BadRequest(new ApiResponse<SesionCaja>(false, $"Ya hay una sesión abierta: {activa.Id} (fecha trabajo: {activa.Fecha:dd/MM/yyyy}). Ciérrela primero.", null));
        var usuario = User.FindFirst(ClaimTypes.Name)?.Value ?? "sistema";
        var sesion = await _svc.CrearSesionAsync(usuario, req.FechaTransaccion);
        return Ok(new ApiResponse<SesionCaja>(true, $"Caja abierta para el {sesion.Fecha:dd/MM/yyyy}.", sesion));
    }

    [HttpPost("caja/aprobar")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<string>>> AprobarCuadre([FromBody] AprobarCuadreRequest req)
    {
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        await _svc.AprobarCuadreAsync(req.IdSesion, usuario);
        return Ok(new ApiResponse<string>(true, "Caja cerrada. Pagos movidos a EN_ADMINISTRACIÓN.", req.IdSesion));
    }

    // ─── LIQUIDACIONES ────────────────────────────────────────────────────────
    [HttpGet("liquidaciones")]
    public async Task<ActionResult<ApiResponse<List<Liquidacion>>>> GetLiquidaciones([FromQuery] string? idPropietario)
    {
        var list = await _svc.GetLiquidacionesAsync(idPropietario);
        return Ok(new ApiResponse<List<Liquidacion>>(true, null, list));
    }

    [HttpGet("liquidaciones/pendientes")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<List<PropietarioPendiente>>>> GetPendientesLiquidacion()
    {
        var pagos      = await _svc.GetPagosAsync(estadoCustodia: "EN_ADMINISTRACION");
        var inmuebles  = await _svc.GetInmueblesAsync();
        var props      = (await _svc.GetPropietariosAsync()).ToDictionary(p => p.Id);
        var inmMap     = inmuebles.ToDictionary(i => i.Id);

        var grupos = new Dictionary<string, (string Nombre, List<PagoEnAdminDetalle> Lista)>();

        foreach (var pago in pagos.Where(p => !p.Anulado))
        {
            string? idProp = null;
            string desc    = pago.IdInmueble;

            if (!string.IsNullOrEmpty(pago.IdInmueble) && inmMap.TryGetValue(pago.IdInmueble, out var inm))
            {
                idProp = inm.IdPropietario;
                desc   = inm.Descripcion;
            }

            if (idProp == null && !string.IsNullOrEmpty(pago.IdCxC))
            {
                var cxc = await _svc.GetCxCCargoAsync(pago.IdCxC);
                if (cxc != null)
                {
                    var contrato = await _svc.GetContratoAsync(cxc.IdContrato);
                    idProp = contrato?.IdPropietario;
                    if (idProp != null && !string.IsNullOrEmpty(contrato!.IdInmueble) && inmMap.TryGetValue(contrato.IdInmueble, out var inm2))
                        desc = inm2.Descripcion;
                }
            }

            if (idProp == null) continue;

            var nombre = props.TryGetValue(idProp, out var pr) ? pr.Nombre : idProp;
            if (!grupos.ContainsKey(idProp)) grupos[idProp] = (nombre, new List<PagoEnAdminDetalle>());

            grupos[idProp].Lista.Add(new PagoEnAdminDetalle(
                pago.Id, pago.IdRecibo, pago.FechaPago,
                pago.MontoAlquiler, pago.MontoMora, pago.TotalPagado,
                desc, pago.IdCxC ?? "", pago.FormaPago));
        }

        var result = grupos
            .Select(g => new PropietarioPendiente(
                g.Key, g.Value.Nombre,
                g.Value.Lista.Count,
                g.Value.Lista.Sum(p => p.MontoAlquiler),
                g.Value.Lista.OrderByDescending(p => p.FechaPago).ToList()))
            .OrderBy(g => g.NombrePropietario)
            .ToList();

        return Ok(new ApiResponse<List<PropietarioPendiente>>(true, null, result));
    }

    [HttpGet("liquidaciones/preview")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<LiquidacionPreview>>> PreviewLiquidacion(
        [FromQuery] string idPropietario,
        [FromQuery] DateTime fechaDesde,
        [FromQuery] DateTime fechaHasta)
    {
        var propietario = await _svc.GetPropietarioAsync(idPropietario)
            ?? throw new InvalidOperationException("Propietario no encontrado");

        var pagosEnAdmin = await _svc.GetPagosAsync(estadoCustodia: "EN_ADMINISTRACION");
        var inmuebles = await _svc.GetInmueblesAsync();
        var inmuebleMap = inmuebles.ToDictionary(i => i.Id, i => i.Descripcion);

        var lineas = new List<LiquidacionPreviewLinea>();
        foreach (var pago in pagosEnAdmin.Where(p => !p.Anulado).OrderBy(p => p.FechaPago))
        {
            if (pago.FechaPago < fechaDesde || pago.FechaPago > fechaHasta.AddDays(1)) continue;
            if (string.IsNullOrEmpty(pago.IdCxC)) continue;
            var cxc = await _svc.GetCxCCargoAsync(pago.IdCxC);
            if (cxc == null) continue;
            var contrato = await _svc.GetContratoAsync(cxc.IdContrato);
            if (contrato?.IdPropietario != idPropietario) continue;

            var desc = inmuebleMap.TryGetValue(pago.IdInmueble, out var d) ? d : pago.IdInmueble;
            var comLinea = propietario.TipoComision == "PORCENTAJE" || propietario.TipoComision == "MIXTO"
                ? Math.Round(pago.MontoAlquiler * propietario.ComisionPorcentaje / 100, 2)
                : 0m;

            lineas.Add(new LiquidacionPreviewLinea(
                pago.Id, pago.IdRecibo, pago.IdCxC, pago.IdInmueble, desc,
                pago.FechaPago, pago.MontoAlquiler, pago.MontoMora, comLinea, pago.MontoAlquiler - comLinea));
        }

        var totalAlquiler = lineas.Sum(l => l.MontoAlquiler);
        var totalMora     = lineas.Sum(l => l.MontoMora);
        var totalComision = propietario.TipoComision switch
        {
            "FIJO"  => propietario.ComisionFija,
            "MIXTO" => lineas.Sum(l => l.ComisionLinea) + propietario.ComisionFija,
            _       => lineas.Sum(l => l.ComisionLinea)
        };
        var totalNeto = totalAlquiler - totalComision;

        var preview = new LiquidacionPreview(
            propietario, lineas, totalAlquiler, totalMora, totalComision, totalNeto,
            propietario.ComisionPorcentaje, propietario.ComisionFija, propietario.TipoComision,
            fechaDesde, fechaHasta);
        return Ok(new ApiResponse<LiquidacionPreview>(true, null, preview));
    }

    [HttpPost("liquidaciones")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<Liquidacion>>> ProcesarLiquidacion([FromBody] ProcesarLiquidacionRequest req)
    {
        var propietario = await _svc.GetPropietarioAsync(req.IdPropietario)
            ?? throw new InvalidOperationException("Propietario no encontrado");

        var pagosEnAdmin = await _svc.GetPagosAsync(estadoCustodia: "EN_ADMINISTRACION");
        var pagosDelPropietario = new List<CxCPago>();

        foreach (var pago in pagosEnAdmin.Where(p => !p.Anulado))
        {
            if (pago.FechaPago < req.FechaDesde || pago.FechaPago > req.FechaHasta.AddDays(1)) continue;
            if (string.IsNullOrEmpty(pago.IdCxC)) continue;
            var cxc = await _svc.GetCxCCargoAsync(pago.IdCxC);
            if (cxc == null) continue;
            var contrato = await _svc.GetContratoAsync(cxc.IdContrato);
            if (contrato?.IdPropietario == req.IdPropietario)
                pagosDelPropietario.Add(pago);
        }

        if (pagosDelPropietario.Count == 0)
            return BadRequest(new ApiResponse<Liquidacion>(false,
                "No hay pagos en estado EN_ADMINISTRACIÓN para este propietario en el rango de fechas indicado.", null));

        // Commission base: MontoAlquiler only (mora belongs to administrator)
        var totalAlquiler = pagosDelPropietario.Sum(p => p.MontoAlquiler);
        var comision = propietario.TipoComision switch
        {
            "FIJO"  => propietario.ComisionFija,
            "MIXTO" => Math.Round(totalAlquiler * propietario.ComisionPorcentaje / 100, 2) + propietario.ComisionFija,
            _       => Math.Round(totalAlquiler * propietario.ComisionPorcentaje / 100, 2)
        };

        var idLiq = await _svc.GenerarIdLiqAsync();
        var liq = new Liquidacion
        {
            Id = idLiq,
            IdPropietario = req.IdPropietario,
            NombrePropietario = propietario.Nombre,
            FechaDesde = req.FechaDesde,
            FechaHasta = req.FechaHasta,
            TotalCobrado = totalAlquiler,
            Comision = comision,
            NetoPropietario = totalAlquiler - comision,
            Estado = "PENDIENTE",
            FechaRegistro = DateTime.Now
        };
        await _svc.CrearLiquidacionAsync(liq);

        foreach (var pago in pagosDelPropietario)
            await _svc.ActualizarEstadoCustodiaAsync(pago.Id, "LIQUIDADO", idLiq);

        return Ok(new ApiResponse<Liquidacion>(true, "Liquidación procesada", liq));
    }

    // ─── V5: Nuevos endpoints de Liquidaciones ───────────────────────────────

    // Pagos EN_ADMINISTRACION de un propietario con detalle para liquidar
    [HttpGet("liquidaciones/v5/pagos-pendientes/{idPropietario}")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<PropietarioPendienteLiquidacion>>> GetPagosPendientes(string idPropietario)
    {
        var propietario = await _svc.GetPropietarioAsync(idPropietario);
        if (propietario == null)
            return NotFound(new ApiResponse<PropietarioPendienteLiquidacion>(false, "Propietario no encontrado", null));

        var pagosAdmin  = await _svc.GetPagosAsync(estadoCustodia: "EN_ADMINISTRACION");
        var inmuebles   = (await _svc.GetInmueblesAsync()).ToDictionary(i => i.Id, i => i.Descripcion);
        var contratos   = (await _svc.GetContratosAsync()).ToDictionary(c => c.Id);

        var lineas = new List<PagoPendienteLiquidacion>();
        foreach (var pago in pagosAdmin.Where(p => !p.Anulado).OrderBy(p => p.FechaPago))
        {
            if (string.IsNullOrEmpty(pago.IdCxC)) continue;
            var cxc      = await _svc.GetCxCCargoAsync(pago.IdCxC);
            if (cxc == null) continue;
            var contrato = contratos.GetValueOrDefault(cxc.IdContrato);
            if (contrato?.IdPropietario != idPropietario) continue;

            var desc = inmuebles.TryGetValue(pago.IdInmueble, out var d) ? d : pago.IdInmueble;
            var com  = propietario.TipoComision switch {
                "FIJO"  => 0m,
                "MIXTO" => Math.Round(pago.MontoAlquiler * propietario.ComisionPorcentaje / 100, 2),
                _       => Math.Round(pago.MontoAlquiler * propietario.ComisionPorcentaje / 100, 2)
            };

            lineas.Add(new PagoPendienteLiquidacion(
                pago.Id, pago.IdRecibo, pago.IdCxC,
                pago.IdInquilino, pago.NombreInquilino,
                desc, pago.FechaPago, pago.FormaPago,
                pago.MontoAlquiler, pago.MontoMora,
                com, pago.MontoAlquiler - com));
        }

        // Comisión fija se aplica una sola vez al total si tipo = FIJO o MIXTO
        var totalAlq = lineas.Sum(l => l.MontoAlquiler);
        var totalCom = propietario.TipoComision switch {
            "FIJO"  => propietario.ComisionFija,
            "MIXTO" => lineas.Sum(l => l.ComisionLinea) + propietario.ComisionFija,
            _       => lineas.Sum(l => l.ComisionLinea)
        };

        var result = new PropietarioPendienteLiquidacion(
            propietario.Id, propietario.Nombre, propietario.Telefono,
            propietario.TipoComision, propietario.ComisionPorcentaje, propietario.ComisionFija,
            lineas, totalAlq, totalCom, totalAlq - totalCom);

        return Ok(new ApiResponse<PropietarioPendienteLiquidacion>(true, null, result));
    }

    // Siguiente número de liquidación (para mostrar en pantalla de confirmación)
    [HttpGet("liquidaciones/v5/siguiente-id")]
    public async Task<ActionResult<ApiResponse<string>>> SiguienteIdLiquidacion()
    {
        var id = await _svc.GenerarIdLiqAsync();
        return Ok(new ApiResponse<string>(true, null, id));
    }

    // Generar liquidación con pagos seleccionados
    [HttpPost("liquidaciones/v5/generar")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<LiquidacionDetalle>>> GenerarLiquidacionV5(
        [FromBody] GenerarLiquidacionRequest req)
    {
        if (req.IdsPagos == null || req.IdsPagos.Count == 0)
            return BadRequest(new ApiResponse<LiquidacionDetalle>(false, "Debe seleccionar al menos un pago.", null));

        var propietario = await _svc.GetPropietarioAsync(req.IdPropietario);
        if (propietario == null)
            return NotFound(new ApiResponse<LiquidacionDetalle>(false, "Propietario no encontrado", null));

        var inmuebles = (await _svc.GetInmueblesAsync()).ToDictionary(i => i.Id, i => i.Descripcion);
        var lineas    = new List<LiquidacionLineaDetalle>();
        var pagosUsados = new List<string>();

        foreach (var idPago in req.IdsPagos)
        {
            var pago = (await _svc.GetPagosAsync()).FirstOrDefault(p => p.Id == idPago);
            if (pago == null || pago.EstadoCustodia != "EN_ADMINISTRACION" || pago.Anulado) continue;

            var desc = inmuebles.TryGetValue(pago.IdInmueble, out var d) ? d : pago.IdInmueble;
            var com  = propietario.TipoComision switch {
                "FIJO"  => 0m,
                "MIXTO" => Math.Round(pago.MontoAlquiler * propietario.ComisionPorcentaje / 100, 2),
                _       => Math.Round(pago.MontoAlquiler * propietario.ComisionPorcentaje / 100, 2)
            };

            lineas.Add(new LiquidacionLineaDetalle(
                pago.Id, pago.IdRecibo, pago.IdInquilino, pago.NombreInquilino,
                desc, pago.FechaPago, pago.FormaPago, pago.MontoAlquiler, com, pago.MontoAlquiler - com));
            pagosUsados.Add(pago.Id);
        }

        if (lineas.Count == 0)
            return BadRequest(new ApiResponse<LiquidacionDetalle>(false, "Ningún pago seleccionado está disponible.", null));

        var totalAlq  = lineas.Sum(l => l.MontoAlquiler);
        var totalCom  = propietario.TipoComision switch {
            "FIJO"  => propietario.ComisionFija,
            "MIXTO" => lineas.Sum(l => l.ComisionLinea) + propietario.ComisionFija,
            _       => lineas.Sum(l => l.ComisionLinea)
        };

        var idLiq = await _svc.GenerarIdLiqAsync();
        // Fecha elegida por el usuario (o fecha local si no se envia), fijada a mediodia UTC
        // para que al mostrarse en zona horaria local nunca cambie de dia.
        var fechaBase = (req.Fecha ?? DateTime.Now).Date;
        var ahora = DateTime.SpecifyKind(fechaBase.AddHours(12), DateTimeKind.Utc);
        var liq   = new Liquidacion {
            Id                = idLiq,
            IdPropietario     = propietario.Id,
            NombrePropietario = propietario.Nombre,
            FechaDesde        = lineas.Min(l => l.FechaPago),
            FechaHasta        = lineas.Max(l => l.FechaPago),
            TotalCobrado      = totalAlq,
            Comision          = totalCom,
            NetoPropietario   = totalAlq - totalCom,
            Estado            = "GENERADA",
            FechaRegistro     = ahora
        };

        await _svc.CrearLiquidacionAsync(liq);
        foreach (var idP in pagosUsados)
            await _svc.ActualizarEstadoCustodiaAsync(idP, "LIQUIDADO", idLiq);

        return Ok(new ApiResponse<LiquidacionDetalle>(true, "Liquidación generada", new LiquidacionDetalle(liq, propietario, lineas)));
    }

    // Anular liquidación
    [HttpPut("liquidaciones/{id}/anular")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<string>>> AnularLiquidacion(string id, [FromBody] AnularLiquidacionRequest req)
    {
        try
        {
            var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
            await _svc.AnularLiquidacionAsync(id, req.MotivoAnulacion, usuario);
            return Ok(new ApiResponse<string>(true, "Liquidación anulada. Los pagos volvieron a EN_ADMINISTRACIÓN.", id));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<string>(false, ex.Message, null));
        }
    }

    // Detalle de una liquidación (encabezado + líneas)
    [HttpGet("liquidaciones/{id}/detalle")]
    public async Task<ActionResult<ApiResponse<LiquidacionDetalle>>> GetLiquidacionDetalle(string id)
    {
        var liq = await _svc.GetLiquidacionAsync(id);
        if (liq == null) return NotFound(new ApiResponse<LiquidacionDetalle>(false, "No encontrada", null));

        var propietario = await _svc.GetPropietarioAsync(liq.IdPropietario);
        var inmuebles   = (await _svc.GetInmueblesAsync()).ToDictionary(i => i.Id, i => i.Descripcion);
        var pagos       = (await _svc.GetPagosAsync()).Where(p => p.IdLiquidacion == id).OrderBy(p => p.FechaPago).ToList();

        var comPorc = propietario?.ComisionPorcentaje ?? 0;
        var lineas  = pagos.Select(p => {
            var desc = inmuebles.TryGetValue(p.IdInmueble, out var d) ? d : p.IdInmueble;
            var com  = Math.Round(p.MontoAlquiler * comPorc / 100, 2);
            return new LiquidacionLineaDetalle(
                p.Id, p.IdRecibo, p.IdInquilino, p.NombreInquilino,
                desc, p.FechaPago, p.FormaPago, p.MontoAlquiler, com, p.MontoAlquiler - com);
        }).ToList();

        return Ok(new ApiResponse<LiquidacionDetalle>(true, null, new LiquidacionDetalle(liq, propietario!, lineas)));
    }

    // Buscar liquidaciones con filtros
    [HttpGet("liquidaciones/v5/buscar")]
    public async Task<ActionResult<ApiResponse<List<LiquidacionListItem>>>> BuscarLiquidaciones(
        [FromQuery] string? idPropietario,
        [FromQuery] string? idLiq,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] bool incluirAnuladas = false)
    {
        var lista = await _svc.BuscarLiquidacionesAsync(idPropietario, idLiq, desde, hasta);
        if (!incluirAnuladas) lista = lista.Where(l => !l.Anulado).ToList();

        var pagos    = await _svc.GetPagosAsync();
        var pagosPorLiq = pagos.GroupBy(p => p.IdLiquidacion ?? "").ToDictionary(g => g.Key, g => g.Count());

        var result = lista.Select(l => new LiquidacionListItem(
            l.Id, l.IdPropietario, l.NombrePropietario, l.FechaRegistro,
            l.TotalCobrado, l.Comision, l.NetoPropietario, l.Estado, l.Anulado,
            pagosPorLiq.TryGetValue(l.Id, out var cnt) ? cnt : 0
        )).ToList();

        return Ok(new ApiResponse<List<LiquidacionListItem>>(true, null, result));
    }

    // Todos los propietarios con pagos EN_ADMINISTRACION pendientes de liquidar
    [HttpGet("liquidaciones/v5/pagos-pendientes-todos")]
    public async Task<ActionResult<ApiResponse<List<PropietarioPendienteLiquidacion>>>> GetPagosPendientesTodos()
    {
        var pagosAdmin = (await _svc.GetPagosAsync(estadoCustodia: "EN_ADMINISTRACION"))
            .Where(p => !p.Anulado).ToList();

        if (pagosAdmin.Count == 0)
            return Ok(new ApiResponse<List<PropietarioPendienteLiquidacion>>(true, null, new List<PropietarioPendienteLiquidacion>()));

        var inmuebles  = (await _svc.GetInmueblesAsync()).ToDictionary(i => i.Id, i => i.Descripcion);
        var contratos  = (await _svc.GetContratosAsync()).ToDictionary(c => c.Id);
        var propietariosIds = contratos.Values
            .Where(c => pagosAdmin.Any(p => !string.IsNullOrEmpty(p.IdCxC)))
            .Select(c => c.IdPropietario).Distinct().ToList();

        // Agrupar pagos por propietario
        var grupos = new Dictionary<string, List<CxCPago>>();
        var cxcCache = new Dictionary<string, CxCCargo?>();

        foreach (var pago in pagosAdmin.OrderBy(p => p.FechaPago))
        {
            if (string.IsNullOrEmpty(pago.IdCxC)) continue;
            if (!cxcCache.ContainsKey(pago.IdCxC))
                cxcCache[pago.IdCxC] = await _svc.GetCxCCargoAsync(pago.IdCxC);
            var cxc = cxcCache[pago.IdCxC];
            if (cxc == null) continue;
            var contrato = contratos.GetValueOrDefault(cxc.IdContrato);
            if (contrato == null) continue;
            var idProp = contrato.IdPropietario;
            if (!grupos.ContainsKey(idProp)) grupos[idProp] = new List<CxCPago>();
            grupos[idProp].Add(pago);
        }

        var result = new List<PropietarioPendienteLiquidacion>();
        foreach (var (idProp, pagos) in grupos)
        {
            var prop = await _svc.GetPropietarioAsync(idProp);
            if (prop == null) continue;

            var lineas = pagos.Select(pago => {
                var desc = inmuebles.TryGetValue(pago.IdInmueble, out var d) ? d : pago.IdInmueble;
                var com  = prop.TipoComision switch {
                    "FIJO"  => 0m,
                    _       => Math.Round(pago.MontoAlquiler * prop.ComisionPorcentaje / 100, 2)
                };
                return new PagoPendienteLiquidacion(
                    pago.Id, pago.IdRecibo, pago.IdCxC,
                    pago.IdInquilino, pago.NombreInquilino,
                    desc, pago.FechaPago, pago.FormaPago,
                    pago.MontoAlquiler, pago.MontoMora, com, pago.MontoAlquiler - com);
            }).ToList();

            var totalAlq = lineas.Sum(l => l.MontoAlquiler);
            var totalCom = prop.TipoComision switch {
                "FIJO"  => prop.ComisionFija,
                "MIXTO" => lineas.Sum(l => l.ComisionLinea) + prop.ComisionFija,
                _       => lineas.Sum(l => l.ComisionLinea)
            };

            result.Add(new PropietarioPendienteLiquidacion(
                prop.Id, prop.Nombre, prop.Telefono,
                prop.TipoComision, prop.ComisionPorcentaje, prop.ComisionFija,
                lineas, totalAlq, totalCom, totalAlq - totalCom));
        }

        result = result.OrderBy(r => r.NombrePropietario).ToList();
        return Ok(new ApiResponse<List<PropietarioPendienteLiquidacion>>(true, null, result));
    }
}
