using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ADEnterprise.Pages.Pagos;

public class RegistrarModel : PageModel
{
    private readonly FacturaService  _facturas;
    private readonly PagoService     _pagos;
    private readonly ClienteService  _clientes;
    private readonly CompaniaService _companias;

    public RegistrarModel(FacturaService f, PagoService p, ClienteService cl, CompaniaService co)
    {
        _facturas  = f;
        _pagos     = p;
        _clientes  = cl;
        _companias = co;
    }

    // ── Datos para la UI ─────────────────────────────────────────────────────
    public string   FacturasPendientesJson { get; set; } = "[]";
    public string   CompaniasJson          { get; set; } = "[]";
    public string   ClientesJson           { get; set; } = "[]";

    [BindProperty]
    public NuevoReciboInput Input { get; set; } = new();

    // ── Modelo de entrada ────────────────────────────────────────────────────
    public class NuevoReciboInput
    {
        [Required(ErrorMessage = "La fecha del recibo es requerida")]
        public DateTime FechaPago { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "La forma de pago es requerida")]
        public string FormaPago { get; set; } = "Cheque";

        [Required(ErrorMessage = "El monto total del instrumento es requerido")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a cero")]
        public decimal MontoInstrumento { get; set; }

        public string NumChequeReferencia { get; set; } = string.Empty;
        public string Notas               { get; set; } = string.Empty;

        // JSON con las aplicaciones: [{numTransaccion, numTicket, montoAplicado,
        //   empresaNombre, clienteNombre, idCompania, idCliente}]
        public string DetalleJson { get; set; } = "[]";

        // Abono a cuenta (opcional)
        public bool    TieneAbono     { get; set; }
        public int     AbonoTicket    { get; set; }
        public string  AbonoIdComp    { get; set; } = string.Empty;
        public string  AbonoIdCli     { get; set; } = string.Empty;
        public decimal AbonoMonto     { get; set; }
    }

    // ── DTO para serialización JSON de facturas ───────────────────────────────
    private record FacturaDto(
        string NumTransaccion, int NumTicket, string EmpresaNombre, string ClienteNombre,
        string IdCompania, string IdCliente, decimal Monto, decimal PagadoALaFecha,
        decimal SaldoPendiente, string Estado, string FechaFactura);

    // ── GET ──────────────────────────────────────────────────────────────────
    public async Task OnGetAsync(int ticket = 0)
    {
        SetViewData();
        Input.FechaPago = DateTime.Today;

        var todasFact   = await _facturas.ObtenerTodasAsync();
        var companias   = await _companias.ObtenerTodasAsync(soloActivas: true);
        var clientes    = await _clientes.ObtenerTodosAsync(soloActivos: true);

        // Solo facturas con saldo pendiente (pendiente, parcial, vencida) — no Pagadas ni Credito
        var pendientes = todasFact
            .Where(f => f.Estado != EstadoFactura.Pagada && f.Estado != EstadoFactura.Credito
                        && f.SaldoPendiente > 0)
            .OrderBy(f => f.FechaFactura)
            .ToList();

        FacturasPendientesJson = JsonSerializer.Serialize(pendientes.Select(f => new FacturaDto(
            f.NumTransaccion, f.NumTicket, f.EmpresaNombre, f.ClienteNombre,
            f.IdCompania, f.IdCliente, f.Monto, f.PagadoALaFecha, f.SaldoPendiente,
            f.Estado.ToString(), f.FechaFactura.ToString("dd/MM/yyyy")
        )));

        CompaniasJson = JsonSerializer.Serialize(companias.Select(c => new { c.Id, c.Nombre }));
        ClientesJson  = JsonSerializer.Serialize(clientes.Select(c => new { c.Id, Nombre = c.NombreCompleto }));

        // Si viene con ticket pre-seleccionado (desde detalle de factura)
        if (ticket > 0)
        {
            var fac = pendientes.FirstOrDefault(f => f.NumTicket == ticket);
            if (fac != null)
            {
                Input.MontoInstrumento = fac.SaldoPendiente;
                // Pre-carga el detalle con este ticket
                var det = new[]{ new {
                    numTransaccion = fac.NumTransaccion,
                    numTicket      = fac.NumTicket,
                    montoAplicado  = fac.SaldoPendiente,
                    empresaNombre  = fac.EmpresaNombre,
                    clienteNombre  = fac.ClienteNombre,
                    idCompania     = fac.IdCompania,
                    idCliente      = fac.IdCliente,
                    saldoOriginal  = fac.SaldoPendiente
                }};
                Input.DetalleJson = JsonSerializer.Serialize(det);
            }
        }
    }

