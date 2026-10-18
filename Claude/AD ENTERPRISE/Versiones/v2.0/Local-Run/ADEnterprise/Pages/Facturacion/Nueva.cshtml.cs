using ADEnterprise.Models;
using System;
using ADEnterprise.Helpers;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace ADEnterprise.Pages.Facturacion;

public class NuevaModel : PageModel
{
    private readonly FacturaService  _facturas;
    private readonly CompaniaService _companias;
    private readonly ClienteService  _clientes;
    private readonly CajaService     _caja;

    public NuevaModel(FacturaService f, CompaniaService c, ClienteService cl, CajaService caja)
    {
        _facturas  = f;
        _companias = c;
        _clientes  = cl;
        _caja      = caja;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Compania> Companias         { get; set; } = new();
    public List<Cliente>  Clientes          { get; set; } = new();
    public decimal        BalanceDisponible { get; set; }   // balance en tiempo real del cuadre activo
    public string         FormToken         { get; set; } = "";

    public class InputModel
    {
        [Required(ErrorMessage = "El N° de Ticket es requerido")]
        public int NumTicket { get; set; }

        [Required(ErrorMessage = "La compañía es requerida")]
        public string IdCompania { get; set; } = string.Empty;

        [Required(ErrorMessage = "El cliente es requerido")]
        public string IdCliente { get; set; } = string.Empty;

        [Required(ErrorMessage = "La fecha es requerida")]
        public DateTime FechaFactura { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "El monto es requerido")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a cero")]
        public decimal Monto { get; set; }

        /// <summary>Monto neto que paga el factor al cliente (factoring). 0 = sin factoring.</summary>
        [Range(0, double.MaxValue, ErrorMessage = "El monto pagado no puede ser negativo")]
        public decimal MontoPagado { get; set; }

        public string Notas { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        SetViewData();
        var balance = await _caja.GetBalanceDisponibleAsync();
        if (balance == null)
        {
            TempData["Error"] = "⚠️ No hay un Cuadre de Caja abierto. Debe abrir la caja antes de registrar un Pago Factoring.";
            return RedirectToPage("/Finanzas/Caja");
        }
        BalanceDisponible  = balance.Value;
        Companias          = await _companias.ObtenerTodasAsync(soloActivas: true);
        Clientes           = await _clientes.ObtenerTodosAsync(soloActivos: true);
        Input.NumTicket    = await _facturas.SiguienteTicketAsync();
        Input.FechaFactura = DateTime.Today;
        FormToken          = FormTokenHelper.Generar(TempData, "Token_Facturacion");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        SetViewData();
        var balance = await _caja.GetBalanceDisponibleAsync();
        if (balance == null)
        {
            TempData["Error"] = "⚠️ No hay un Cuadre de Caja abierto. La operación no puede procesarse.";
            return RedirectToPage("/Finanzas/Caja");
        }
        BalanceDisponible = balance.Value;
        Companias = await _companias.ObtenerTodasAsync(soloActivas: true);
        Clientes  = await _clientes.ObtenerTodosAsync(soloActivos: true);

        // Validar token de envío único (anti-duplicado)
        var tokenEnviado = Request.Form["FormToken"].ToString();
        if (!FormTokenHelper.Validar(TempData, "Token_Facturacion", tokenEnviado))
        {
            TempData["Advertencia"] = "⚠️ Esta transacción ya fue procesada o el formulario expiró. Verifique la lista antes de registrar nuevamente.";
            return RedirectToPage("/Facturacion/Consulta");
        }

        if (!ModelState.IsValid)
        {
            FormToken = FormTokenHelper.Generar(TempData, "Token_Facturacion");
            return Page();
        }

        // Resolver nombres a partir de los IDs seleccionados
        var compania = Companias.FirstOrDefault(c => c.Id == Input.IdCompania);
        var cliente  = Clientes.FirstOrDefault(c => c.Id == Input.IdCliente);

        if (compania == null)
        {
            ModelState.AddModelError("Input.IdCompania", "Compañía no encontrada.");
            return Page();
        }
        if (cliente == null)
        {
            ModelState.AddModelError("Input.IdCliente", "Cliente no encontrado.");
            return Page();
        }

        var usuario = User.FindFirst("NombreCompleto")?.Value
                      ?? User.Identity?.Name ?? "Sistema";

        // Validar: MontoPagado no puede exceder Monto
        if (Input.MontoPagado > Input.Monto)
        {
            ModelState.AddModelError("Input.MontoPagado", "El monto pagado no puede exceder el monto de la factura.");
            return Page();
        }

        var factura = new Factura
        {
            NumTicket        = Input.NumTicket,
            IdCompania       = compania.Id,
            EmpresaNombre    = compania.Nombre,
            IdCliente        = cliente.Id,
            ClienteNombre    = cliente.NombreCompleto,
            FechaFactura     = Input.FechaFactura,
            Monto            = Input.Monto,
            MontoPagado      = Input.MontoPagado,
            Notas            = Input.Notas,
            UsuarioMigracion = usuario
        };

        try
        {
            await _facturas.CrearAsync(factura, usuario);
            TempData["Exito"] = $"✅ Factura N° {factura.NumTicket} creada. Transacción: {factura.NumTransaccion}";
            return RedirectToPage("/Facturacion/Consulta");
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"❌ Error al guardar la factura: {ex.Message}";
            return Page();
        }
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
