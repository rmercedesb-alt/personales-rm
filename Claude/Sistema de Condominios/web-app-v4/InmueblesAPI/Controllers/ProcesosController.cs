using InmueblesAPI.Models;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InmueblesAPI.Controllers;

[ApiController]
[Route("api/procesos")]
[Authorize]
public class ProcesosController : ControllerBase
{
    private readonly InmueblesService _svc;
    public ProcesosController(InmueblesService sheets) => _svc = sheets;

    [HttpGet("estado")]
    public async Task<ActionResult<ApiResponse<ProcesosEstado>>> GetEstado()
    {
        var estado = await _svc.GetProcesosEstadoAsync();
        return Ok(new ApiResponse<ProcesosEstado>(true, null, estado));
    }

    [HttpPost("recalcular-mora")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<int>>> RecalcularMora()
    {
        var cantidad = await _svc.RecalcularMorasMasivoAsync();
        return Ok(new ApiResponse<int>(true, $"{cantidad} CxC actualizada(s) con su mora recalculada", cantidad));
    }

    [HttpPost("generar-cargos")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<int>>> GenerarCargos()
    {
        var cantidad = await _svc.GenerarCargosRecurrentesAsync();
        return Ok(new ApiResponse<int>(true, $"{cantidad} cargo(s) de alquiler generado(s)", cantidad));
    }
}
