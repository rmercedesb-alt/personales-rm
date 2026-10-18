using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Mantenimiento;

public class CompaniasModel : PageModel
{
    private readonly CompaniaService _companias;
    public CompaniasModel(CompaniaService companias) => _companias = companias;

    public List<Compania> Companias { get; set; } = new();

    public async Task OnGetAsync()
    {
        SetViewData();
        // Backfill automático silencioso: si hay registros sin ADCOM###, se asignan en una
        // sola llamada batch (no genera error de cuota). Solo ocurre si realmente hay cambios.
        await _companias.BackfillIdsAsync();
        Companias = await _companias.ObtenerTodasAsync();
    }

    public async Task<IActionResult> OnPostCrearAsync(
        string Nombre, string Rnc, string Telefono, string Direccion,
        string Correo, int CondicionPagoDias)
    {
        await _companias.CrearAsync(new Compania
        {
            Nombre = Nombre, Rnc = Rnc ?? "", Telefono = Telefono ?? "",
            Direccion = Direccion ?? "", Correo = Correo ?? "",
            CondicionPagoDias = CondicionPagoDias > 0 ? CondicionPagoDias : 30,
            Activo = true
        });
        TempData["Exito"] = $"Compañía '{Nombre}' creada exitosamente.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditarAsync(
        string Id, string Nombre, string Rnc, string Telefono,
        string Direccion, string Correo, int CondicionPagoDias)
    {
        var todas = await _companias.ObtenerTodasAsync();
        var c = todas.FirstOrDefault(x => x.Id == Id);
        if (c != null)
        {
            c.Nombre = Nombre; c.Rnc = Rnc ?? ""; c.Telefono = Telefono ?? "";
            c.Direccion = Direccion ?? ""; c.Correo = Correo ?? "";
            c.CondicionPagoDias = CondicionPagoDias > 0 ? CondicionPagoDias : 30;
            await _companias.ActualizarAsync(c);
            TempData["Exito"] = $"Compañía '{Nombre}' actualizada.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(string id)
    {
        var todas = await _companias.ObtenerTodasAsync();
        var c = todas.FirstOrDefault(x => x.Id == id);
        if (c != null)
        {
            c.Activo = !c.Activo;
            await _companias.ActualizarAsync(c);
            TempData["Exito"] = $"Estado de '{c.Nombre}' actualizado.";
        }
        return RedirectToPage();
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
