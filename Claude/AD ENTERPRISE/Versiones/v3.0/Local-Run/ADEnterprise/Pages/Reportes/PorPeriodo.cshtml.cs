using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Reportes;

/// <summary>
/// "Por Período" redirige al Reporte General con filtros de fecha visibles.
/// </summary>
public class PorPeriodoModel : PageModel
{
    public IActionResult OnGet()
    {
        var hoy  = DateTime.Today;
        var inicio = new DateTime(hoy.Year, hoy.Month, 1);
        return RedirectToPage("/Reportes/General",
            new { FechaDesde = inicio.ToString("yyyy-MM-dd"), FechaHasta = hoy.ToString("yyyy-MM-dd") });
    }
}
