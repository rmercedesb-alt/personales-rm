using InmueblesAPI.Models;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace InmueblesAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login([FromBody] LoginRequest req)
    {
        var result = await _auth.LoginAsync(req.Email, req.Password);
        if (result == null)
            return Unauthorized(new ApiResponse<LoginResponse>(false, "Credenciales inválidas", null));
        return Ok(new ApiResponse<LoginResponse>(true, null, result));
    }
}
