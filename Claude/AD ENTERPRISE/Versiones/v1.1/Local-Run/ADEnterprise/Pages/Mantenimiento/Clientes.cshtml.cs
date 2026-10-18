using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Mantenimiento;

public class ClientesModel : PageModel
{
    private readonly ClienteService _clientes;
    public ClientesModel(ClienteService clientes) => _clientes = clientes;

    public List<Cliente> Clientes { get; set; } = new();

    public async Task OnGetAsync()
    {
        SetViewData();
        // Backfill automático silencioso: si hay registros sin ADCTE###, se asignan en una
        // sola llamada batch (no genera error de cuota). Solo ocurre si realmente hay cambios.
        await _clientes.BackfillIdsAsync();
        Clientes = await _clientes.ObtenerTodosAsync();
    }

    public async Task<IActionResult> OnPostCrearAsync(
        string NombreCompleto, string Cedula, string Telefono, string Correo)
    {
        await _clientes.CrearAsync(new Cliente
        {
            NombreCompleto = NombreCompleto, Cedula = Cedula ?? "",
            Telefono = Telefono ?? "", Correo = Correo ?? "", Activo = true
        });
        TempData["Exito"] = $"Cliente '{NombreCompleto}' creado exitosamente.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditarAsync(
        string Id, string NombreCompleto, string Cedula, string Telefono, string Correo)
    {
        var todos = await _clientes.ObtenerTodosAsync();
        var c = todos.FirstOrDefault(x => x.Id == Id);
        if (c != null)
        {
            c.NombreCompleto = NombreCompleto; c.Cedula = Cedula ?? "";
            c.Telefono = Telefono ?? ""; c.Correo = Correo ?? "";
            await _clientes.ActualizarAsync(c);
            TempData["Exito"] = $"Cliente '{NombreCompleto}' actualizado.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(string id)
    {
        var todos = await _clientes.ObtenerTodosAsync();
        var c = todos.FirstOrDefault(x => x.Id == id);
        if (c != null)
        {
            c.Activo = !c.Activo;
            await _clientes.ActualizarAsync(c);
            TempData["Exito"] = $"Estado de '{c.NombreCompleto}' actualizado.";
        }
        return RedirectToPage();
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
