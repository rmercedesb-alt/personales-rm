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
        var kpis = await _svc.GetKPIsAsync();
        return Ok(new ApiResponse<DashboardKPIs>(true, null, kpis));
    }
}
