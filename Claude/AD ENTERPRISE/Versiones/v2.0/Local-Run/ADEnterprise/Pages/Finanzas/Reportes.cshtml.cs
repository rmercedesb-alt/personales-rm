using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Finanzas;

public class ReportesModel : PageModel
{
    private readonly CajaService _caja;

    public ReportesModel(CajaService caja) { _caja = caja; }

    // ── Filtros ───────────────────────────────────────────────────────────────
    [BindProperty(SupportsGet = true)] public DateTime? Desde      { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Hasta      { get; set; }
    [BindProperty(SupportsGet = true)] public string    Categoria  { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string    TipoMov    { get; set; } = "";

    // ── Datos ─────────────────────────────────────────────────────────────────
    public List<MovimientoCaja>  Movimientos     { get; set; } = new();
    public List<CuadreCaja>      Cuadres         { get; set; } = new();
    public decimal               TotalCreditos   { get; set; }
    public decimal               TotalDebitos    { get; set; }
    public decimal               BalanceGeneral  { get; set; }

    // Resumen por categoría
    public Dictionary<string, (decimal Credito, decimal Debito)> ResumenCategorias { get; set; } = new();

    public static readonly string[] Categorias = {
        "PAGO_FACTORING", "COMISION_FACTORING", "COBRO_CXC",
        "INGRESO_CAPITAL", "EGRESO_NOMINA", "EGRESO_SERVICIO", "EGRESO_OTRO"
    };

    public async Task<IActionResult> OnGetAsync()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        // Cargar todos los movimientos de todos los cuadres
        Cuadres = await _caja.GetHistorialCuadresAsync();
        BalanceGeneral = await _caja.GetBalanceActualAsync();

        var todos = new List<MovimientoCaja>();
        foreach (var c in Cuadres)
        {
            var movs = await _caja.GetMovimientosDelDiaAsync(c.SeqCuadre);
            todos.AddRange(movs);
        }

        // Filtrar
        var filtrado = todos.AsEnumerable();
        if (Desde.HasValue)   filtrado = filtrado.Where(m => m.FechaMovimiento.Date >= Desde.Value.Date);
        if (Hasta.HasValue)   filtrado = filtrado.Where(m => m.FechaMovimiento.Date <= Hasta.Value.Date);
        if (!string.IsNullOrEmpty(Categoria)) filtrado = filtrado.Where(m => m.Categoria == Categoria);
        if (!string.IsNullOrEmpty(TipoMov))   filtrado = filtrado.Where(m => m.TipoMov == TipoMov);

        Movimientos   = filtrado.OrderByDescending(m => m.FechaMovimiento).ToList();
        TotalCreditos = Movimientos.Where(m => m.TipoMov == "CREDITO").Sum(m => m.Monto);
        TotalDebitos  = Movimientos.Where(m => m.TipoMov == "DEBITO").Sum(m => m.Monto);

        // Resumen por categoría (del conjunto filtrado)
        ResumenCategorias = Movimientos
            .GroupBy(m => m.Categoria)
            .ToDictionary(
                g => g.Key,
                g => (
                    Credito: g.Where(m => m.TipoMov == "CREDITO").Sum(m => m.Monto),
                    Debito:  g.Where(m => m.TipoMov == "DEBITO").Sum(m => m.Monto)
                )
            );

        return Page();
    }

    public static string LabelCategoria(string cat) => cat switch {
        "PAGO_FACTORING"     => "Pagos Factoring",
        "COMISION_FACTORING" => "Comisión Factoring",
        "COBRO_CXC"          => "Cobros CxC",
        "INGRESO_CAPITAL"    => "Ingreso de Capital",
        "EGRESO_NOMINA"      => "Pago de Nómina",
        "EGRESO_SERVICIO"    => "Pago de Servicio",
        "EGRESO_OTRO"        => "Otros Gastos",
        "FACTURA_CXC"        => "Pagos Factoring",   // compatibilidad
        _                    => cat
    };
}
