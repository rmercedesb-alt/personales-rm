using ADEnterprise.Models;
using ADEnterprise.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Reportes;

public class PorCompaniaModel : PageModel
{
    private readonly ReporteService  _rp;
    private readonly FacturaService  _facturas;

    public PorCompaniaModel(ReporteService rp, FacturaService f)
    { _rp = rp; _facturas = f; }

    [BindProperty(SupportsGet = true)] public DateTime FechaDesde { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime FechaHasta { get; set; }
    [BindProperty(SupportsGet = true)] public string   FiltroEstado { get; set; } = "";

    public List<GrupoCompania> Grupos { get; set; } = new();

    public class GrupoCompania
    {
        public string        Empresa   { get; set; } = "";
        public List<Factura> Facturas  { get; set; } = new();
        public decimal TotalMonto    => Facturas.Sum(f => f.Monto);
        public decimal TotalPagado   => Facturas.Sum(f => f.PagadoALaFecha);
        public decimal TotalPendiente=> Facturas.Sum(f => f.SaldoPendiente);
        public int     Pendientes    => Facturas.Count(f => f.Estado == EstadoFactura.Pendiente || f.Estado == EstadoFactura.Vencida);
    }

    public async Task OnGetAsync()
    {
        SetViewData();
        if (FechaDesde == default) FechaDesde = new DateTime(DateTime.Today.Year, 1, 1);
        if (FechaHasta == default) FechaHasta = DateTime.Today;

        var (facturas, _) = await _rp.CargarDatosAsync(FechaDesde, FechaHasta, "", "", FiltroEstado);
        Grupos = facturas
            .GroupBy(f => f.EmpresaNombre)
            .Select(g => new GrupoCompania { Empresa = g.Key, Facturas = g.OrderByDescending(f => f.FechaFactura).ToList() })
            .OrderBy(g => g.Empresa).ToList();
    }

    public async Task<IActionResult> OnPostExportarExcelAsync(DateTime desde, DateTime hasta, string estado = "")
    {
        SetViewData();
        var (facturas, _) = await _rp.CargarDatosAsync(desde, hasta, "", "", estado);

        using var wb = new XLWorkbook();
        var grupos = facturas.GroupBy(f => f.EmpresaNombre).OrderBy(g => g.Key);

        foreach (var grupo in grupos)
        {
            var wsName = grupo.Key.Length > 30 ? grupo.Key[..30] : grupo.Key;
            var ws = wb.Worksheets.Add(wsName);

            ws.Cell(1, 1).Value = "Compañía: " + grupo.Key;
            ws.Range(1, 1, 1, 7).Merge().Style.Font.SetBold(true).Font.SetFontSize(12)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#1a3a5c")).Font.SetFontColor(XLColor.White);

            string[] cols = { "Ticket", "Cliente", "Fecha", "Monto", "Pagado", "Saldo", "Estado" };
            for (int c = 0; c < cols.Length; c++)
            {
                ws.Cell(3, c+1).Value = cols[c];
                ws.Cell(3, c+1).Style.Font.SetBold(true)
                    .Fill.SetBackgroundColor(XLColor.FromHtml("#2d6db5")).Font.SetFontColor(XLColor.White);
            }

            int row = 4;
            foreach (var f in grupo.OrderByDescending(f => f.FechaFactura))
            {
                ws.Cell(row,1).Value = f.NumTicket;
                ws.Cell(row,2).Value = f.ClienteNombre;
                ws.Cell(row,3).Value = f.FechaFactura.ToString("dd/MM/yyyy");
                ws.Cell(row,4).Value = f.Monto;
                ws.Cell(row,5).Value = f.PagadoALaFecha;
                ws.Cell(row,6).Value = f.SaldoPendiente;
                ws.Cell(row,7).Value = f.Estado.ToString();
                for (int c = 4; c <= 6; c++) ws.Cell(row,c).Style.NumberFormat.Format = "#,##0.00";
                row++;
            }
            ws.Cell(row,3).Value = "TOTAL";
            ws.Cell(row,4).Value = grupo.Sum(f => f.Monto);
            ws.Cell(row,5).Value = grupo.Sum(f => f.PagadoALaFecha);
            ws.Cell(row,6).Value = grupo.Sum(f => f.SaldoPendiente);
            for (int c = 3; c <= 6; c++) ws.Cell(row,c).Style.Font.SetBold(true).NumberFormat.Format = "#,##0.00";
            ws.Columns().AdjustToContents();
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ReportePorCompania_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
