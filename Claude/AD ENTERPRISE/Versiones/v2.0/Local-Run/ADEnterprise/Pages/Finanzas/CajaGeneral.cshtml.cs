using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Finanzas;

public class CajaGeneralModel : PageModel
{
    private readonly CajaService _caja;

    public CajaGeneralModel(CajaService caja) { _caja = caja; }

    // ── Estado actual ─────────────────────────────────────────────────────────
    public CuadreCaja?           CajaAbierta       { get; set; }
    public decimal               BalanceActual     { get; set; }
    public bool                  CajaDeOtrosDias   { get; set; }

    // ── Movimientos del cuadre seleccionado ───────────────────────────────────
    public List<MovimientoCaja>  Movimientos       { get; set; } = new();
    public List<MovimientoConSaldo> MovimientosConSaldo { get; set; } = new();
    public CuadreCaja?           CuadreSeleccionado { get; set; }

    // ── Totales del cuadre actual ─────────────────────────────────────────────
    public decimal TotalCreditos  { get; set; }
    public decimal TotalDebitos   { get; set; }

    // ── Historial de cuadres (para selector) ─────────────────────────────────
    public List<CuadreCaja>      HistorialCuadres  { get; set; } = new();

    // ── Filtro: cuadre seleccionado ───────────────────────────────────────────
    [BindProperty(SupportsGet = true)]
    public string? SeqCuadreVer { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;

        BalanceActual   = await _caja.GetBalanceActualAsync();
        HistorialCuadres = await _caja.GetHistorialCuadresAsync();

        var (ok, caja, _) = await _caja.ValidarCajaAsync();
        CajaAbierta     = caja;
        CajaDeOtrosDias = !ok && caja != null;

        // Determinar qué cuadre mostrar en el estado de cuenta
        string? seqTarget = SeqCuadreVer;
        if (string.IsNullOrEmpty(seqTarget) && CajaAbierta != null)
            seqTarget = CajaAbierta.SeqCuadre;
        else if (string.IsNullOrEmpty(seqTarget) && HistorialCuadres.Any())
            seqTarget = HistorialCuadres.First().SeqCuadre; // más reciente

        if (!string.IsNullOrEmpty(seqTarget))
        {
            CuadreSeleccionado = HistorialCuadres.FirstOrDefault(c => c.SeqCuadre == seqTarget);
            Movimientos = await _caja.GetMovimientosDelDiaAsync(seqTarget);

            // Calcular saldo acumulado (estado de cuenta), arrancando desde el BalanceInicial
            decimal saldoAcum = CuadreSeleccionado?.BalanceInicial ?? 0;
            MovimientosConSaldo = Movimientos
                .OrderBy(m => m.FechaCreacion)
                .ThenBy(m => m.SeqMovCaja)
                .Select(m =>
                {
                    if (m.TipoMov == "CREDITO") saldoAcum += m.Monto;
                    else                         saldoAcum -= m.Monto;
                    return new MovimientoConSaldo(m, saldoAcum);
                })
                .ToList();

            TotalCreditos = Movimientos.Where(m => m.TipoMov == "CREDITO").Sum(m => m.Monto);
            TotalDebitos  = Movimientos.Where(m => m.TipoMov == "DEBITO" ).Sum(m => m.Monto);
        }

        SeqCuadreVer = seqTarget;
        return Page();
    }
}

/// <summary>
/// Decorador: movimiento + saldo acumulado calculado en el servidor.
/// </summary>
public record MovimientoConSaldo(MovimientoCaja Mov, decimal SaldoAcumulado);
