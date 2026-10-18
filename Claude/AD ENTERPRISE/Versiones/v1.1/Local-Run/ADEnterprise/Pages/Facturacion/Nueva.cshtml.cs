using ADEnterprise.Models;
using System;
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

    public NuevaModel(FacturaService f, CompaniaService c, ClienteService cl)
    {
        _facturas  = f;
        _companias = c;
        _clientes  = cl;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Compania> Companias { get; set; } = new();
    public List<Cliente>  Clientes  { get; set; } = new();

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

        public string Notas { get; set; } = string.Empty;
    }

    public async Task OnGetAsync()
    {
        SetViewData();
        Companias = await _companias.ObtenerTodasAsync(soloActivas: true);
        Clientes  = await _clientes.ObtenerTodosAsync(soloActivos: true);
        Input.NumTicket = await _facturas.SiguienteTicketAsync();
        Input.FechaFactura = DateTime.Today;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        SetViewData();
        Companias = await _companias.ObtenerTodasAsync(soloActivas: true);
        Clientes  = await _clientes.ObtenerTodosAsync(soloActivos: true);

        if (!ModelState.IsValid) return Page();

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

        var factura = new Factura
        {
            NumTicket        = Input.NumTicket,
            IdCompania       = compania.Id,
            EmpresaNombre    = compania.Nombre,
            IdCliente        = cliente.Id,
            ClienteNombre    = cliente.NombreCompleto,
            FechaFactura     = Input.FechaFactura,
            Monto            = Input.Monto,
            Notas            = Input.Notas,
            UsuarioMigracion = User.FindFirst("NombreCompleto")?.Value
                               ?? User.Identity?.Name
                               ?? "Sistema"
        };

        try
        {
            await _facturas.CrearAsync(factura);
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
