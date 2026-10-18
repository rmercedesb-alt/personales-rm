using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Pagos;

public class ImprimirReciboModel : PageModel
{
    private readonly PagoService    _pagos;
    private readonly FacturaService _facturas;

    public ImprimirReciboModel(PagoService pagos, FacturaService facturas)
    {
        _pagos    = pagos;
        _facturas = facturas;
    }

    public class LineaRecibo
    {
        public Pago   Pago          { get; set; } = new();
        public string EmpresaNombre { get; set; } = string.Empty;
        public string ClienteNombre { get; set; } = string.Empty;
        public string TipoLinea     { get; set; } = "Factura";  // "Factura" | "Abono"
        public string EstadoFactura { get; set; } = string.Empty;
    }

    public string NumRecibo        { get; set; } = string.Empty;
    public string FormaPago        { get; set; } = string.Empty;
    public string NumCheque        { get; set; } = string.Empty;
    public DateTime Fecha          { get; set; }
    public decimal MontoInstrumento { get; set; }
    public decimal TotalAplicado   { get; set; }
    public string  UsuarioRegistro { get; set; } = string.Empty;
    public string  Notas           { get; set; } = string.Empty;
    public List<LineaRecibo> Lineas { get; set; } = new();
    public bool Encontrado         { get; set; }

    public async Task<IActionResult> OnGetAsync(string num)
    {
        if (string.IsNullOrWhiteSpace(num))
            return RedirectToPage("/Pagos/Consulta");

        NumRecibo = num;

        var todosPagos    = await _pagos.ObtenerTodosAsync();
        var todasFacturas = await _facturas.ObtenerTodasAsync();
        var mapaFacturas  = todasFacturas.ToDictionary(f => f.NumTransaccion, f => f);

        // Buscar pagos del recibo
        var pagosRecibo = todosPagos
            .Where(p => p.NumRecibo == num || p.NumTransaccionPago == num)
            .OrderBy(p => p.NumTicket)
            .ToList();

        if (!pagosRecibo.Any())
        {
            Encontrado = false;
            return Page();
        }

        Encontrado = true;
        var primero = pagosRecibo.First();
        FormaPago        = primero.FormaPago.ToString();
        NumCheque        = primero.NumChequeReferencia;
        Fecha            = primero.FechaPago;
        MontoInstrumento = primero.MontoInstrumento > 0 ? primero.MontoInstrumento : pagosRecibo.Sum(p => p.MontoPagado);
        UsuarioRegistro  = primero.UsuarioRegistro;
        Notas            = primero.Notas;
        TotalAplicado    = pagosRecibo.Sum(p => p.MontoPagado);

        Lineas = pagosRecibo.Select(p => {
            mapaFacturas.TryGetValue(p.NumTransaccionFactura, out var f);
            var esCredito = f?.Estado == EstadoFactura.Credito;
            return new LineaRecibo
            {
                Pago          = p,
                EmpresaNombre = !string.IsNullOrEmpty(p.EmpresaNombre) ? p.EmpresaNombre : (f?.EmpresaNombre ?? ""),
                ClienteNombre = !string.IsNullOrEmpty(p.ClienteNombre) ? p.ClienteNombre : (f?.ClienteNombre ?? ""),
                TipoLinea     = esCredito ? "Abono" : "Factura",
                EstadoFactura = f?.Estado.ToString() ?? ""
            };
        }).ToList();

        return Page();
    }
}
