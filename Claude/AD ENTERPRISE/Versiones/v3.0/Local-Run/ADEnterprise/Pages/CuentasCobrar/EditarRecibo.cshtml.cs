using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace ADEnterprise.Pages.CuentasCobrar;

public class EditarReciboModel : PageModel
{
    private readonly PagoService    _pagos;
    private readonly FacturaService _facturas;

    public EditarReciboModel(PagoService pagos, FacturaService facturas)
    {
        _pagos    = pagos;
        _facturas = facturas;
    }

    // ── DTO por línea ──────────────────────────────────────────────────────────
    public class LineaDetalle
    {
        public Pago   Pago          { get; set; } = new();
        public string EmpresaNombre { get; set; } = string.Empty;
        public string ClienteNombre { get; set; } = string.Empty;
        public string TipoLinea     { get; set; } = "Factura";
        public string EstadoFactura { get; set; } = string.Empty;
        public decimal SaldoPendiente { get; set; }
    }

    // ── Datos de vista ─────────────────────────────────────────────────────────
    public string         NumRecibo        { get; set; } = string.Empty;
    public List<LineaDetalle> Lineas       { get; set; } = new();
    public decimal        TotalAplicado    => Lineas.Sum(l => l.Pago.MontoPagado);
    public bool           Encontrado       { get; set; }

    // ── Binding del formulario de edición ─────────────────────────────────────
    [BindProperty]
    public HeaderInput Input { get; set; } = new();

    public class HeaderInput
    {
        public string   NumRecibo           { get; set; } = string.Empty;

        [Required(ErrorMessage = "Fecha requerida")]
        public DateTime FechaPago           { get; set; } = DateTime.Today;

        [Required]
        public string   FormaPago           { get; set; } = "Cheque";

        public string   NumChequeReferencia { get; set; } = string.Empty;
        public string   Notas               { get; set; } = string.Empty;

        [Range(0.01, double.MaxValue, ErrorMessage = "Monto debe ser mayor a 0")]
        public decimal  MontoInstrumento    { get; set; }
    }

    // ── GET ───────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnGetAsync(string num)
    {
        SetViewData();
        if (string.IsNullOrWhiteSpace(num))
            return RedirectToPage("/CuentasCobrar/Consulta");

        await CargarReciboAsync(num);
        return Page();
    }

    // ── POST: Guardar edición del encabezado ──────────────────────────────────
    public async Task<IActionResult> OnPostGuardarAsync()
    {
        SetViewData();
        if (!ModelState.IsValid)
        {
            await CargarReciboAsync(Input.NumRecibo);
            return Page();
        }

        var usuario  = User.FindFirst("NombreCompleto")?.Value ?? "Sistema";
        var formaPago = Enum.TryParse<FormaPago>(Input.FormaPago, out var fp) ? fp : FormaPago.Cheque;

        await _pagos.ActualizarHeaderReciboAsync(
            Input.NumRecibo,
            Input.FechaPago,
            formaPago,
            Input.NumChequeReferencia,
            Input.Notas,
            Input.MontoInstrumento,
            usuario);

        TempData["Exito"] = $"Recibo <strong>{Input.NumRecibo}</strong> actualizado correctamente.";
        return RedirectToPage(new { num = Input.NumRecibo });
    }

    // ── POST: Anular/eliminar recibo completo ─────────────────────────────────
    public async Task<IActionResult> OnPostAnularAsync(string numRecibo)
    {
        SetViewData();
        var usuario = User.FindFirst("NombreCompleto")?.Value ?? "Sistema";

        var msg = await _pagos.AnularReciboAsync(numRecibo, usuario);

        TempData["Exito"] = $"✅ {msg}";
        return RedirectToPage("/CuentasCobrar/Consulta");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private async Task CargarReciboAsync(string numRecibo)
    {
        NumRecibo = numRecibo;

        var todosPagos    = await _pagos.ObtenerTodosAsync();
        var todasFacturas = await _facturas.ObtenerTodasAsync();
        var mapaFacturas  = todasFacturas.ToDictionary(f => f.NumTransaccion, f => f);

        var pagosRecibo = todosPagos
            .Where(p => p.NumRecibo == numRecibo || p.NumTransaccionPago == numRecibo)
            .OrderBy(p => p.NumTicket)
            .ToList();

        if (!pagosRecibo.Any()) { Encontrado = false; return; }

        Encontrado = true;
        var primero = pagosRecibo.First();

        // Pre-llenar el formulario con datos actuales
        Input = new HeaderInput
        {
            NumRecibo           = numRecibo,
            FechaPago           = primero.FechaPago,
            FormaPago           = primero.FormaPago.ToString(),
            NumChequeReferencia = primero.NumChequeReferencia,
            Notas               = primero.Notas,
            MontoInstrumento    = primero.MontoInstrumento > 0
                                    ? primero.MontoInstrumento
                                    : pagosRecibo.Sum(p => p.MontoPagado)
        };

        Lineas = pagosRecibo.Select(p =>
        {
            mapaFacturas.TryGetValue(p.NumTransaccionFactura, out var f);
            var esCredito = f?.Estado == EstadoFactura.Credito;
            return new LineaDetalle
            {
                Pago          = p,
                EmpresaNombre = !string.IsNullOrEmpty(p.EmpresaNombre) ? p.EmpresaNombre : (f?.EmpresaNombre ?? ""),
                ClienteNombre = !string.IsNullOrEmpty(p.ClienteNombre) ? p.ClienteNombre : (f?.ClienteNombre ?? ""),
                TipoLinea     = esCredito ? "Abono" : "Factura",
                EstadoFactura = f?.Estado.ToString() ?? "",
                SaldoPendiente = f?.SaldoPendiente ?? 0
            };
        }).ToList();
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
