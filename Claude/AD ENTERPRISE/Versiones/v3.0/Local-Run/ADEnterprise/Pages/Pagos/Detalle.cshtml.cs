using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace ADEnterprise.Pages.Pagos;

public class DetallePagoModel : PageModel
{
    private readonly PagoService    _pagos;
    private readonly FacturaService _facturas;

    public DetallePagoModel(PagoService pagos, FacturaService facturas)
    {
        _pagos    = pagos;
        _facturas = facturas;
    }

    public Pago?    Pago    { get; set; }
    public Factura? Factura { get; set; }
    public bool     EsAdmin { get; set; }

    [BindProperty]
    public EditarPagoInput Input { get; set; } = new();

    public class EditarPagoInput
    {
        [Required] public string NumTransaccionPago { get; set; } = string.Empty;

        [Required(ErrorMessage = "La fecha es requerida")]
        public DateTime FechaPago { get; set; }

        [Required(ErrorMessage = "El monto es requerido")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a cero")]
        public decimal MontoPagado { get; set; }

        [Required] public FormaPago FormaPago { get; set; }

        public string NumChequeReferencia { get; set; } = string.Empty;
        public string Notas               { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        SetViewData();
        if (string.IsNullOrEmpty(id))
            return RedirectToPage("/Pagos/Consulta");

        var todos = await _pagos.ObtenerTodosAsync();
        Pago = todos.FirstOrDefault(p => p.NumTransaccionPago == id);

        if (Pago == null)
        {
            TempData["Error"] = $"No se encontró el pago: {id}";
            return RedirectToPage("/Pagos/Consulta");
        }

        Factura = await _facturas.BuscarPorTransaccionAsync(Pago.NumTransaccionFactura);

        Input = new EditarPagoInput
        {
            NumTransaccionPago  = Pago.NumTransaccionPago,
            FechaPago           = Pago.FechaPago,
            MontoPagado         = Pago.MontoPagado,
            FormaPago           = Pago.FormaPago,
            NumChequeReferencia = Pago.NumChequeReferencia,
            Notas               = Pago.Notas
        };

        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        SetViewData();

        if (!ModelState.IsValid)
        {
            var todos = await _pagos.ObtenerTodosAsync();
            Pago    = todos.FirstOrDefault(p => p.NumTransaccionPago == Input.NumTransaccionPago);
            Factura = Pago != null ? await _facturas.BuscarPorTransaccionAsync(Pago.NumTransaccionFactura) : null;
            return Page();
        }

        // Verificar permiso
        var perfil = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (perfil != "Administrador")
        {
            TempData["Error"] = "Solo administradores pueden editar pagos.";
            return RedirectToPage("/Pagos/Consulta");
        }

        var usuario = User.FindFirst("NombreCompleto")?.Value ?? User.Identity?.Name ?? "Sistema";

        // Cargar pago actual
        var todos2 = await _pagos.ObtenerTodosAsync();
        var pago   = todos2.FirstOrDefault(p => p.NumTransaccionPago == Input.NumTransaccionPago);
        if (pago == null)
        {
            TempData["Error"] = "No se encontró el pago.";
            return RedirectToPage("/Pagos/Consulta");
        }

        // Aplicar cambios (IDs se conservan del pago original o se re-derivan de la factura)
        pago.FechaPago           = Input.FechaPago;
        pago.MontoPagado         = Input.MontoPagado;
        pago.FormaPago           = Input.FormaPago;
        pago.NumChequeReferencia = Input.NumChequeReferencia;
        pago.Notas               = Input.Notas;

        // Si el pago no tiene IDs (dato histórico), obtenerlos de la factura
        if (string.IsNullOrEmpty(pago.IdCompania) || string.IsNullOrEmpty(pago.IdCliente))
        {
            var facAsoc = await _facturas.BuscarPorTransaccionAsync(pago.NumTransaccionFactura);
            if (facAsoc != null)
            {
                pago.IdCompania = facAsoc.IdCompania;
                pago.IdCliente  = facAsoc.IdCliente;
            }
        }

        // Guardar pago + recalcular balance de la factura
        await _pagos.ActualizarAsync(pago, usuario);

        TempData["Exito"] = $"✅ Pago {pago.NumTransaccionPago} actualizado. Balance de factura recalculado.";
        return RedirectToPage("/Pagos/Detalle", new { id = pago.NumTransaccionPago });
    }

    private void SetViewData()
    {
        var perfil = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        EsAdmin    = perfil == "Administrador";
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = perfil;
    }
}
