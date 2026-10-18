using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using InmueblesAPI.Models;
using Microsoft.IdentityModel.Tokens;

namespace InmueblesAPI.Services;

public class AuthService
{
    private readonly IConfiguration _config;
    private readonly GoogleSheetsService _sheets;

    public AuthService(IConfiguration config, GoogleSheetsService sheets)
    {
        _config = config;
        _sheets = sheets;
    }

    // Usuario maestro hardcoded — siempre tiene acceso aunque no aparezca en Sheets
    private static readonly UsuarioSistema _masterAdmin = new()
    {
        Email        = "r.mercedesb@gmail.com",
        Nombre       = "Rudy Mercedes",
        Rol          = "ADMINISTRADOR",
        Estado       = "SI",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("kina5468")
    };

    public async Task<LoginResponse?> LoginAsync(string email, string password)
    {
        UsuarioSistema? usuario;

        if (email.Equals(_masterAdmin.Email, StringComparison.OrdinalIgnoreCase))
        {
            // Siempre autentica con la clave hardcoded; ignora Sheets para este usuario
            if (!BCrypt.Net.BCrypt.Verify(password, _masterAdmin.PasswordHash)) return null;
            usuario = _masterAdmin;
        }
        else
        {
            usuario = await _sheets.GetUsuarioAsync(email);
            if (usuario == null || usuario.Estado != "SI") return null;

            if (string.IsNullOrEmpty(usuario.PasswordHash))
            {
                var hash = BCrypt.Net.BCrypt.HashPassword(password);
                await _sheets.SetPasswordHashAsync(email, hash);
            }
            else if (!BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
            {
                return null;
            }
        }

        var token = GenerateToken(usuario);
        var expiry = DateTime.UtcNow.AddHours(int.Parse(_config["Jwt:ExpiryHours"] ?? "8"));
        return new LoginResponse(token, usuario.Nombre, usuario.Rol, expiry);
    }

    private string GenerateToken(UsuarioSistema usuario)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, usuario.Email),
            new Claim(ClaimTypes.Name, usuario.Nombre),
            new Claim(ClaimTypes.Role, usuario.Rol),
            new Claim("rol", usuario.Rol)
        };
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(int.Parse(_config["Jwt:ExpiryHours"] ?? "8")),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
