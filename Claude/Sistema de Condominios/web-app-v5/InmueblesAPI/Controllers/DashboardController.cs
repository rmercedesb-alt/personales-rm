using InmueblesAPI.Models;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InmueblesAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _svc;
    public DashboardController(DashboardService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<DashboardKPIs>>> Get()
    {
        try
        {
            var kpis = await _svc.GetKPIsAsync();
            return Ok(new ApiResponse<DashboardKPIs>(true, null, kpis));
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException?.Message ?? "";
            var detail = $"{ex.GetType().Name}: {ex.Message}" + (inner.Length > 0 ? $" | Inner: {inner}" : "");
            return StatusCode(500, new { success = false, message = detail });
        }
    }

    [HttpGet("mora-detalle")]
    public async Task<ActionResult<ApiResponse<List<MoraDetalleItem>>>> MoraDetalle()
    {
        try
        {
            var data = await _svc.GetMoraDetalleAsync();
            return Ok(new ApiResponse<List<MoraDetalleItem>>(true, null, data));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpGet("art1762-detalle")]
    public async Task<ActionResult<ApiResponse<List<MoraDetalleItem>>>> Art1762Detalle()
    {
        try
        {
            var data = await _svc.GetArt1762DetalleAsync();
            return Ok(new ApiResponse<List<MoraDetalleItem>>(true, null, data));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}
