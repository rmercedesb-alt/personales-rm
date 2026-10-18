using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Reportes;

public class PorClienteModel : PageModel
{
    private readonly ReporteService _rp;
    private readonly FacturaService _facturas;

    public PorClienteModel(ReporteService rp, FacturaService f) { _rp = rp; _facturas = f; }

    [BindProperty(SupportsGet = true)] public DateTime FechaDesde    { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime FechaHasta    { get; set; }
    [BindProperty(SupportsGet = true)] public string   FiltroEmpresa { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string   FiltroEstado  { get; set; } = "";

    public List<string>      Empresas { get; set; } = new();
    public List<GrupoCliente> Grupos  { get; set; } = new();

    public class GrupoCliente
    {
        public string        Cliente   { get; set; } = "";
        public List<Factura> Facturas  { get; set; } = new();
        public decimal TotalMonto    => Facturas.Sum(f => f.Monto);
        public decimal TotalPagado   => Facturas.Sum(f => f.PagadoALaFecha);
        public decimal TotalPendiente=> Facturas.Sum(f => f.SaldoPendiente);
        public decimal PorcentajoPago => TotalMonto > 0 ? Math.Round(TotalPagado / TotalMonto * 100, 1) : 0;
    }

    public async Task OnGetAsync()
    {
        SetViewData();
        if (FechaDesde == default) FechaDesde = new DateTime(DateTime.Today.Year, 1, 1);
        if (FechaHasta == default) FechaHasta = DateTime.Today;

        var (facturas, _) = await _rp.CargarDatosAsync(FechaDesde, FechaHasta, FiltroEmpresa, "", FiltroEstado);
        Grupos = facturas
            .GroupBy(f => f.ClienteNombre)
            .Select(g => new GrupoCliente { Cliente = g.Key, Facturas = g.OrderByDescending(f => f.FechaFactura).ToList() })
            .OrderByDescending(g => g.TotalPendiente).ToList();

        var todas = await _facturas.ObtenerTodasAsync();
        Empresas = todas.Select(f => f.EmpresaNombre).Where(e => !string.IsNullOrEmpty(e)).Distinct().OrderBy(e => e).ToList();
    }

    public async Task<IActionResult> OnPostExportarExcelAsync(DateTime desde, DateTime hasta, string empresa = "", string estado = "")
    {
        SetViewData();
        var (facturas, pagos) = await _rp.CargarDatosAsync(desde, hasta, empresa, "", estado);
        var bytes = _rp.ExcelPagosPorCliente(pagos, facturas, $"Por Cliente {desde:dd/MM/yyyy} - {hasta:dd/MM/yyyy}");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ReportePorCliente_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
