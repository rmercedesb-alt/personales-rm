using InmueblesAPI.Models;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InmueblesAPI.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class MaestrosController : ControllerBase
{
    private readonly GoogleSheetsService _sheets;
    public MaestrosController(GoogleSheetsService sheets) => _sheets = sheets;

    // ─── PROPIETARIOS ───────────────────────────────────────────────────────
    [HttpGet("propietarios")]
    public async Task<ActionResult<ApiResponse<List<Propietario>>>> GetPropietarios()
    {
        var list = await _sheets.GetPropietariosAsync();
        return Ok(new ApiResponse<List<Propietario>>(true, null, list));
    }

    [HttpGet("propietarios/{id}")]
    public async Task<ActionResult<ApiResponse<Propietario>>> GetPropietario(string id)
    {
        var item = await _sheets.GetPropietarioAsync(id);
        if (item == null) return NotFound(new ApiResponse<Propietario>(false, "No encontrado", null));
        return Ok(new ApiResponse<Propietario>(true, null, item));
    }

    [HttpPost("propietarios")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<Propietario>>> CrearPropietario([FromBody] CrearPropietarioRequest req)
    {
        var item = await _sheets.CrearPropietarioAsync(req);
        return Ok(new ApiResponse<Propietario>(true, "Propietario creado", item));
    }

    [HttpPut("propietarios/{id}")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<IActionResult> ActualizarPropietario(string id, [FromBody] ActualizarPropietarioRequest req)
    {
        await _sheets.ActualizarPropietarioAsync(id, req);
        return Ok(new ApiResponse<object>(true, "Propietario actualizado", null));
    }

    // ─── CO-PROPIETARIOS ────────────────────────────────────────────────────
    [HttpGet("propietarios/{idPropietario}/copropietarios")]
    public async Task<ActionResult<ApiResponse<List<CoPropietario>>>> GetCoPropietarios(string idPropietario)
    {
        var list = await _sheets.GetCoPropietariosAsync(idPropietario);
        return Ok(new ApiResponse<List<CoPropietario>>(true, null, list.Where(c => c.Estado == "ACTIVO").ToList()));
    }

    [HttpPost("propietarios/{idPropietario}/copropietarios")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<CoPropietario>>> CrearCoPropietario(string idPropietario, [FromBody] CrearCoPropietarioRequest req)
    {
        var existing = await _sheets.GetCoPropietariosAsync(idPropietario);
        var totalPeso = existing.Where(c => c.Estado == "ACTIVO").Sum(c => c.PesoPorc) + req.PesoPorc;
        if (totalPeso > 100)
            return BadRequest(new ApiResponse<CoPropietario>(false, $"La suma de pesos excedería 100% (actual: {totalPeso - req.PesoPorc}%)", null));
        var item = await _sheets.CrearCoPropietarioAsync(req with { IdPropietario = idPropietario });
        return Ok(new ApiResponse<CoPropietario>(true, "Co-propietario creado", item));
    }

    [HttpPut("copropietarios/{id}")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<IActionResult> ActualizarCoPropietario(string id, [FromBody] ActualizarCoPropietarioRequest req)
    {
        await _sheets.ActualizarCoPropietarioAsync(id, req);
        return Ok(new ApiResponse<object>(true, "Co-propietario actualizado", null));
    }

    [HttpDelete("copropietarios/{id}")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<IActionResult> EliminarCoPropietario(string id)
    {
        await _sheets.EliminarCoPropietarioAsync(id);
        return Ok(new ApiResponse<object>(true, "Co-propietario eliminado", null));
    }

    // ─── INMUEBLES ──────────────────────────────────────────────────────────
    [HttpGet("inmuebles")]
    public async Task<ActionResult<ApiResponse<List<Inmueble>>>> GetInmuebles([FromQuery] bool soloDisponibles = false)
    {
        var list = await _sheets.GetInmueblesAsync(soloDisponibles);
        return Ok(new ApiResponse<List<Inmueble>>(true, null, list));
    }

    [HttpGet("inmuebles/{id}")]
    public async Task<ActionResult<ApiResponse<Inmueble>>> GetInmueble(string id)
    {
        var item = await _sheets.GetInmuebleAsync(id);
        if (item == null) return NotFound(new ApiResponse<Inmueble>(false, "No encontrado", null));
        return Ok(new ApiResponse<Inmueble>(true, null, item));
    }

    [HttpPost("inmuebles")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<Inmueble>>> CrearInmueble([FromBody] CrearInmuebleRequest req)
    {
        var item = await _sheets.CrearInmuebleAsync(req);
        return Ok(new ApiResponse<Inmueble>(true, "Inmueble creado", item));
    }

    [HttpPut("inmuebles/{id}")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<IActionResult> ActualizarInmueble(string id, [FromBody] ActualizarInmuebleRequest req)
    {
        await _sheets.ActualizarInmuebleAsync(id, req);
        return Ok(new ApiResponse<object>(true, "Inmueble actualizado", null));
    }

    // ─── INQUILINOS ─────────────────────────────────────────────────────────
    [HttpGet("inquilinos")]
    public async Task<ActionResult<ApiResponse<List<Inquilino>>>> GetInquilinos()
    {
        var list = await _sheets.GetInquilinosAsync();
        return Ok(new ApiResponse<List<Inquilino>>(true, null, list));
    }

    [HttpGet("inquilinos/{id}")]
    public async Task<ActionResult<ApiResponse<Inquilino>>> GetInquilino(string id)
    {
        var item = await _sheets.GetInquilinoAsync(id);
        if (item == null) return NotFound(new ApiResponse<Inquilino>(false, "No encontrado", null));
        return Ok(new ApiResponse<Inquilino>(true, null, item));
    }

    [HttpPost("inquilinos")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<ActionResult<ApiResponse<Inquilino>>> CrearInquilino([FromBody] CrearInquilinoRequest req)
    {
        var item = await _sheets.CrearInquilinoAsync(req);
        return Ok(new ApiResponse<Inquilino>(true, "Inquilino creado", item));
    }

    [HttpPut("inquilinos/{id}")]
    [Authorize(Roles = "ADMINISTRADOR,ASISTENTE")]
    public async Task<IActionResult> ActualizarInquilino(string id, [FromBody] ActualizarInquilinoRequest req)
    {
        await _sheets.ActualizarInquilinoAsync(id, req);
        return Ok(new ApiResponse<object>(true, "Inquilino actualizado", null));
    }

    // ─── CONTRATOS ──────────────────────────────────────────────────────────
    [HttpGet("contratos")]
    public async Task<ActionResult<ApiResponse<List<Contrato>>>> GetContratos([FromQuery] bool soloVigentes = false)
    {
        var list = await _sheets.GetContratosAsync(soloVigentes);
        return Ok(new ApiResponse<List<Contrato>>(true, null, list));
    }

    [HttpGet("contratos/{id}")]
    public async Task<ActionResult<ApiResponse<Contrato>>> GetContrato(string id)
    {
        var item = await _sheets.GetContratoAsync(id);
        if (item == null) return NotFound(new ApiResponse<Contrato>(false, "No encontrado", null));
        return Ok(new ApiResponse<Contrato>(true, null, item));
    }

    [HttpPost("contratos")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<Contrato>>> CrearContrato([FromBody] CrearContratoRequest req)
    {
        var item = await _sheets.CrearContratoAsync(req);
        return Ok(new ApiResponse<Contrato>(true, "Contrato creado", item));
    }

    [HttpPut("contratos/{id}")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<IActionResult> ActualizarContrato(string id, [FromBody] ActualizarContratoRequest req)
    {
        await _sheets.ActualizarContratoAsync(id, req);
        return Ok(new ApiResponse<object>(true, "Contrato actualizado", null));
    }

    // ─── ESTADO CUENTA INQUILINO ─────────────────────────────────────────────
    [HttpGet("inquilinos/{id}/estado-cuenta")]
    public async Task<ActionResult<ApiResponse<EstadoCuentaInquilino>>> EstadoCuenta(string id)
    {
        var inquilino = await _sheets.GetInquilinoAsync(id);
        if (inquilino == null) return NotFound(new ApiResponse<EstadoCuentaInquilino>(false, "Inquilino no encontrado", null));

        var cargos = await _sheets.GetCxCCargosAsync(idInquilino: id);
        var vigentes = cargos.Where(c => c.Estado != "PAGADO").ToList();
        var pagos = await _sheets.GetPagosAsync(idInquilino: id);
        var pagosRecientes = pagos.OrderByDescending(p => p.FechaPago).Take(10).ToList();
        var totalPendiente = vigentes.Sum(c => c.SaldoPendiente);
        var iniciomes = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var totalMes = pagos.Where(p => p.FechaPago >= iniciomes).Sum(p => p.TotalPagado);

        return Ok(new ApiResponse<EstadoCuentaInquilino>(true, null,
            new EstadoCuentaInquilino(inquilino, vigentes, pagosRecientes, totalPendiente, totalMes)));
    }
}
