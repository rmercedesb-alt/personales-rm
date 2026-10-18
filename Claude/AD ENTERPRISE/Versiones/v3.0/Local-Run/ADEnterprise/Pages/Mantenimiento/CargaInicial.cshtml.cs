using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Mantenimiento;

public class CargaInicialModel : PageModel
{
    private readonly GoogleSheetsService _gs;

    public CargaInicialModel(GoogleSheetsService gs) => _gs = gs;

    // ── Propiedades vinculadas a cada tabla ──────────────────────────────────

    [BindProperty] public string? DatosCompanias       { get; set; }
    [BindProperty] public string? DatosClientes        { get; set; }
    [BindProperty] public string? DatosFacturas        { get; set; }
    [BindProperty] public string? DatosPagos           { get; set; }
    [BindProperty] public string? DatosEgresos         { get; set; }
    [BindProperty] public string? DatosIngresosCapital { get; set; }
    [BindProperty] public string? TablaActiva          { get; set; }

    public string? Resultado { get; set; }
    public bool    Exito     { get; set; }

    private string Usuario => User.FindFirst("NombreCompleto")?.Value ?? User.Identity?.Name ?? "Sistema";

    public IActionResult OnGet() => Page();

    // ── Compañías ────────────────────────────────────────────────────────────
    // Columnas esperadas: Nombre;RNC;Telefono;Direccion;Correo;CondicionPagoDias
    public async Task<IActionResult> OnPostCompaniasAsync()
    {
        var lineas = ParsearLineas(DatosCompanias);
        if (lineas.Count == 0) { TempData["Error"] = "No hay datos para cargar."; return RedirectToPage(); }

        int seq = 1;
        var filas = new List<IList<object>>();
        foreach (var cols in lineas)
        {
            filas.Add(new List<object>
            {
                $"EMP-{seq:D4}",           // ID
                Get(cols,0),               // Nombre
                Get(cols,1),               // RNC
                Get(cols,2),               // Telefono
                Get(cols,3),               // Direccion
                Get(cols,4),               // Correo
                Get(cols,5,"30"),           // CondicionPagoDias
                "TRUE"                     // Activo
            });
            seq++;
        }
        await _gs.AgregarMultiplesFilasAsync(GoogleSheetsService.HOJA_COMPANIAS, filas);
        TempData["Exito"] = $"✅ {filas.Count} compañía(s) cargadas correctamente.";
        return RedirectToPage();
    }

    // ── Clientes ─────────────────────────────────────────────────────────────
    // Columnas: NombreCompleto;Cedula;Telefono;Correo
    public async Task<IActionResult> OnPostClientesAsync()
    {
        var lineas = ParsearLineas(DatosClientes);
        if (lineas.Count == 0) { TempData["Error"] = "No hay datos para cargar."; return RedirectToPage(); }

        int seq = 1;
        var filas = new List<IList<object>>();
        foreach (var cols in lineas)
        {
            filas.Add(new List<object>
            {
                $"CLI-{seq:D4}",
                Get(cols,0),  // NombreCompleto
                Get(cols,1),  // Cedula
                Get(cols,2),  // Telefono
                Get(cols,3),  // Correo
                "TRUE"
            });
            seq++;
        }
        await _gs.AgregarMultiplesFilasAsync(GoogleSheetsService.HOJA_CLIENTES, filas);
        TempData["Exito"] = $"✅ {filas.Count} cliente(s) cargados correctamente.";
        return RedirectToPage();
    }

