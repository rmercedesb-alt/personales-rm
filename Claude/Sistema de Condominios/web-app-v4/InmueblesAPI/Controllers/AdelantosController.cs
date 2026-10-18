using System.Security.Claims;
using InmueblesAPI.Models;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InmueblesAPI.Controllers;

[ApiController]
[Route("api/adelantos")]
[Authorize]
public class AdelantosController : ControllerBase
{
    private readonly InmueblesService _svc;
    public AdelantosController(InmueblesService sheets) => _svc = sheets;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<Adelanto>>>> Get([FromQuery] string? idPropietario)
    {
        var list = await _svc.GetAdelantosAsync(idPropietario);
        return Ok(new ApiResponse<List<Adelanto>>(true, null, list));
    }

    [HttpPost]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<Adelanto>>> Crear([FromBody] CrearAdelantoRequest req)
    {
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        var adelanto = await _svc.CrearAdelantoAsync(req);
        return Ok(new ApiResponse<Adelanto>(true, "Adelanto registrado", adelanto));
    }
}
