using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Mantenimiento;

/// <summary>
/// Permite al Administrador detener el servidor local de forma controlada.
/// Solo disponible cuando la app corre localmente (IsDevelopment o siempre en v1).
/// </summary>
public class ApagarModel : PageModel
{
    private readonly IHostApplicationLifetime _appLifetime;

    public ApagarModel(IHostApplicationLifetime appLifetime)
    {
        _appLifetime = appLifetime;
    }

    public IActionResult OnGet()
    {
        SetViewData();
        // Solo administradores pueden acceder
        var perfil = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (perfil != "Administrador")
            return RedirectToPage("/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        SetViewData();
        var perfil = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (perfil != "Administrador")
            return RedirectToPage("/Index");

        // Dar tiempo para que la respuesta se envíe antes de apagar
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500); // 1.5 segundos
            _appLifetime.StopApplication();
        });

        return Page();
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
