using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Finanzas;

public class CuadreCajaReporteModel : PageModel
{
    private readonly CajaService _caja;

    public CuadreCajaReporteModel(CajaService caja) { _caja = caja; }

    // ── Filtros ───────────────────────────────────────────────────────────────
    [BindProperty(SupportsGet = true)] public DateTime? Desde     { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Hasta     { get; set; }
    [BindProperty(SupportsGet = true)] public string?   SeqFiltro { get; set; }

    // ── Datos ─────────────────────────────────────────────────────────────────
    public List<ResumenCuadre> Cuadres { get; set; } = new();
    public bool                Buscado { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!Desde.HasValue && !Hasta.HasValue && string.IsNullOrEmpty(SeqFiltro))
        {
            // Por defecto: mostrar el día de hoy
            Desde = DateTime.Today;
            Hasta = DateTime.Today;
        }
        Buscado = true;

        var historial = await _caja.GetHistorialCuadresAsync();

        // Filtrar cuadres
        var filtrados = historial.AsEnumerable();
        if (!string.IsNullOrEmpty(SeqFiltro))
            filtrados = filtrados.Where(c =>
                c.SeqCuadre.Contains(SeqFiltro, StringComparison.OrdinalIgnoreCase));
        if (Desde.HasValue)
            filtrados = filtrados.Where(c => c.FechaApertura.Date >= Desde.Value.Date);
        if (Hasta.HasValue)
            filtrados = filtrados.Where(c => c.FechaApertura.Date <= Hasta.Value.Date);

        var cuadresFiltrados = filtrados.OrderByDescending(c => c.FechaApertura).ToList();

        foreach (var cuadre in cuadresFiltrados)
        {
            var movs = await _caja.GetMovimientosDelDiaAsync(cuadre.SeqCuadre);

            var porCategoria = movs
                .GroupBy(m => m.Categoria)
                .Select(g => new LineaCategoria
                {
                    Categoria   = g.Key,
                    Creditos    = g.Where(m => m.TipoMov == "CREDITO").Sum(m => m.Monto),
                    Debitos     = g.Where(m => m.TipoMov == "DEBITO" ).Sum(m => m.Monto),
                    Cantidad    = g.Count()
                })
                .OrderBy(l => l.Categoria)
                .ToList();

            decimal totalCred = movs.Where(m => m.TipoMov == "CREDITO").Sum(m => m.Monto);
            decimal totalDeb  = movs.Where(m => m.TipoMov == "DEBITO" ).Sum(m => m.Monto);

            // Calcular saldo acumulado por transacción, partiendo del BalanceInicial del cuadre
            decimal saldoAcum = cuadre.BalanceInicial;
            var movsConSaldo = movs
                .OrderBy(m => m.FechaCreacion)
                .ThenBy(m => m.SeqMovCaja)
                .Select(m =>
                {
                    if (m.TipoMov == "CREDITO") saldoAcum += m.Monto;
                    else                         saldoAcum -= m.Monto;
                    return new MovimientoConSaldo(m, saldoAcum);
                })
                .ToList();

            Cuadres.Add(new ResumenCuadre
            {
                Cuadre              = cuadre,
                Movimientos         = movs,
                PorCategoria        = porCategoria,
                MovimientosConSaldo = movsConSaldo,
                TotalCreditos       = totalCred,
                TotalDebitos        = totalDeb,
                BalanceDisponible   = cuadre.BalanceInicial + totalCred - totalDeb
            });
        }

        return Page();
    }

    // ── Helpers de etiquetas ──────────────────────────────────────────────────

    public static string LabelCategoria(string cat) => cat switch
    {
        "PAGO_FACTORING"     => "Pagos Factoring (adelantos a clientes)",
        "COMISION_FACTORING" => "Comisiones Factoring (ingresos)",
        "COBRO_CXC"          => "Cobros de Cartera (CxC)",
        "INGRESO_CAPITAL"    => "Ingresos de Capital",
        "EGRESO_NOMINA"      => "Pagos de Nómina",
        "EGRESO_SERVICIO"    => "Pagos de Servicios",
        "EGRESO_OTRO"        => "Otros Egresos",
        // compatibilidad
        "FACTURA_CXC"        => "Pagos Factoring (adelantos a clientes)",
        _                    => cat
    };

    public static string BadgeEstado(string estado) =>
        estado == "ABIERTA"
            ? "badge bg-success"
            : "badge bg-secondary";
}

// ── DTOs del reporte ──────────────────────────────────────────────────────────

public class ResumenCuadre
{
    public CuadreCaja                  Cuadre               { get; set; } = null!;
    public List<MovimientoCaja>        Movimientos          { get; set; } = new();
    public List<LineaCategoria>        PorCategoria         { get; set; } = new();
    public List<MovimientoConSaldo>    MovimientosConSaldo  { get; set; } = new();
    public decimal                     TotalCreditos        { get; set; }
    public decimal                     TotalDebitos         { get; set; }
    public decimal                     BalanceDisponible    { get; set; }
}

public class LineaCategoria
{
    public string  Categoria { get; set; } = "";
    public decimal Creditos  { get; set; }
    public decimal Debitos   { get; set; }
    public int     Cantidad  { get; set; }
    public decimal Neto      => Creditos - Debitos;
}
