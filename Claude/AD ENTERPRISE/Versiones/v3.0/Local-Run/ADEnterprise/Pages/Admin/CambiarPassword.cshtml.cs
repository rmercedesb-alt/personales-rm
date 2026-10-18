using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ADEnterprise.Pages.Admin;

public class CambiarPasswordModel : PageModel
{
    private readonly UsuarioService _usuarios;

    public CambiarPasswordModel(UsuarioService usuarios) => _usuarios = usuarios;

    [BindProperty] public string PasswordActual  { get; set; } = "";
    [BindProperty] public string PasswordNuevo   { get; set; } = "";
    [BindProperty] public string PasswordConfirm { get; set; } = "";

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(PasswordNuevo) || PasswordNuevo.Length < 6)
        {
            TempData["Error"] = "La nueva contraseña debe tener al menos 6 caracteres.";
            return Page();
        }

        if (PasswordNuevo != PasswordConfirm)
        {
            TempData["Error"] = "La nueva contraseña y la confirmación no coinciden.";
            return Page();
        }

        var nombreUsuario = User.Identity?.Name ?? "";
        var usuario = await _usuarios.BuscarPorUsuarioAsync(nombreUsuario);
        if (usuario == null)
        {
            TempData["Error"] = "Usuario no encontrado.";
            return Page();
        }

        if (!BCrypt.Net.BCrypt.Verify(PasswordActual, usuario.PasswordHash))
        {
            TempData["Error"] = "La contraseña actual es incorrecta.";
            return Page();
        }

        await _usuarios.CambiarPasswordAsync(usuario.Id, PasswordNuevo);
        TempData["Exito"] = "Contraseña actualizada correctamente.";
        return RedirectToPage("/Index");
    }
}
