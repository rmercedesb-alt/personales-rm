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
}
