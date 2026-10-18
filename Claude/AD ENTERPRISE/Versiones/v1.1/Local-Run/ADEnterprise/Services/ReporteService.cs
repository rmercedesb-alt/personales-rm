using ADEnterprise.Models;
using ClosedXML.Excel;

namespace ADEnterprise.Services;

/// <summary>
/// Genera archivos Excel para los diferentes reportes del sistema.
/// </summary>
public class ReporteService
{
    private readonly FacturaService  _facturas;
    private readonly PagoService     _pagos;
    private readonly CompaniaService _companias;

    public ReporteService(FacturaService f, PagoService p, CompaniaService co)
    {
        _facturas  = f;
        _pagos     = p;
        _companias = co;
    }

    // ── FILTRADO COMÚN ────────────────────────────────────────────────────

    public async Task<(List<Factura> Facturas, List<Pago> Pagos)> CargarDatosAsync(
        DateTime desde, DateTime hasta,
        string empresa = "", string cliente = "", string estado = "")
    {
        var todasFact  = await _facturas.ObtenerTodasAsync();
        var todosPagos = await _pagos.ObtenerTodosAsync();

        var factFilt = todasFact
            .Where(f => f.FechaFactura.Date >= desde.Date && f.FechaFactura.Date <= hasta.Date)
            .Where(f => string.IsNullOrEmpty(empresa) ||
                        f.EmpresaNombre.Equals(empresa, StringComparison.OrdinalIgnoreCase))
            .Where(f => string.IsNullOrEmpty(cliente) ||
                        f.ClienteNombre.Equals(cliente, StringComparison.OrdinalIgnoreCase))
            .Where(f => string.IsNullOrEmpty(estado)  ||
                        f.Estado.ToString().Equals(estado, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var transIds = factFilt.Select(f => f.NumTransaccion).ToHashSet();

        var pagosFilt = todosPagos
            .Where(p => p.FechaPago.Date >= desde.Date && p.FechaPago.Date <= hasta.Date)
            .Where(p => string.IsNullOrEmpty(empresa) || transIds.Contains(p.NumTransaccionFactura))
            .ToList();

        return (factFilt, pagosFilt);
    }

    // ── EXCEL: FACTURAS GENERALES ─────────────────────────────────────────

    public byte[] ExcelFacturas(List<Factura> facturas, string titulo = "Reporte de Facturas")
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Facturas");

        // Encabezado del reporte
        ws.Cell(1, 1).Value = "AD ALL DIGITAL ENTERPRISE — " + titulo;
        ws.Range(1, 1, 1, 9).Merge().Style
            .Font.SetBold(true).Font.SetFontSize(13)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#1a3a5c"))
            .Font.SetFontColor(XLColor.White)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Cell(2, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}  |  Total registros: {facturas.Count}";
        ws.Range(2, 1, 2, 9).Merge().Style
            .Font.SetFontSize(10).Font.SetItalic(true)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        // Encabezados columnas
        string[] cols = { "N° Ticket", "Transacción", "Compañía", "Cliente",
                          "Fecha", "Monto (RD$)", "Pagado (RD$)", "Saldo (RD$)", "Estado" };
        for (int c = 0; c < cols.Length; c++)
        {
            ws.Cell(4, c + 1).Value = cols[c];
            ws.Cell(4, c + 1).Style
                .Font.SetBold(true)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#2d6db5"))
                .Font.SetFontColor(XLColor.White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        // Datos
        int row = 5;
        foreach (var f in facturas.OrderBy(x => x.FechaFactura))
        {
            ws.Cell(row, 1).Value  = f.NumTicket;
            ws.Cell(row, 2).Value  = f.NumTransaccion;
            ws.Cell(row, 3).Value  = f.EmpresaNombre;
            ws.Cell(row, 4).Value  = f.ClienteNombre;
            ws.Cell(row, 5).Value  = f.FechaFactura.ToString("dd/MM/yyyy");
            ws.Cell(row, 6).Value  = f.Monto;
            ws.Cell(row, 7).Value  = f.PagadoALaFecha;
            ws.Cell(row, 8).Value  = f.SaldoPendiente;
            ws.Cell(row, 9).Value  = f.Estado.ToString();

            // Formatear montos
            for (int c = 6; c <= 8; c++)
                ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";

            // Color por estado
            var bgColor = f.Estado switch
            {
                EstadoFactura.Pagada        => XLColor.FromHtml("#d4edda"),
                EstadoFactura.Vencida       => XLColor.FromHtml("#f8d7da"),
                EstadoFactura.PagadoParcial => XLColor.FromHtml("#fff3cd"),
                _                           => XLColor.FromHtml("#ffffff")
            };
            ws.Row(row).Style.Fill.SetBackgroundColor(bgColor);
            row++;
        }

        // Totales
        ws.Cell(row, 5).Value = "TOTALES:";
        ws.Cell(row, 5).Style.Font.SetBold(true).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        ws.Cell(row, 6).Value = facturas.Sum(f => f.Monto);
        ws.Cell(row, 7).Value = facturas.Sum(f => f.PagadoALaFecha);
        ws.Cell(row, 8).Value = facturas.Sum(f => f.SaldoPendiente);
        for (int c = 6; c <= 8; c++)
        {
            ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, c).Style.Font.SetBold(true);
            ws.Cell(row, c).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#1a3a5c"));
            ws.Cell(row, c).Style.Font.SetFontColor(XLColor.White);
        }

        ws.Columns().AdjustToContents();
        ws.Column(2).Width = 22;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── EXCEL: PAGOS ──────────────────────────────────────────────────────

    public byte[] ExcelPagos(List<Pago> pagos, string titulo = "Historial de Pagos")
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Pagos");

        ws.Cell(1, 1).Value = "AD ALL DIGITAL ENTERPRISE — " + titulo;
        ws.Range(1, 1, 1, 7).Merge().Style
            .Font.SetBold(true).Font.SetFontSize(13)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#1a3a5c"))
            .Font.SetFontColor(XLColor.White)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Cell(2, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}  |  Total pagos: {pagos.Count}";
        ws.Range(2, 1, 2, 7).Merge().Style.Font.SetFontSize(10).Font.SetItalic(true)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        string[] cols = { "N° Transacción Pago", "N° Ticket", "Fecha Pago",
                          "Forma de Pago", "Referencia", "Monto (RD$)", "Usuario" };
        for (int c = 0; c < cols.Length; c++)
        {
            ws.Cell(4, c + 1).Value = cols[c];
            ws.Cell(4, c + 1).Style.Font.SetBold(true)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#198754"))
                .Font.SetFontColor(XLColor.White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        int row = 5;
        foreach (var p in pagos.OrderByDescending(x => x.FechaPago))
        {
            ws.Cell(row, 1).Value = p.NumTransaccionPago;
            ws.Cell(row, 2).Value = p.NumTicket;
            ws.Cell(row, 3).Value = p.FechaPago.ToString("dd/MM/yyyy");
            ws.Cell(row, 4).Value = p.FormaPago.ToString();
            ws.Cell(row, 5).Value = p.NumChequeReferencia;
            ws.Cell(row, 6).Value = p.MontoPagado;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 7).Value = p.UsuarioRegistro;
            row++;
        }

        ws.Cell(row, 5).Value = "TOTAL:";
        ws.Cell(row, 5).Style.Font.SetBold(true).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        ws.Cell(row, 6).Value = pagos.Sum(p => p.MontoPagado);
        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(row, 6).Style.Font.SetBold(true)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#198754"))
            .Font.SetFontColor(XLColor.White);

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── EXCEL: PAGOS AGRUPADOS POR CLIENTE ───────────────────────────────

    public byte[] ExcelPagosPorCliente(
        List<Pago> pagos,
        List<Factura> facturas,
        string titulo = "Pagos por Cliente")
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Pagos por Cliente");

        ws.Cell(1, 1).Value = "AD ALL DIGITAL ENTERPRISE — " + titulo;
        ws.Range(1, 1, 1, 5).Merge().Style
            .Font.SetBold(true).Font.SetFontSize(13)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#1a3a5c"))
            .Font.SetFontColor(XLColor.White)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Cell(2, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Range(2, 1, 2, 5).Merge().Style.Font.SetFontSize(10).Font.SetItalic(true)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        string[] cols = { "Cliente", "N° Facturas", "Total Facturado (RD$)",
                          "Total Pagado (RD$)", "Saldo Pendiente (RD$)" };
        for (int c = 0; c < cols.Length; c++)
        {
            ws.Cell(4, c + 1).Value = cols[c];
            ws.Cell(4, c + 1).Style.Font.SetBold(true)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#2d6db5"))
                .Font.SetFontColor(XLColor.White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        // Mapa: transaccion → cliente
        var transToCliente = facturas.ToDictionary(f => f.NumTransaccion, f => f.ClienteNombre);

        var grupos = pagos
            .GroupBy(p => transToCliente.TryGetValue(p.NumTransaccionFactura, out var cli) ? cli : "Desconocido")
            .Select(g => new
            {
                Cliente      = g.Key,
                NumFacturas  = g.Select(p => p.NumTicket).Distinct().Count(),
                TotalFact    = facturas.Where(f => f.ClienteNombre == g.Key).Sum(f => f.Monto),
                TotalPagado  = g.Sum(p => p.MontoPagado),
                TotalPend    = facturas.Where(f => f.ClienteNombre == g.Key && f.Estado != EstadoFactura.Pagada).Sum(f => f.SaldoPendiente)
            })
            .OrderByDescending(g => g.TotalPagado)
            .ToList();

        int row = 5;
        foreach (var g in grupos)
        {
            ws.Cell(row, 1).Value = g.Cliente;
            ws.Cell(row, 2).Value = g.NumFacturas;
            ws.Cell(row, 3).Value = g.TotalFact;
            ws.Cell(row, 4).Value = g.TotalPagado;
            ws.Cell(row, 5).Value = g.TotalPend;
            for (int c = 3; c <= 5; c++)
                ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }

        // Totales
        ws.Cell(row, 1).Value = "TOTAL GENERAL";
        ws.Cell(row, 1).Style.Font.SetBold(true);
        ws.Cell(row, 3).Value = grupos.Sum(g => g.TotalFact);
        ws.Cell(row, 4).Value = grupos.Sum(g => g.TotalPagado);
        ws.Cell(row, 5).Value = grupos.Sum(g => g.TotalPend);
        for (int c = 3; c <= 5; c++)
        {
            ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, c).Style.Font.SetBold(true);
            ws.Cell(row, c).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#1a3a5c"));
            ws.Cell(row, c).Style.Font.SetFontColor(XLColor.White);
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
