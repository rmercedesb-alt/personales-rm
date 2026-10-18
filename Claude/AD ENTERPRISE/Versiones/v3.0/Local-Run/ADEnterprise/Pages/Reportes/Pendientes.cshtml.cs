using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Reportes;

public class PendientesModel : PageModel
{
    private readonly ReporteService  _rp;
    private readonly FacturaService  _facturas;
    private readonly CompaniaService _companias;

    public PendientesModel(ReporteService rp, FacturaService f, CompaniaService co)
    { _rp = rp; _facturas = f; _companias = co; }

    [BindProperty(SupportsGet = true)] public string FiltroEmpresa { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string FiltroCliente { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string FiltroEstado  { get; set; } = ""; // Pendiente|Vencida|PagadoParcial

    public List<Factura> Facturas { get; set; } = new();
    public List<string>  Empresas { get; set; } = new();
    public List<string>  Clientes { get; set; } = new();

    public decimal TotalMonto     => Facturas.Sum(f => f.Monto);
    public decimal TotalPagado    => Facturas.Sum(f => f.PagadoALaFecha);
    public decimal TotalPendiente => Facturas.Sum(f => f.SaldoPendiente);
    public int     TotalVencidas  => Facturas.Count(f => f.Estado == EstadoFactura.Vencida);

    public async Task OnGetAsync()
    {
        SetViewData();
        var todas = await _facturas.ObtenerTodasAsync();

        Empresas = todas.Select(f => f.EmpresaNombre).Where(e => !string.IsNullOrEmpty(e)).Distinct().OrderBy(e => e).ToList();
        Clientes = todas.Select(f => f.ClienteNombre).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();

        Facturas = todas
            .Where(f => f.Estado != EstadoFactura.Pagada) // sólo no pagadas
            .Where(f => string.IsNullOrEmpty(FiltroEmpresa) || f.EmpresaNombre.Equals(FiltroEmpresa, StringComparison.OrdinalIgnoreCase))
            .Where(f => string.IsNullOrEmpty(FiltroCliente) || f.ClienteNombre.Equals(FiltroCliente, StringComparison.OrdinalIgnoreCase))
            .Where(f => string.IsNullOrEmpty(FiltroEstado)  || f.Estado.ToString().Equals(FiltroEstado, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.FechaFactura) // más vencida primero
            .ToList();
    }

    public async Task<IActionResult> OnPostExportarExcelAsync(string empresa = "", string cliente = "", string estado = "")
    {
        SetViewData();
        var todas = await _facturas.ObtenerTodasAsync();
        var filtradas = todas
            .Where(f => f.Estado != EstadoFactura.Pagada)
            .Where(f => string.IsNullOrEmpty(empresa) || f.EmpresaNombre.Equals(empresa, StringComparison.OrdinalIgnoreCase))
            .Where(f => string.IsNullOrEmpty(cliente) || f.ClienteNombre.Equals(cliente, StringComparison.OrdinalIgnoreCase))
            .Where(f => string.IsNullOrEmpty(estado)  || f.Estado.ToString().Equals(estado, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.FechaFactura).ToList();

        var bytes = _rp.ExcelFacturas(filtradas, "Facturas Pendientes");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"FacturasPendientes_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
