using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ADEnterprise.Pages.Facturacion;

public class DetalleModel : PageModel
{
    private readonly FacturaService  _facturas;
    private readonly PagoService     _pagos;
    private readonly CompaniaService _companias;
    private readonly ClienteService  _clientes;

    public DetalleModel(FacturaService facturas, PagoService pagos,
                        CompaniaService companias, ClienteService clientes)
    {
        _facturas  = facturas;
        _pagos     = pagos;
        _companias = companias;
        _clientes  = clientes;
    }

    public Factura?        Factura   { get; set; }
    public List<Pago>      Pagos     { get; set; } = new();
    public List<Compania>  Companias { get; set; } = new();
    public List<Cliente>   Clientes  { get; set; } = new();
    public bool            EsAdmin   { get; set; }

    [BindProperty]
    public EditarFacturaInput Input { get; set; } = new();

    public class EditarFacturaInput
    {
        [Required] public string NumTransaccion { get; set; } = string.Empty;

        [Required(ErrorMessage = "La compañía es requerida")]
        public string IdCompania { get; set; } = string.Empty;

        [Required(ErrorMessage = "El cliente es requerido")]
        public string IdCliente { get; set; } = string.Empty;

        [Required(ErrorMessage = "La fecha es requerida")]
        public DateTime FechaFactura { get; set; }

        [Required(ErrorMessage = "El monto es requerido")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a cero")]
        public decimal Monto { get; set; }

        public string Notas { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        SetViewData();
        if (string.IsNullOrEmpty(id))
            return RedirectToPage("/Facturacion/Consulta");

        Factura = await _facturas.BuscarPorTransaccionAsync(id);
        if (Factura == null)
        {
            TempData["Error"] = $"No se encontró la factura con ID: {id}";
            return RedirectToPage("/Facturacion/Consulta");
        }

        Pagos     = await _pagos.ObtenerPorFacturaAsync(id);
        Companias = await _companias.ObtenerTodasAsync();
        Clientes  = await _clientes.ObtenerTodosAsync();

        // Pre-seleccionar compañía y cliente:
        // Si la factura ya tiene IDs, usarlos; si no, buscar por nombre (datos migrados)
        var idComp = Factura.IdCompania;
        if (string.IsNullOrEmpty(idComp))
            idComp = Companias.FirstOrDefault(c =>
                c.Nombre.Equals(Factura.EmpresaNombre, StringComparison.OrdinalIgnoreCase))?.Id ?? "";

        var idCli = Factura.IdCliente;
        if (string.IsNullOrEmpty(idCli))
            idCli = Clientes.FirstOrDefault(c =>
                c.NombreCompleto.Equals(Factura.ClienteNombre, StringComparison.OrdinalIgnoreCase))?.Id ?? "";

        Input = new EditarFacturaInput
        {
            NumTransaccion = Factura.NumTransaccion,
            IdCompania     = idComp,
            IdCliente      = idCli,
            FechaFactura   = Factura.FechaFactura,
            Monto          = Factura.Monto,
            Notas          = Factura.Notas
        };

        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        SetViewData();

        // Cargar listas antes de validar (necesarias para mostrar la página si hay error)
        Companias = await _companias.ObtenerTodasAsync();
        Clientes  = await _clientes.ObtenerTodosAsync();

        if (!ModelState.IsValid)
        {
            Factura = await _facturas.BuscarPorTransaccionAsync(Input.NumTransaccion);
            Pagos   = await _pagos.ObtenerPorFacturaAsync(Input.NumTransaccion);
            return Page();
        }

        // Verificar permiso
        var perfil = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (perfil != "Administrador")
        {
            TempData["Error"] = "Solo administradores pueden editar facturas.";
            return RedirectToPage("/Facturacion/Consulta");
        }

        var factura = await _facturas.BuscarPorTransaccionAsync(Input.NumTransaccion);
        if (factura == null)
        {
            TempData["Error"] = "No se encontró la factura.";
            return RedirectToPage("/Facturacion/Consulta");
        }

        // Resolver nombres a partir de los IDs
        var compania = Companias.FirstOrDefault(c => c.Id == Input.IdCompania);
        var cliente  = Clientes.FirstOrDefault(c => c.Id == Input.IdCliente);

        if (compania == null)
        { ModelState.AddModelError("Input.IdCompania", "Compañía no encontrada."); return Page(); }
        if (cliente == null)
        { ModelState.AddModelError("Input.IdCliente", "Cliente no encontrado."); return Page(); }

        var usuario = User.FindFirst("NombreCompleto")?.Value ?? User.Identity?.Name ?? "Sistema";

        // Snapshot antes de editar
        var snapshot = JsonSerializer.Serialize(new
        {
            factura.EmpresaNombre, factura.ClienteNombre, factura.IdCompania, factura.IdCliente,
            factura.FechaFactura, factura.Monto, factura.Notas, factura.Estado
        });

        // Aplicar cambios
        factura.IdCompania          = compania.Id;
        factura.EmpresaNombre       = compania.Nombre;
        factura.IdCliente           = cliente.Id;
        factura.ClienteNombre       = cliente.NombreCompleto;
        factura.FechaFactura        = Input.FechaFactura;
        factura.Monto               = Input.Monto;
        factura.Notas               = Input.Notas;
        factura.ValorAnterior       = snapshot;
        factura.FechaModificacion   = DateTime.Now;
        factura.UsuarioModificacion = usuario;

        // Recalcular estado si cambió el monto
        if (factura.PagadoALaFecha >= factura.Monto && factura.Monto > 0)
        {
            factura.PagadoALaFecha = factura.Monto;
            factura.Estado = EstadoFactura.Pagada;
        }
        else if (factura.PagadoALaFecha > 0)
            factura.Estado = EstadoFactura.PagadoParcial;
        else
            factura.Estado = EstadoFactura.Pendiente;

        await _facturas.ActualizarAsync(factura);

        TempData["Exito"] = $"✅ Factura #{factura.NumTicket} actualizada. Empresa: {factura.EmpresaNombre} | Cliente: {factura.ClienteNombre}";
        return RedirectToPage("/Facturacion/Detalle", new { id = factura.NumTransaccion });
    }

    private void SetViewData()
    {
        var perfil = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        EsAdmin    = perfil == "Administrador";
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = perfil;
    }
}
