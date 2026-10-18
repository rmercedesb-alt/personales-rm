using InmueblesAPI.Models;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InmueblesAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsuariosController : ControllerBase
{
    private readonly GoogleSheetsService _sheets;

    public UsuariosController(GoogleSheetsService sheets) => _sheets = sheets;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var lista = await _sheets.GetUsuariosAsync();
        // Nunca exponer el hash al cliente
        var resultado = lista.Select(u => new
        {
            u.Email, u.Nombre, u.Rol, activo = u.Estado
        });
        return Ok(new ApiResponse<object>(true, null, resultado));
    }

    [HttpPost]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<IActionResult> Create([FromBody] CrearUsuarioRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) ||
            string.IsNullOrWhiteSpace(req.Nombre) ||
            string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new ApiResponse<object>(false, "Email, nombre y clave son requeridos.", null));

        var existentes = await _sheets.GetUsuariosAsync();
        if (existentes.Any(u => u.Email.Equals(req.Email, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new ApiResponse<object>(false, "Ya existe un usuario con ese email.", null));

        var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        await _sheets.CrearUsuarioAsync(req.Email, req.Nombre, req.Rol, req.Activo, hash);
        return Ok(new ApiResponse<object>(true, "Usuario creado.", null));
    }

    [HttpPut("{email}")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<IActionResult> Update(string email, [FromBody] ActualizarUsuarioRequest req)
    {
        var decoded = Uri.UnescapeDataString(email);
        string? newHash = null;
        if (!string.IsNullOrWhiteSpace(req.Password))
            newHash = BCrypt.Net.BCrypt.HashPassword(req.Password);

        await _sheets.ActualizarUsuarioAsync(decoded, req.Nombre, req.Rol, req.Activo, newHash);
        return Ok(new ApiResponse<object>(true, "Usuario actualizado.", null));
    }
}
