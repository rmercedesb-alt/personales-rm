using ADEnterprise.Models;
using ADEnterprise.Helpers;
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
    private readonly CajaService     _caja;

    public RegistrarModel(FacturaService f, PagoService p, ClienteService cl, CompaniaService co, CajaService caja)
    {
        _facturas  = f;
        _pagos     = p;
        _clientes  = cl;
        _companias = co;
        _caja      = caja;
    }

    // ── Datos para la UI ─────────────────────────────────────────────────────
    public string FacturasPendientesJson { get; set; } = "[]";
    public string FormToken              { get; set; } = "";
    public string CompaniasJson          { get; set; } = "[]";
    public string ClientesJson           { get; set; } = "[]";

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

        // JSON con los ítems del recibo (facturas + abonos)
        // Cada item: { tipo, numTransaccion, numTicket, montoAplicado,
        //              empresaNombre, clienteNombre, idCompania, idCliente, ... }
        public string DetalleJson { get; set; } = "[]";
    }

    // ── DTO para serialización JSON de facturas ───────────────────────────────
    private record FacturaDto(
        string NumTransaccion, int NumTicket, string EmpresaNombre, string ClienteNombre,
        string IdCompania, string IdCliente, decimal Monto, decimal PagadoALaFecha,
        decimal SaldoPendiente, string Estado, string FechaFactura);

    // ── GET ──────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnGetAsync(int ticket = 0)
    {
        SetViewData();
        if (await _caja.GetCajaAbiertaAsync() == null)
        {
            TempData["Error"] = "⚠️ No hay un Cuadre de Caja abierto. Debe abrir la caja antes de registrar cobros.";
            return RedirectToPage("/Finanzas/Caja");
        }
        Input.FechaPago = DateTime.Today;

        var todasFact   = await _facturas.ObtenerTodasAsync();
        var companias   = await _companias.ObtenerTodasAsync(soloActivas: true);
        var clientes    = await _clientes.ObtenerTodosAsync(soloActivos: true);

        var pendientes = todasFact
            .Where(f => f.Estado != EstadoFactura.Pagada && f.Estado != EstadoFactura.Credito
                        && f.SaldoPendiente > 0)
            .OrderBy(f => f.FechaFactura)
            .ToList();

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        FacturasPendientesJson = JsonSerializer.Serialize(pendientes.Select(f => new FacturaDto(
            f.NumTransaccion, f.NumTicket, f.EmpresaNombre, f.ClienteNombre,
            f.IdCompania, f.IdCliente, f.Monto, f.PagadoALaFecha, f.SaldoPendiente,
            f.Estado.ToString(), f.FechaFactura.ToString("dd/MM/yyyy")
        )), jsonOpts);

        CompaniasJson = JsonSerializer.Serialize(companias.Select(c => new { c.Id, c.Nombre }), jsonOpts);
        ClientesJson  = JsonSerializer.Serialize(clientes.Select(c => new { c.Id, Nombre = c.NombreCompleto }), jsonOpts);

        // Pre-cargar si viene con ticket pre-seleccionado
        if (ticket > 0)
        {
            var fac = pendientes.FirstOrDefault(f => f.NumTicket == ticket);
            if (fac != null)
            {
                Input.MontoInstrumento = fac.SaldoPendiente;
                var det = new[]{ new {
                    tipo           = "factura",
                    numTransaccion = fac.NumTransaccion,
                    numTicket      = fac.NumTicket,
                    montoAplicado  = fac.SaldoPendiente,
                    saldoOriginal  = fac.SaldoPendiente,
                    empresaNombre  = fac.EmpresaNombre,
                    clienteNombre  = fac.ClienteNombre,
                    idCompania     = fac.IdCompania,
                    idCliente      = fac.IdCliente,
                    _uid           = 1
                }};
                Input.DetalleJson = JsonSerializer.Serialize(det, jsonOpts);
            }
        }
        FormToken = FormTokenHelper.Generar(TempData, "Token_PagosRegistrar");
        return Page();
    }

    // ── POST ─────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostAsync()
    {
        SetViewData();
        if (await _caja.GetCajaAbiertaAsync() == null)
        {
            TempData["Error"] = "⚠️ No hay un Cuadre de Caja abierto. La operación no puede procesarse.";
            return RedirectToPage("/Finanzas/Caja");
        }

        // Validar token de envío único
        var tokenEnviado = Request.Form["FormToken"].ToString();
        if (!FormTokenHelper.Validar(TempData, "Token_PagosRegistrar", tokenEnviado))
        {
            TempData["Advertencia"] = "⚠️ Esta transacción ya fue procesada o el formulario expiró. Verifique los cobros antes de registrar nuevamente.";
            return RedirectToPage("/Pagos/Consulta");
        }

        var forma = Input.FormaPago;
        if ((forma == "Cheque" || forma == "Transferencia" || forma == "Deposito")
            && string.IsNullOrWhiteSpace(Input.NumChequeReferencia))
        {
            ModelState.AddModelError("Input.NumChequeReferencia",
                "El N° de cheque / referencia es obligatorio para esta forma de pago.");
        }

        if (!ModelState.IsValid)
        {
            FormToken = FormTokenHelper.Generar(TempData, "Token_PagosRegistrar");
            await RecargarDatosAsync();
            return Page();
        }

        // Deserializar items del detalle
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

        if (detalle.Count == 0)
        {
            FormToken = FormTokenHelper.Generar(TempData, "Token_PagosRegistrar");
            ModelState.AddModelError("", "Debe agregar al menos una factura o un abono a cuenta.");
            await RecargarDatosAsync();
            return Page();
        }

        var usuario   = User.FindFirst("NombreCompleto")?.Value ?? User.Identity?.Name ?? "Sistema";
        var numRecibo = await _pagos.SiguienteNumReciboAsync();
        var formaPago = Enum.TryParse<FormaPago>(Input.FormaPago, out var fp) ? fp : FormaPago.Cheque;

        // Separar facturas y abonos
        var itemsFactura = detalle.Where(d => string.Equals(d.Tipo, "factura", StringComparison.OrdinalIgnoreCase)).ToList();
        var itemsAbono   = detalle.Where(d => string.Equals(d.Tipo, "abono",   StringComparison.OrdinalIgnoreCase)).ToList();

        // Construir pagos de facturas
        var pagos = itemsFactura.Select(d => new Pago
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

        // Registrar el recibo con facturas
        await _pagos.RegistrarReciboAsync(numRecibo, Input.MontoInstrumento, pagos, usuario);

        // Procesar cada abono a cuenta
        int countAbonos = 0;
        foreach (var abono in itemsAbono)
        {
            if (abono.MontoAplicado <= 0) continue;
            if (string.IsNullOrEmpty(abono.IdCompania) || string.IsNullOrEmpty(abono.IdCliente)) continue;

            // Crear factura de crédito
            var credito = await _facturas.CrearCreditoAsync(
                abono.MontoAplicado,
                abono.NumTicket,  // ticket ingresado por el usuario
                abono.EmpresaNombre,
                abono.ClienteNombre,
                abono.IdCompania, abono.IdCliente,
                Input.FechaPago, usuario);

            // Registrar pago del abono
            var pagoAbono = new Pago
            {
                NumTransaccionFactura = credito.NumTransaccion,
                NumTicket             = credito.NumTicket,
                FechaPago             = Input.FechaPago,
                MontoPagado           = abono.MontoAplicado,
                FormaPago             = formaPago,
                NumChequeReferencia   = Input.NumChequeReferencia,
                Notas                 = $"Abono a cuenta. Recibo: {numRecibo}. {Input.Notas}".Trim('.', ' '),
                IdCompania            = abono.IdCompania,
                IdCliente             = abono.IdCliente,
                EmpresaNombre         = abono.EmpresaNombre,
                ClienteNombre         = abono.ClienteNombre,
                NumRecibo             = numRecibo,
                MontoInstrumento      = Input.MontoInstrumento
            };
            await _pagos.RegistrarAsync(pagoAbono);
            countAbonos++;
        }

        var msgExtra = countAbonos > 0 ? $" + {countAbonos} abono(s) a cuenta." : ".";
        TempData["Exito"] = $"✅ Recibo <strong>{numRecibo}</strong> registrado con {pagos.Count} factura(s){msgExtra}";
        // Redirigir directo a impresión del recibo
        return RedirectToPage("/Pagos/ImprimirRecibo", new { num = numRecibo });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task RecargarDatosAsync()
    {
        var jsonOpts  = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var todasFact = await _facturas.ObtenerTodasAsync();
        var companias = await _companias.ObtenerTodasAsync(soloActivas: true);
        var clientes  = await _clientes.ObtenerTodosAsync(soloActivos: true);
        var pendientes = todasFact
            .Where(f => f.Estado != EstadoFactura.Pagada && f.Estado != EstadoFactura.Credito && f.SaldoPendiente > 0)
            .OrderBy(f => f.FechaFactura).ToList();
        FacturasPendientesJson = JsonSerializer.Serialize(pendientes.Select(f => new FacturaDto(
            f.NumTransaccion, f.NumTicket, f.EmpresaNombre, f.ClienteNombre,
            f.IdCompania, f.IdCliente, f.Monto, f.PagadoALaFecha, f.SaldoPendiente,
            f.Estado.ToString(), f.FechaFactura.ToString("dd/MM/yyyy"))), jsonOpts);
        CompaniasJson = JsonSerializer.Serialize(companias.Select(c => new { c.Id, c.Nombre }), jsonOpts);
        ClientesJson  = JsonSerializer.Serialize(clientes.Select(c => new { c.Id, Nombre = c.NombreCompleto }), jsonOpts);
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }

    // DTO para deserializar el DetalleJson desde el formulario
    private class DetalleItem
    {
        public string  Tipo           { get; set; } = "factura";
        public string  NumTransaccion { get; set; } = string.Empty;
        public int     NumTicket      { get; set; }
        public decimal MontoAplicado  { get; set; }
        public string  EmpresaNombre  { get; set; } = string.Empty;
        public string  ClienteNombre  { get; set; } = string.Empty;
        public string  IdCompania     { get; set; } = string.Empty;
        public string  IdCliente      { get; set; } = string.Empty;
    }
}
