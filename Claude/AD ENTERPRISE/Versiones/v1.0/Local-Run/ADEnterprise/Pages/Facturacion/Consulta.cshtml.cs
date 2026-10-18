using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Facturacion;

public class ConsultaModel : PageModel
{
    private readonly FacturaService _facturas;
    public ConsultaModel(FacturaService facturas) => _facturas = facturas;

    public List<Factura> Facturas { get; set; } = new();
    public List<string>  Empresas { get; set; } = new();
    public List<string>  Clientes { get; set; } = new();

    public async Task OnGetAsync()
    {
        SetViewData();
        try
        {
            Facturas = await _facturas.ObtenerTodasAsync();
            Empresas = Facturas
                .Select(f => f.EmpresaNombre)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct().OrderBy(e => e).ToList();
            Clientes = Facturas
                .Select(f => f.ClienteNombre)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct().OrderBy(c => c).ToList();
        }
        catch
        {
            Facturas = new(); Empresas = new(); Clientes = new();
        }
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
