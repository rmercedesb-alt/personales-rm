using ADEnterprise.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace ADEnterprise.Pages;

public class LoginModel : PageModel
{
    private readonly UsuarioService _usuarios;
    private readonly SetupService   _setup;
    private readonly IConfiguration _config;

    public LoginModel(UsuarioService usuarios, SetupService setup, IConfiguration config)
    {
        _usuarios = usuarios;
        _setup    = setup;
        _config   = config;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "El usuario es requerido")]
        public string Usuario { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es requerida")]
        public string Password { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // Si el sistema no está configurado → ir al Setup
        if (!_setup.EstaConfigurado())
            return RedirectToPage("/Setup");

        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Index");

        // Crear admin inicial si no existe ningún usuario
        await _usuarios.SeedAdminInicialAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // ── 1. Verificar MASTER ADMIN (desde appsettings, NO desde Google Sheets)
        //       Este usuario no puede ser eliminado desde la interfaz del sistema.
        var masterUser = _config["MasterAdmin:Usuario"] ?? "";
        var masterPass = _config["MasterAdmin:Password"] ?? "";

        if (!string.IsNullOrEmpty(masterUser) && !string.IsNullOrEmpty(masterPass) &&
            Input.Usuario.Equals(masterUser, StringComparison.OrdinalIgnoreCase) &&
            Input.Password == masterPass)
        {
            var masterClaims = new List<Claim>
            {
                new(ClaimTypes.Name,      masterUser),
                new(ClaimTypes.GivenName, "Master Administrador"),
                new(ClaimTypes.Role,      "Administrador"),
                new("UserId",             "MASTER"),
                new("NombreCompleto",     "Master Administrador"),
                new("EsMaster",           "true")
            };
            var masterIdentity  = new ClaimsIdentity(masterClaims, CookieAuthenticationDefaults.AuthenticationScheme);
            var masterPrincipal = new ClaimsPrincipal(masterIdentity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, masterPrincipal);
            return RedirectToPage("/Index");
        }

        // ── 2. Verificar usuario normal (desde Google Sheets)
        var usuario = await _usuarios.BuscarPorUsuarioAsync(Input.Usuario);
        if (usuario == null || !BCrypt.Net.BCrypt.Verify(Input.Password, usuario.PasswordHash))
        {
            ErrorMessage = "Usuario o contraseña incorrectos.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name,      usuario.NombreUsuario),
            new(ClaimTypes.GivenName, usuario.NombreCompleto),
            new(ClaimTypes.Role,      usuario.Perfil.ToString()),
            new("UserId",             usuario.Id),
            new("NombreCompleto",     usuario.NombreCompleto)
        };

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return RedirectToPage("/Index");
    }
}
