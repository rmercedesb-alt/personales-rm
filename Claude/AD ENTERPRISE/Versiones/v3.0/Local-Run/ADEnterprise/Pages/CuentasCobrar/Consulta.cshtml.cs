using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.CuentasCobrar;

public class ConsultaPagosModel : PageModel
{
    private readonly PagoService    _pagos;
    private readonly FacturaService _facturas;

    public ConsultaPagosModel(PagoService pagos, FacturaService facturas)
    {
        _pagos    = pagos;
        _facturas = facturas;
    }

    // ── Línea de pago enriquecida ─────────────────────────────────────────────
    public class PagoDetallado
    {
        public Pago   Pago          { get; set; } = new();
        public string EmpresaNombre { get; set; } = string.Empty;
        public string ClienteNombre { get; set; } = string.Empty;
        public string EstadoFactura { get; set; } = string.Empty;
    }

    // ── Grupo por Recibo (agrupa todos los tickets de un mismo instrumento) ───
    public class GrupoRecibo
    {
        public string         NumRecibo        { get; set; } = string.Empty;
        public string         NumCheque        { get; set; } = string.Empty;
        public string         FormaPago        { get; set; } = string.Empty;
        public DateTime       Fecha            { get; set; }
        public decimal        MontoInstrumento { get; set; }
        public List<PagoDetallado> Pagos       { get; set; } = new();

        public decimal TotalAplicado => Pagos.Sum(p => p.Pago.MontoPagado);
        public decimal Balance       => MontoInstrumento - TotalAplicado;
        public string  UsuarioRegistro => Pagos.FirstOrDefault()?.Pago.UsuarioRegistro ?? "";
    }

    // ── Propiedades expuestas a la vista ─────────────────────────────────────
    public List<GrupoRecibo> Recibos  { get; set; } = new();
    public List<string>      Empresas { get; set; } = new();
    public List<string>      Clientes { get; set; } = new();
    public List<string>      NosRecibo{ get; set; } = new();
    public List<string>      NosCheque{ get; set; } = new();

    // ── Parámetro de apertura automática ─────────────────────────────────────
    public string ReciboAbierto { get; set; } = string.Empty;

    public async Task OnGetAsync(string? recibo = null)
    {
        SetViewData();
        ReciboAbierto = recibo ?? string.Empty;

        try
        {
            var todosPagos    = await _pagos.ObtenerTodosAsync();
            var todasFacturas = await _facturas.ObtenerTodasAsync();
            var mapaFacturas  = todasFacturas.ToDictionary(f => f.NumTransaccion, f => f);

            // Enriquecer pagos con datos de factura
            var detallados = todosPagos.Select(p => {
                mapaFacturas.TryGetValue(p.NumTransaccionFactura, out var f);
                return new PagoDetallado
                {
                    Pago          = p,
                    EmpresaNombre = !string.IsNullOrEmpty(p.EmpresaNombre) ? p.EmpresaNombre
                                    : (f?.EmpresaNombre ?? ""),
                    ClienteNombre = !string.IsNullOrEmpty(p.ClienteNombre) ? p.ClienteNombre
                                    : (f?.ClienteNombre ?? ""),
                    EstadoFactura = f?.Estado.ToString() ?? ""
                };
            }).ToList();

            // Agrupar por NumRecibo (pagos sin recibo agrupan por NumTransaccionPago)
            Recibos = detallados
                .GroupBy(d => string.IsNullOrEmpty(d.Pago.NumRecibo)
                    ? $"__LEGACY__{d.Pago.NumTransaccionPago}"
                    : d.Pago.NumRecibo)
                .Select(g => {
                    var primero = g.First().Pago;
                    return new GrupoRecibo
                    {
                        NumRecibo        = g.Key.StartsWith("__LEGACY__")
                            ? primero.NumTransaccionPago : primero.NumRecibo,
                        NumCheque        = primero.NumChequeReferencia,
                        FormaPago        = primero.FormaPago.ToString(),
                        Fecha            = primero.FechaPago,
                        MontoInstrumento = primero.MontoInstrumento > 0
                            ? primero.MontoInstrumento
                            : g.Sum(d => d.Pago.MontoPagado),
                        Pagos            = g.OrderBy(d => d.Pago.NumTicket).ToList()
                    };
                })
                .OrderByDescending(r => r.Fecha)
                .ThenByDescending(r => r.NumRecibo)
                .ToList();

            // Listas para filtros
            NosRecibo = Recibos.Select(r => r.NumRecibo)
                .Where(r => !r.StartsWith("P") || r.Length < 15) // excluir legados muy largos
                .OrderByDescending(r => r).Take(100).ToList();
            NosCheque = Recibos.Select(r => r.NumCheque)
                .Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
            Empresas  = detallados.Select(d => d.EmpresaNombre)
                .Where(e => !string.IsNullOrEmpty(e)).Distinct().OrderBy(e => e).ToList();
            Clientes  = detallados.Select(d => d.ClienteNombre)
                .Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
        }
        catch
        {
            Recibos  = new(); Empresas = new(); Clientes = new();
        }
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
