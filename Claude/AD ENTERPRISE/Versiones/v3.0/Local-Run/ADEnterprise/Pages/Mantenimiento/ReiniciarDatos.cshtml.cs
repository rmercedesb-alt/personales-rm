using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Mantenimiento;

public class ReiniciarDatosModel : PageModel
{
    private readonly ReiniciarDatosService _svc;

    public ReiniciarDatosModel(ReiniciarDatosService svc) => _svc = svc;

    [BindProperty] public string Confirmacion { get; set; } = "";

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (Confirmacion?.Trim().ToUpper() != "REINICIAR")
        {
            TempData["Error"] = "Debe escribir REINICIAR (en mayúsculas) para confirmar la operación.";
            return Page();
        }

        var (ok, msg) = await _svc.EjecutarAsync();
        if (ok)
            TempData["Exito"] = msg;
        else
            TempData["Error"] = msg;

        return RedirectToPage();
    }
}