    // ── Facturas ─────────────────────────────────────────────────────────────
    // Columnas: NumTicket;EmpresaNombre;ClienteNombre;Monto;FechaFactura;Estado;PagadoALaFecha;Notas;IdCompania;IdCliente
    public async Task<IActionResult> OnPostFacturasAsync()
    {
        var lineas = ParsearLineas(DatosFacturas);
        if (lineas.Count == 0) { TempData["Error"] = "No hay datos para cargar."; return RedirectToPage(); }

        int seq = 1;
        var filas = new List<IList<object>>();
        foreach (var cols in lineas)
        {
            filas.Add(new List<object>
            {
                $"TXN-{seq:D6}",          // NumTransaccion
                Get(cols,0),              // NumTicket
                Get(cols,1),              // EmpresaNombre
                Get(cols,2),              // ClienteNombre
                Get(cols,3),              // Monto
                Get(cols,4),              // FechaFactura
                Get(cols,5,"Pendiente"),   // Estado
                Get(cols,6,"0"),           // PagadoALaFecha
                Get(cols,7),              // Notas
                "",                       // ValorAnterior
                "CargaInicial",           // Origen
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), // FechaMigracion
                Usuario,                  // UsuarioMigracion
                "",                       // FechaModificacion
                "",                       // UsuarioModificacion
                Get(cols,8),              // IdCompania
                Get(cols,9)               // IdCliente
            });
            seq++;
        }
        await _gs.AgregarMultiplesFilasAsync(FacturaService.HOJA_FACTURAS, filas);
        TempData["Exito"] = $"✅ {filas.Count} factura(s) cargadas correctamente.";
        return RedirectToPage();
    }

    // ── Pagos ────────────────────────────────────────────────────────────────
    // Columnas: NumTransaccionFactura;NumTicket;FechaPago;MontoPagado;FormaPago;NumChequeReferencia;Notas;IdCompania;IdCliente;NumRecibo;MontoInstrumento;EmpresaNombre;ClienteNombre
    // Lógica especial: filas con el mismo NumChequeReferencia comparten el mismo NumTransaccionPago
    public async Task<IActionResult> OnPostPagosAsync()
    {
        var lineas = ParsearLineas(DatosPagos);
        if (lineas.Count == 0) { TempData["Error"] = "No hay datos para cargar."; return RedirectToPage(); }

        // Agrupar por NumChequeReferencia para asignar NumTransaccionPago
        // Cheques vacíos o "EFECTIVO" reciben número único por fila
        var chequeSeq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int seqActual = 1;
        foreach (var cols in lineas)
        {
            var cheque = Get(cols, 5).Trim();
            if (!string.IsNullOrWhiteSpace(cheque) && cheque.ToUpper() != "EFECTIVO")
            {
                if (!chequeSeq.ContainsKey(cheque))
                    chequeSeq[cheque] = seqActual++;
            }
        }

        var filas = new List<IList<object>>();
        foreach (var cols in lineas)
        {
            var cheque = Get(cols, 5).Trim();
            int numTxn;
            if (!string.IsNullOrWhiteSpace(cheque) && cheque.ToUpper() != "EFECTIVO" && chequeSeq.ContainsKey(cheque))
                numTxn = chequeSeq[cheque];
            else
                numTxn = seqActual++;

            filas.Add(new List<object>
            {
                $"PAG-{numTxn:D6}",       // NumTransaccionPago
                Get(cols,0),              // NumTransaccionFactura
                Get(cols,1),              // NumTicket
                Get(cols,2),              // FechaPago
                Get(cols,3),              // MontoPagado
                Get(cols,4),              // FormaPago
                Get(cols,5),              // NumChequeReferencia
                Get(cols,6),              // Notas
                Usuario,                  // UsuarioRegistro
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), // FechaRegistro
                "",                       // FechaModificacion
                "",                       // UsuarioModificacion
                Get(cols,7),              // IdCompania
                Get(cols,8),              // IdCliente
                Get(cols,9),              // NumRecibo
                Get(cols,10),             // MontoInstrumento
                Get(cols,11),             // EmpresaNombre
                Get(cols,12)              // ClienteNombre
            });
        }
        await _gs.AgregarMultiplesFilasAsync(GoogleSheetsService.HOJA_PAGOS, filas);
        TempData["Exito"] = $"✅ {filas.Count} pago(s) cargados correctamente. Cheques agrupados: {chequeSeq.Count}.";
        return RedirectToPage();
    }

    // ── Egresos ──────────────────────────────────────────────────────────────
    // Columnas: TipoEgreso;ANombreDe;Fecha;Monto;Nota
    public async Task<IActionResult> OnPostEgresosAsync()
    {
        var lineas = ParsearLineas(DatosEgresos);
        if (lineas.Count == 0) { TempData["Error"] = "No hay datos para cargar."; return RedirectToPage(); }

        int seq = 1;
        var filas = new List<IList<object>>();
        foreach (var cols in lineas)
        {
            filas.Add(new List<object>
            {
                $"EGR-{seq:D6}",   // SeqEgreso
                "",                // SeqCuadre (sin cuadre en carga inicial)
                Get(cols,0),       // TipoEgreso
                Get(cols,1),       // ANombreDe
                Get(cols,2),       // Fecha
                Get(cols,3),       // Monto
                Get(cols,4),       // Nota
                Usuario,           // UsuarioRegistro
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), // FechaCreacion
                ""                 // SeqMovCaja
            });
            seq++;
        }
        await _gs.AgregarMultiplesFilasAsync(EgresosService.HOJA, filas);
        TempData["Exito"] = $"✅ {filas.Count} egreso(s) cargados correctamente.";
        return RedirectToPage();
    }

    // ── Ingresos de Capital ───────────────────────────────────────────────────
    // Columnas: Nombre;Fecha;Monto;Observaciones
    public async Task<IActionResult> OnPostIngresosCapitalAsync()
    {
        var lineas = ParsearLineas(DatosIngresosCapital);
        if (lineas.Count == 0) { TempData["Error"] = "No hay datos para cargar."; return RedirectToPage(); }

        int seq = 1;
        var filas = new List<IList<object>>();
        foreach (var cols in lineas)
        {
            filas.Add(new List<object>
            {
                $"ING-{seq:D6}",   // Secuencia
                "",                // SeqCuadre
                Get(cols,0),       // Nombre
                Get(cols,1),       // Fecha
                Get(cols,2),       // Monto
                Get(cols,3),       // Observaciones
                Usuario,           // UsuarioRegistro
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), // FechaCreacion
                ""                 // SeqMovCaja
            });
            seq++;
        }
        await _gs.AgregarMultiplesFilasAsync(IngresosCapitalService.HOJA, filas);
        TempData["Exito"] = $"✅ {filas.Count} ingreso(s) de capital cargados correctamente.";
        return RedirectToPage();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<string[]> ParsearLineas(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return new();

        return texto
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
            .Select(l => l.Split(';'))
            .ToList();
    }

    private static string Get(string[] cols, int idx, string def = "")
        => cols.Length > idx ? cols[idx].Trim() : def;
}
