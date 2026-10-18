using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Finanzas;

public class CajaModel : PageModel
{
    private readonly CajaService _caja;

    public CajaModel(CajaService caja) => _caja = caja;

    // ── Vista ────────────────────────────────────────────────────────────────
    public CuadreCaja?       CajaAbierta     { get; set; }
    public List<CuadreCaja>  HistorialCuadres { get; set; } = new();
    public decimal           BalanceActual   { get; set; }
    public bool              CajaDeOtrosDias { get; set; }  // abierta pero de día anterior

    [BindProperty] public decimal BalanceInicial { get; set; }

    private string Usuario => User.FindFirst("NombreCompleto")?.Value
                           ?? User.Identity?.Name ?? "Sistema";

    public async Task OnGetAsync()
    {
        SetViewData();
        await CargarAsync();
    }

    // ── Abrir caja ────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostAbrirAsync()
    {
        SetViewData();
        try
        {
            // Verificar que no haya caja de otro día sin cerrar
            var cajaExistente = await _caja.GetCajaAbiertaAsync();
            if (cajaExistente != null && cajaExistente.FechaApertura.Date != DateTime.Today)
            {
                TempData["Error"] = $"Debe cerrar la caja del {cajaExistente.FechaApertura:dd/MM/yyyy} antes de abrir la de hoy.";
                return RedirectToPage();
            }

            var seq = await _caja.AbrirCajaAsync(BalanceInicial, Usuario);
            TempData["Exito"] = $"Caja {seq} abierta exitosamente con balance inicial RD$ {BalanceInicial:N2}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al abrir caja: {ex.Message}";
        }
        return RedirectToPage();
    }

    // ── Cerrar caja ───────────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostCerrarAsync(string seqCuadre)
    {
        SetViewData();
        try
        {
            await _caja.CerrarCajaAsync(seqCuadre, Usuario);
            TempData["Exito"] = $"Caja {seqCuadre} cerrada y cuadrada exitosamente.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al cerrar caja: {ex.Message}";
        }
        return RedirectToPage();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private async Task CargarAsync()
    {
        CajaAbierta      = await _caja.GetCajaAbiertaAsync();
        HistorialCuadres = await _caja.GetHistorialCuadresAsync();
        BalanceActual    = await _caja.GetBalanceActualAsync();

        if (CajaAbierta != null)
            CajaDeOtrosDias = CajaAbierta.FechaApertura.Date != DateTime.Today;

        // Balance inicial sugerido = balance actual
        BalanceInicial = BalanceActual;
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
