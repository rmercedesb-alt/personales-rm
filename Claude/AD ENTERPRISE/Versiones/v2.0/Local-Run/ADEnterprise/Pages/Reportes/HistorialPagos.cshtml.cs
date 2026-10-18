using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Reportes;

public class HistorialPagosModel : PageModel
{
    private readonly ReporteService  _rp;
    private readonly FacturaService  _facturas;
    private readonly CompaniaService _companias;

    public HistorialPagosModel(ReporteService rp, FacturaService f, CompaniaService co)
    { _rp = rp; _facturas = f; _companias = co; }

    [BindProperty(SupportsGet = true)] public DateTime FechaDesde    { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime FechaHasta    { get; set; }
    [BindProperty(SupportsGet = true)] public string   FiltroEmpresa { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string   FiltroCliente { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string   FiltroRecibo  { get; set; } = "";

    public List<Pago>    TodosPagos  { get; set; } = new();
    public List<string>  Empresas    { get; set; } = new();
    public List<string>  Clientes    { get; set; } = new();

    // Agrupación por cliente (punto 4)
    public List<ResumenCliente> ResumenPorCliente { get; set; } = new();

    public decimal TotalCobrado => TodosPagos.Sum(p => p.MontoPagado);

    public class ResumenCliente
    {
        public string      Cliente       { get; set; } = "";
        public List<Pago>  Pagos         { get; set; } = new();
        public decimal     TotalPagado   => Pagos.Sum(p => p.MontoPagado);
        public int         NumFacturas   => Pagos.Select(p => p.NumTicket).Distinct().Count();
    }

    public async Task OnGetAsync()
    {
        SetViewData();
        if (FechaDesde == default) FechaDesde = new DateTime(DateTime.Today.Year, 1, 1);
        if (FechaHasta == default) FechaHasta = DateTime.Today;

        var todasFact        = await _facturas.ObtenerTodasAsync();
        var (_, todosPagosAll) = await _rp.CargarDatosAsync(FechaDesde, FechaHasta);

        Empresas = todasFact.Select(f => f.EmpresaNombre).Where(e => !string.IsNullOrEmpty(e)).Distinct().OrderBy(e => e).ToList();
        Clientes = todasFact.Select(f => f.ClienteNombre).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();

        // Mapa transacción → datos factura
        var mapaFact = todasFact.ToDictionary(f => f.NumTransaccion, f => f);

        var pagosFiltrados = todosPagosAll
            .Where(p => p.FechaPago.Date >= FechaDesde.Date && p.FechaPago.Date <= FechaHasta.Date)
            .ToList();

        // Filtro adicional por empresa (a través de la factura)
        if (!string.IsNullOrEmpty(FiltroEmpresa))
        {
            pagosFiltrados = pagosFiltrados.Where(p =>
                mapaFact.TryGetValue(p.NumTransaccionFactura, out var f) &&
                f.EmpresaNombre.Equals(FiltroEmpresa, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (!string.IsNullOrEmpty(FiltroCliente))
        {
            pagosFiltrados = pagosFiltrados.Where(p =>
                mapaFact.TryGetValue(p.NumTransaccionFactura, out var f) &&
                f.ClienteNombre.Equals(FiltroCliente, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Filtro por N° Recibo
        if (!string.IsNullOrWhiteSpace(FiltroRecibo))
        {
            var rec = FiltroRecibo.Trim();
            pagosFiltrados = pagosFiltrados.Where(p =>
                p.NumRecibo.Contains(rec, StringComparison.OrdinalIgnoreCase) ||
                p.NumTransaccionPago.Contains(rec, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        TodosPagos = pagosFiltrados.OrderByDescending(p => p.FechaPago).ToList();

        // Agrupar por cliente (usando la factura para obtener nombre)
        ResumenPorCliente = pagosFiltrados
            .GroupBy(p => mapaFact.TryGetValue(p.NumTransaccionFactura, out var f) ? f.ClienteNombre : "Desconocido")
            .Select(g => new ResumenCliente { Cliente = g.Key, Pagos = g.OrderByDescending(p => p.FechaPago).ToList() })
            .OrderByDescending(g => g.TotalPagado)
            .ToList();
    }

    public async Task<IActionResult> OnPostExportarExcelAsync(
        DateTime desde, DateTime hasta, string empresa = "", string cliente = "")
    {
        SetViewData();
        var todasFact  = await _facturas.ObtenerTodasAsync();
        var (_, pagos) = await _rp.CargarDatosAsync(desde, hasta, empresa, cliente);

        var mapaFact = todasFact.ToDictionary(f => f.NumTransaccion, f => f);
        var filtrados = pagos.Where(p => p.FechaPago.Date >= desde.Date && p.FechaPago.Date <= hasta.Date);
        if (!string.IsNullOrEmpty(empresa))
            filtrados = filtrados.Where(p => mapaFact.TryGetValue(p.NumTransaccionFactura, out var f) &&
                f.EmpresaNombre.Equals(empresa, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(cliente))
            filtrados = filtrados.Where(p => mapaFact.TryGetValue(p.NumTransaccionFactura, out var f) &&
                f.ClienteNombre.Equals(cliente, StringComparison.OrdinalIgnoreCase));

        var bytes = _rp.ExcelPagosPorCliente(filtrados.ToList(), todasFact,
            $"Historial de Pagos {desde:dd/MM/yyyy} - {hasta:dd/MM/yyyy}");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"HistorialPagos_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
