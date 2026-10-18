using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Reportes;

public class GeneralModel : PageModel
{
    private readonly ReporteService  _rp;
    private readonly FacturaService  _facturas;
    private readonly CompaniaService _companias;
    private readonly ClienteService  _clientes;

    public GeneralModel(ReporteService rp, FacturaService f, CompaniaService co, ClienteService cl)
    {
        _rp = rp; _facturas = f; _companias = co; _clientes = cl;
    }

    [BindProperty(SupportsGet = true)] public DateTime FechaDesde    { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime FechaHasta    { get; set; }
    [BindProperty(SupportsGet = true)] public string   FiltroEmpresa { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string   FiltroCliente { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string   FiltroEstado  { get; set; } = "";

    public List<Factura> Facturas  { get; set; } = new();
    public List<string>  Empresas  { get; set; } = new();
    public List<string>  Clientes  { get; set; } = new();

    public decimal TotalMonto    => Facturas.Sum(f => f.Monto);
    public decimal TotalPagado   => Facturas.Sum(f => f.PagadoALaFecha);
    public decimal TotalPendiente=> Facturas.Sum(f => f.SaldoPendiente);

    public async Task OnGetAsync()
    {
        SetViewData();
        if (FechaDesde == default) FechaDesde = new DateTime(DateTime.Today.Year, 1, 1);
        if (FechaHasta == default) FechaHasta = DateTime.Today;

        var (facturas, _) = await _rp.CargarDatosAsync(FechaDesde, FechaHasta, FiltroEmpresa, FiltroCliente, FiltroEstado);
        Facturas = facturas.OrderByDescending(f => f.FechaFactura).ToList();

        var todas = await _facturas.ObtenerTodasAsync();
        Empresas = todas.Select(f => f.EmpresaNombre).Where(e => !string.IsNullOrEmpty(e)).Distinct().OrderBy(e => e).ToList();
        Clientes = todas.Select(f => f.ClienteNombre).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
    }

    public async Task<IActionResult> OnPostExportarExcelAsync(
        DateTime desde, DateTime hasta, string empresa = "", string cliente = "", string estado = "")
    {
        SetViewData();
        var (facturas, _) = await _rp.CargarDatosAsync(desde, hasta, empresa, cliente, estado);
        var bytes = _rp.ExcelFacturas(facturas, $"Reporte General {desde:dd/MM/yyyy} - {hasta:dd/MM/yyyy}");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ReporteGeneral_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
