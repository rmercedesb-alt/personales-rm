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
    private readonly GoogleSheetsService _sheets;
    private readonly PagoService _pagos;

    public CobranzaController(GoogleSheetsService sheets, PagoService pagos)
    {
        _sheets = sheets;
        _pagos = pagos;
    }

    // ─── CXC ────────────────────────────────────────────────────────────────
    [HttpGet("cxc")]
    public async Task<ActionResult<ApiResponse<List<CxCCargo>>>> GetCxC(
        [FromQuery] string? idInquilino, [FromQuery] string? estado)
    {
        var list = await _sheets.GetCxCCargosAsync(idInquilino, estado);
        return Ok(new ApiResponse<List<CxCCargo>>(true, null, list));
    }

    [HttpGet("cxc/{id}")]
    public async Task<ActionResult<ApiResponse<CxCCargo>>> GetCxCById(string id)
    {
        var item = await _sheets.GetCxCCargoAsync(id);
        if (item == null) return NotFound(new ApiResponse<CxCCargo>(false, "No encontrado", null));
        return Ok(new ApiResponse<CxCCargo>(true, null, item));
    }

    [HttpPost("cxc")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<CxCCargo>>> CrearCxCManual([FromBody] CrearCxCManualRequest req)
    {
        var cargo = await _sheets.CrearCxCManualAsync(req);
        return Ok(new ApiResponse<CxCCargo>(true, "CxC creada correctamente", cargo));
    }

    [HttpPut("cxc/{id}")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<string>>> AjustarCxC(string id, [FromBody] AjustarCxCRequest req)
    {
        var cargo = await _sheets.GetCxCCargoAsync(id);
        if (cargo == null) return NotFound(new ApiResponse<string>(false, "CxC no encontrada", null));
        if (cargo.Estado == "PAGADO") return BadRequest(new ApiResponse<string>(false, "No se puede ajustar una CxC completamente pagada", null));
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        await _sheets.AjustarCxCConLogAsync(id, req, usuario);
        return Ok(new ApiResponse<string>(true, "Ajuste registrado con log", id));
    }

    [HttpPut("cxc/{id}/mora")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<string>>> AjustarMora(string id, [FromBody] AjustarMoraCxCRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.MotivoAjuste))
            return BadRequest(new ApiResponse<string>(false, "Debe indicar el motivo del ajuste", null));
        var cargo = await _sheets.GetCxCCargoAsync(id);
        if (cargo == null) return NotFound(new ApiResponse<string>(false, "CxC no encontrada", null));
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        await _sheets.AjustarMoraCxCAsync(id, req, usuario);
        return Ok(new ApiResponse<string>(true, "Mora ajustada", id));
    }

    // ─── PAGOS ───────────────────────────────────────────────────────────────
    [HttpGet("pagos")]
    public async Task<ActionResult<ApiResponse<List<CxCPago>>>> GetPagos(
        [FromQuery] string? idInquilino, [FromQuery] string? estadoCustodia, [FromQuery] string? idSesionCaja)
    {
        var list = await _sheets.GetPagosAsync(idInquilino, estadoCustodia, idSesionCaja);
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
        var list = await _sheets.GetAbonosCuentaAsync(idInquilino);
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
        var sesion = await _sheets.GetSesionHoyAsync();
        return Ok(new ApiResponse<SesionCaja?>(true, null, sesion));
    }

    [HttpPost("caja/aprobar")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<string>>> AprobarCuadre([FromBody] AprobarCuadreRequest req)
    {
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        await _sheets.AprobarCuadreAsync(req.IdSesion, usuario);
        return Ok(new ApiResponse<string>(true, "Cuadre aprobado. Pagos movidos a EN_ADMINISTRACIÓN.", req.IdSesion));
    }

    // ─── LIQUIDACIONES ────────────────────────────────────────────────────────
    [HttpGet("liquidaciones")]
    public async Task<ActionResult<ApiResponse<List<Liquidacion>>>> GetLiquidaciones([FromQuery] string? idPropietario)
    {
        var list = await _sheets.GetLiquidacionesAsync(idPropietario);
        return Ok(new ApiResponse<List<Liquidacion>>(true, null, list));
    }

    [HttpPost("liquidaciones")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<Liquidacion>>> ProcesarLiquidacion([FromBody] ProcesarLiquidacionRequest req)
    {
        var propietario = await _sheets.GetPropietarioAsync(req.IdPropietario)
            ?? throw new InvalidOperationException("Propietario no encontrado");

        var pagosEnAdmin = await _sheets.GetPagosAsync(estadoCustodia: "EN_ADMINISTRACION");
        var pagosDelPropietario = new List<CxCPago>();

        foreach (var pago in pagosEnAdmin)
        {
            if (pago.FechaPago >= req.FechaDesde && pago.FechaPago <= req.FechaHasta)
            {
                // Verify the payment belongs to this owner via contract
                var cxc = await _sheets.GetCxCCargoAsync(pago.IdCxC);
                if (cxc != null)
                {
                    var contrato = await _sheets.GetContratoAsync(cxc.IdContrato);
                    if (contrato?.IdPropietario == req.IdPropietario)
                        pagosDelPropietario.Add(pago);
                }
            }
        }

        var totalCobrado = pagosDelPropietario.Sum(p => p.TotalPagado);
        var comision = propietario.TipoComision == "PORCENTAJE"
            ? totalCobrado * propietario.ComisionPorcentaje / 100
            : propietario.TipoComision == "FIJO"
                ? propietario.ComisionFija
                : totalCobrado * propietario.ComisionPorcentaje / 100 + propietario.ComisionFija;

        var idLiq = await _sheets.GenerarIdUnicoAsync("LIQ", GoogleSheetsService.TAB_LIQUIDACIONES);
        var liq = new Liquidacion
        {
            Id = idLiq,
            IdPropietario = req.IdPropietario,
            NombrePropietario = propietario.Nombre,
            FechaDesde = req.FechaDesde,
            FechaHasta = req.FechaHasta,
            TotalCobrado = totalCobrado,
            Comision = comision,
            NetoPropietario = totalCobrado - comision,
            Estado = "PENDIENTE",
            FechaRegistro = DateTime.Now
        };
        await _sheets.CrearLiquidacionAsync(liq);

        foreach (var pago in pagosDelPropietario)
            await _sheets.ActualizarEstadoCustodiaAsync(pago.Id, "LIQUIDADO", idLiq);

        return Ok(new ApiResponse<Liquidacion>(true, "Liquidación procesada", liq));
    }
}