    // ── POST ─────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostAsync()
    {
        SetViewData();

        var forma = Input.FormaPago;
        if ((forma == "Cheque" || forma == "Transferencia" || forma == "Deposito")
            && string.IsNullOrWhiteSpace(Input.NumChequeReferencia))
        {
            ModelState.AddModelError("Input.NumChequeReferencia",
                "El N° de cheque / referencia es obligatorio para esta forma de pago.");
        }

        if (!ModelState.IsValid)
        {
            await RecargarDatosAsync();
            return Page();
        }

        // Deserializar el detalle de tickets aplicados
        List<DetalleItem> detalle;
        try
        {
            detalle = JsonSerializer.Deserialize<List<DetalleItem>>(Input.DetalleJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<DetalleItem>();
        }
        catch
        {
            detalle = new List<DetalleItem>();
        }

        if (detalle.Count == 0 && !Input.TieneAbono)
        {
            ModelState.AddModelError("", "Debe agregar al menos una factura o un abono a cuenta.");
            await RecargarDatosAsync();
            return Page();
        }

        var usuario  = User.FindFirst("NombreCompleto")?.Value ?? User.Identity?.Name ?? "Sistema";
        var numRecibo = await _pagos.SiguienteNumReciboAsync();
        var formaPago = Enum.TryParse<FormaPago>(Input.FormaPago, out var fp) ? fp : FormaPago.Cheque;

        // Construir lista de pagos
        var pagos = detalle.Select(d => new Pago
        {
            NumTransaccionFactura = d.NumTransaccion,
            NumTicket             = d.NumTicket,
            FechaPago             = Input.FechaPago,
            MontoPagado           = d.MontoAplicado,
            FormaPago             = formaPago,
            NumChequeReferencia   = Input.NumChequeReferencia,
            Notas                 = Input.Notas,
            IdCompania            = d.IdCompania,
            IdCliente             = d.IdCliente,
            EmpresaNombre         = d.EmpresaNombre,
            ClienteNombre         = d.ClienteNombre,
            MontoInstrumento      = Input.MontoInstrumento
        }).ToList();

        // Registrar el recibo
        await _pagos.RegistrarReciboAsync(numRecibo, Input.MontoInstrumento, pagos, usuario);

        // Abono a cuenta (si aplica)
        if (Input.TieneAbono && Input.AbonoMonto > 0)
        {
            var companias = await _companias.ObtenerTodasAsync();
            var clientes  = await _clientes.ObtenerTodosAsync();
            var comp      = companias.FirstOrDefault(c => c.Id == Input.AbonoIdComp);
            var cli       = clientes.FirstOrDefault(c => c.Id == Input.AbonoIdCli);

            // Crear factura de crédito
            var credito = await _facturas.CrearCreditoAsync(
                Input.AbonoMonto,
                Input.AbonoTicket > 0 ? Input.AbonoTicket : 0,
                comp?.Nombre ?? Input.AbonoIdComp,
                cli?.NombreCompleto ?? Input.AbonoIdCli,
                Input.AbonoIdComp, Input.AbonoIdCli,
                Input.FechaPago, usuario);

            // Registrar pago de abono (ligado a la factura de crédito)
            var pagoAbono = new Pago
            {
                NumTransaccionFactura = credito.NumTransaccion,
                NumTicket             = credito.NumTicket,
                FechaPago             = Input.FechaPago,
                MontoPagado           = Input.AbonoMonto,
                FormaPago             = formaPago,
                NumChequeReferencia   = Input.NumChequeReferencia,
                Notas                 = $"Abono a cuenta. Recibo: {numRecibo}",
                IdCompania            = Input.AbonoIdComp,
                IdCliente             = Input.AbonoIdCli,
                EmpresaNombre         = comp?.Nombre ?? "",
                ClienteNombre         = cli?.NombreCompleto ?? "",
                NumRecibo             = numRecibo,
                MontoInstrumento      = Input.MontoInstrumento
            };
            await _pagos.RegistrarAsync(pagoAbono);
        }

        TempData["Exito"] = $"✅ Recibo <strong>{numRecibo}</strong> registrado con {pagos.Count} factura(s).";
        return RedirectToPage("/Pagos/Consulta", new { recibo = numRecibo });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task RecargarDatosAsync()
    {
        var todasFact = await _facturas.ObtenerTodasAsync();
        var companias = await _companias.ObtenerTodasAsync(soloActivas: true);
        var clientes  = await _clientes.ObtenerTodosAsync(soloActivos: true);
        var pendientes = todasFact
            .Where(f => f.Estado != EstadoFactura.Pagada && f.Estado != EstadoFactura.Credito && f.SaldoPendiente > 0)
            .OrderBy(f => f.FechaFactura).ToList();
        FacturasPendientesJson = JsonSerializer.Serialize(pendientes.Select(f => new FacturaDto(
            f.NumTransaccion, f.NumTicket, f.EmpresaNombre, f.ClienteNombre,
            f.IdCompania, f.IdCliente, f.Monto, f.PagadoALaFecha, f.SaldoPendiente,
            f.Estado.ToString(), f.FechaFactura.ToString("dd/MM/yyyy"))));
        CompaniasJson = JsonSerializer.Serialize(companias.Select(c => new { c.Id, c.Nombre }));
        ClientesJson  = JsonSerializer.Serialize(clientes.Select(c => new { c.Id, Nombre = c.NombreCompleto }));
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }

    // DTO para deserializar el DetalleJson desde el formulario
    private class DetalleItem
    {
        public string  NumTransaccion { get; set; } = string.Empty;
        public int     NumTicket      { get; set; }
        public decimal MontoAplicado  { get; set; }
        public string  EmpresaNombre  { get; set; } = string.Empty;
        public string  ClienteNombre  { get; set; } = string.Empty;
        public string  IdCompania     { get; set; } = string.Empty;
        public string  IdCliente      { get; set; } = string.Empty;
    }
}
