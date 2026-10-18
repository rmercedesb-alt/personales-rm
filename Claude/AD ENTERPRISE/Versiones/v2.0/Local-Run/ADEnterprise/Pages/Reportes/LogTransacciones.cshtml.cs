using ADEnterprise.Models;
using ADEnterprise.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Reportes;

public class LogTransaccionesModel : PageModel
{
    private readonly FacturaService _facturas;
    private readonly PagoService    _pagos;

    public LogTransaccionesModel(FacturaService f, PagoService p) { _facturas = f; _pagos = p; }

    [BindProperty(SupportsGet = true)] public DateTime FechaDesde    { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime FechaHasta    { get; set; }
    [BindProperty(SupportsGet = true)] public string   FiltroTipo    { get; set; } = ""; // Factura | Pago
    [BindProperty(SupportsGet = true)] public string   FiltroUsuario { get; set; } = "";

    public List<EntradaLog> Entradas  { get; set; } = new();
    public List<string>     Usuarios  { get; set; } = new();

    public class EntradaLog
    {
        public DateTime Fecha     { get; set; }
        public string   Tipo      { get; set; } = ""; // "Factura" | "Pago"
        public string   NumRef    { get; set; } = ""; // NumTransaccion o NumTransaccionPago
        public int      NumTicket { get; set; }
        public string   Empresa   { get; set; } = "";
        public string   Cliente   { get; set; } = "";
        public decimal  Monto     { get; set; }
        public string   Detalle   { get; set; } = ""; // estado / forma de pago
        public string   Usuario   { get; set; } = "";
    }

    public async Task OnGetAsync()
    {
        SetViewData();
        if (FechaDesde == default) FechaDesde = DateTime.Today.AddDays(-30);
        if (FechaHasta == default) FechaHasta = DateTime.Today;

        var todasFact  = await _facturas.ObtenerTodasAsync();
        var todosPagos = await _pagos.ObtenerTodosAsync();

        var mapaFact = todasFact.ToDictionary(f => f.NumTransaccion, f => f);

        // Para facturas: usar FechaMigracion como "fecha de ingreso al sistema" cuando esté disponible.
        // FechaMigracion se asigna para datos importados (su fecha de importación) y para facturas
        // nuevas creadas en el sistema (fecha/hora exacta de creación).
        // Si no tiene FechaMigracion (datos históricos sin este campo), se usa FechaFactura.
        var logFacturas = todasFact
            .Select(f => new { Factura = f, FechaLog = f.FechaMigracion ?? f.FechaFactura })
            .Where(x => x.FechaLog.Date >= FechaDesde.Date && x.FechaLog.Date <= FechaHasta.Date)
            .Select(x => new EntradaLog
            {
                Fecha     = x.FechaLog,
                Tipo      = "Factura",
                NumRef    = x.Factura.NumTransaccion,
                NumTicket = x.Factura.NumTicket,
                Empresa   = x.Factura.EmpresaNombre,
                Cliente   = x.Factura.ClienteNombre,
                Monto     = x.Factura.Monto,
                Detalle   = $"{x.Factura.Estado} · Fact: {x.Factura.FechaFactura:dd/MM/yyyy}",
                Usuario   = !string.IsNullOrEmpty(x.Factura.UsuarioMigracion)
                                ? x.Factura.UsuarioMigracion : "Sistema"
            });

        var logPagos = todosPagos
            .Where(p => p.FechaPago.Date >= FechaDesde.Date && p.FechaPago.Date <= FechaHasta.Date)
            .Select(p =>
            {
                mapaFact.TryGetValue(p.NumTransaccionFactura, out var f);
                return new EntradaLog
                {
                    Fecha     = p.FechaPago,
                    Tipo      = "Pago",
                    NumRef    = p.NumTransaccionPago,
                    NumTicket = p.NumTicket,
                    Empresa   = f?.EmpresaNombre ?? "",
                    Cliente   = f?.ClienteNombre ?? "",
                    Monto     = p.MontoPagado,
                    Detalle   = p.FormaPago.ToString() + (string.IsNullOrEmpty(p.NumChequeReferencia) ? "" : $" #{p.NumChequeReferencia}"),
                    Usuario   = p.UsuarioRegistro
                };
            });

        // Modificaciones de facturas
        var logModFacturas = todasFact
            .Where(f => f.FechaModificacion.HasValue &&
                        f.FechaModificacion.Value.Date >= FechaDesde.Date &&
                        f.FechaModificacion.Value.Date <= FechaHasta.Date)
            .Select(f => new EntradaLog
            {
                Fecha     = f.FechaModificacion!.Value,
                Tipo      = "Mod. Factura",
                NumRef    = f.NumTransaccion,
                NumTicket = f.NumTicket,
                Empresa   = f.EmpresaNombre,
                Cliente   = f.ClienteNombre,
                Monto     = f.Monto,
                Detalle   = $"Estado: {f.Estado} · Pagado: {f.PagadoALaFecha:N2}",
                Usuario   = f.UsuarioModificacion
            });

        // Modificaciones de pagos
        var logModPagos = todosPagos
            .Where(p => p.FechaModificacion.HasValue &&
                        p.FechaModificacion.Value.Date >= FechaDesde.Date &&
                        p.FechaModificacion.Value.Date <= FechaHasta.Date)
            .Select(p =>
            {
                mapaFact.TryGetValue(p.NumTransaccionFactura, out var f);
                return new EntradaLog
                {
                    Fecha     = p.FechaModificacion!.Value,
                    Tipo      = "Mod. Pago",
                    NumRef    = p.NumTransaccionPago,
                    NumTicket = p.NumTicket,
                    Empresa   = f?.EmpresaNombre ?? "",
                    Cliente   = f?.ClienteNombre ?? "",
                    Monto     = p.MontoPagado,
                    Detalle   = $"{p.FormaPago}" + (p.NumChequeReferencia != "" ? $" #{p.NumChequeReferencia}" : ""),
                    Usuario   = p.UsuarioModificacion
                };
            });

        var todas = logFacturas.Concat(logPagos).Concat(logModFacturas).Concat(logModPagos);

        if (!string.IsNullOrEmpty(FiltroTipo))
            todas = todas.Where(e => e.Tipo.Equals(FiltroTipo, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(FiltroUsuario))
            todas = todas.Where(e => e.Usuario.Equals(FiltroUsuario, StringComparison.OrdinalIgnoreCase));

        Entradas = todas.OrderByDescending(e => e.Fecha).ToList();

        Usuarios = Entradas.Select(e => e.Usuario).Distinct().OrderBy(u => u).ToList();
    }

    public async Task<IActionResult> OnPostExportarExcelAsync(
        DateTime desde, DateTime hasta, string tipo = "", string usuario = "")
    {
        // Rebuilding the same data for export
        var model = new LogTransaccionesModel(_facturas, _pagos)
        {
            FechaDesde = desde, FechaHasta = hasta, FiltroTipo = tipo, FiltroUsuario = usuario
        };
        // Re-use OnGetAsync logic inline
        SetViewData();

        var todasFact  = await _facturas.ObtenerTodasAsync();
        var todosPagos = await _pagos.ObtenerTodosAsync();
        var mapaFact   = todasFact.ToDictionary(f => f.NumTransaccion, f => f);

        var logFacturas = todasFact
            .Select(f => new { Factura = f, FechaLog = f.FechaMigracion ?? f.FechaFactura })
            .Where(x => x.FechaLog.Date >= desde.Date && x.FechaLog.Date <= hasta.Date)
            .Select(x => new EntradaLog
            {
                Fecha = x.FechaLog, Tipo = "Factura", NumRef = x.Factura.NumTransaccion,
                NumTicket = x.Factura.NumTicket, Empresa = x.Factura.EmpresaNombre,
                Cliente = x.Factura.ClienteNombre, Monto = x.Factura.Monto,
                Detalle = $"{x.Factura.Estado} · Fact: {x.Factura.FechaFactura:dd/MM/yyyy}",
                Usuario = !string.IsNullOrEmpty(x.Factura.UsuarioMigracion)
                              ? x.Factura.UsuarioMigracion : "Sistema"
            });

        var logPagos = todosPagos
            .Where(p => p.FechaPago.Date >= desde.Date && p.FechaPago.Date <= hasta.Date)
            .Select(p =>
            {
                mapaFact.TryGetValue(p.NumTransaccionFactura, out var f);
                return new EntradaLog
                {
                    Fecha = p.FechaPago, Tipo = "Pago", NumRef = p.NumTransaccionPago,
                    NumTicket = p.NumTicket, Empresa = f?.EmpresaNombre ?? "", Cliente = f?.ClienteNombre ?? "",
                    Monto = p.MontoPagado, Detalle = p.FormaPago.ToString(),
                    Usuario = p.UsuarioRegistro
                };
            });

        var todas = logFacturas.Concat(logPagos);
        if (!string.IsNullOrEmpty(tipo))    todas = todas.Where(e => e.Tipo == tipo);
        if (!string.IsNullOrEmpty(usuario)) todas = todas.Where(e => e.Usuario == usuario);
        var lista = todas.OrderByDescending(e => e.Fecha).ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Log de Transacciones");

        ws.Cell(1, 1).Value = $"AD ALL DIGITAL ENTERPRISE — Log de Transacciones {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}";
        ws.Range(1, 1, 1, 8).Merge().Style.Font.SetBold(true).Font.SetFontSize(12)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#1a3a5c")).Font.SetFontColor(XLColor.White)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        string[] cols = { "Fecha", "Tipo", "N° Referencia", "Ticket", "Empresa", "Cliente", "Monto (RD$)", "Detalle", "Usuario" };
        for (int c = 0; c < cols.Length; c++)
        {
            ws.Cell(3, c+1).Value = cols[c];
            ws.Cell(3, c+1).Style.Font.SetBold(true)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#2d6db5")).Font.SetFontColor(XLColor.White);
        }

        int row = 4;
        foreach (var e in lista)
        {
            ws.Cell(row, 1).Value = e.Fecha.ToString("dd/MM/yyyy HH:mm");
            ws.Cell(row, 2).Value = e.Tipo;
            ws.Cell(row, 3).Value = e.NumRef;
            ws.Cell(row, 4).Value = e.NumTicket;
            ws.Cell(row, 5).Value = e.Empresa;
            ws.Cell(row, 6).Value = e.Cliente;
            ws.Cell(row, 7).Value = e.Monto;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 8).Value = e.Detalle;
            ws.Cell(row, 9).Value = e.Usuario;

            if (e.Tipo == "Pago")
                ws.Row(row).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#d4edda"));
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.Column(3).Width = 24;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"LogTransacciones_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
