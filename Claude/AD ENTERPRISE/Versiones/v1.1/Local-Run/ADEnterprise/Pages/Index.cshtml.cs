using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages;

public class IndexModel : PageModel
{
    private readonly FacturaService      _facturas;
    private readonly PagoService         _pagos;
    private readonly GoogleSheetsService _gs;

    public IndexModel(FacturaService facturas, PagoService pagos, GoogleSheetsService gs)
    {
        _facturas = facturas;
        _pagos    = pagos;
        _gs       = gs;
    }

    // ── Filtros ───────────────────────────────────────────────────────────
    [BindProperty(SupportsGet = true)] public DateTime FechaDesde    { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime FechaHasta    { get; set; }
    [BindProperty(SupportsGet = true)] public string   FiltroEmpresa { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string   FiltroCliente { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string   Vista         { get; set; } = "General";

    // ── Combos ────────────────────────────────────────────────────────────
    public List<string> Empresas { get; set; } = new();
    public List<string> Clientes { get; set; } = new();

    // ── Vista General ─────────────────────────────────────────────────────
    public DashboardStats    Stats              { get; set; } = new();
    public List<BalanceItem> BalancePorCompania { get; set; } = new();
    public List<BalanceItem> TopDeudores        { get; set; } = new();
    public List<Pago>        UltimosPagos       { get; set; } = new();

    // ── Vista Por Cliente ─────────────────────────────────────────────────
    public List<ClienteStats> RankingClientes      { get; set; } = new();
    public List<ClienteStats> Top3Deuda            { get; set; } = new();
    public decimal            TotalFacturadoGlobal  { get; set; }
    public decimal            TotalCobradoGlobal    { get; set; }
    public decimal            PromedioMensualCobrado { get; set; }
    public string             DatosMensualesJson    { get; set; } = "[]";

    // ── Error ─────────────────────────────────────────────────────────────
    public string? ErrorConexion { get; set; }

    public async Task OnGetAsync()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value ?? User.Identity?.Name;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (FechaDesde == default) FechaDesde = new DateTime(DateTime.Today.Year, 1, 1);
        if (FechaHasta == default) FechaHasta = DateTime.Today;
        FiltroEmpresa ??= string.Empty;
        FiltroCliente ??= string.Empty;
        Vista         ??= "General";

        List<Factura> todasSinFiltro;
        List<Pago>    todosPagos;

        try
        {
            todasSinFiltro = await _facturas.ObtenerTodasAsync();
            todosPagos     = await _pagos.ObtenerTodosAsync();
            if (_gs.UltimoError != null) ErrorConexion = _gs.UltimoError;
        }
        catch (System.Net.Http.HttpRequestException)
        {
            ErrorConexion = "Sin conexión a internet. Verifique su conexión y recargue la página.";
            return;
        }
        catch (Exception ex)
        {
            ErrorConexion = $"Error al conectar con Google Sheets: {ex.Message}";
            return;
        }

        // Combos — siempre de toda la data
        Empresas = todasSinFiltro.Select(f => f.EmpresaNombre)
            .Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().OrderBy(e => e).ToList();
        Clientes = todasSinFiltro.Select(f => f.ClienteNombre)
            .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();

        // Aplicar filtros de fecha + empresa + cliente
        var filtradas = todasSinFiltro
            .Where(f => f.FechaFactura.Date >= FechaDesde.Date &&
                        f.FechaFactura.Date <= FechaHasta.Date)
            .Where(f => string.IsNullOrEmpty(FiltroEmpresa) ||
                        f.EmpresaNombre.Equals(FiltroEmpresa, StringComparison.OrdinalIgnoreCase))
            .Where(f => string.IsNullOrEmpty(FiltroCliente) ||
                        f.ClienteNombre.Equals(FiltroCliente, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var transaccionesFiltradas = filtradas.Select(f => f.NumTransaccion).ToHashSet();

        var pagosFiltrados = todosPagos
            .Where(p => p.FechaPago.Date >= FechaDesde.Date &&
                        p.FechaPago.Date <= FechaHasta.Date)
            .Where(p => string.IsNullOrEmpty(FiltroEmpresa) ||
                        transaccionesFiltradas.Contains(p.NumTransaccionFactura))
            .ToList();

        // ─────────────────────────────────────────────────────────────────
        // Estadísticas comunes (usadas en ambas vistas)
        // ─────────────────────────────────────────────────────────────────
        Stats = new DashboardStats
        {
            TotalFacturas   = filtradas.Count,
            MontoTotal      = filtradas.Sum(f => f.Monto),
            TotalPendientes = filtradas.Count(f => f.Estado == EstadoFactura.Pendiente),
            MontoPendiente  = filtradas.Where(f => f.Estado == EstadoFactura.Pendiente).Sum(f => f.SaldoPendiente),
            TotalPagadas    = filtradas.Count(f => f.Estado == EstadoFactura.Pagada),
            MontoPagado     = filtradas.Where(f => f.Estado == EstadoFactura.Pagada).Sum(f => f.Monto),
            TotalParciales  = filtradas.Count(f => f.Estado == EstadoFactura.PagadoParcial),
            MontoParcial    = filtradas.Where(f => f.Estado == EstadoFactura.PagadoParcial).Sum(f => f.SaldoPendiente),
            TotalVencidas   = filtradas.Count(f => f.Estado == EstadoFactura.Vencida),
            MontoVencido    = filtradas.Where(f => f.Estado == EstadoFactura.Vencida).Sum(f => f.SaldoPendiente),
            TotalCobrado    = pagosFiltrados.Sum(p => p.MontoPagado)
        };

        // ─────────────────────────────────────────────────────────────────
        // Vista GENERAL
        // ─────────────────────────────────────────────────────────────────
        BalancePorCompania = filtradas
            .Where(f => f.Estado != EstadoFactura.Pagada)
            .GroupBy(f => f.EmpresaNombre)
            .Select(g => new BalanceItem { Nombre = g.Key, Monto = g.Sum(f => f.SaldoPendiente), Cantidad = g.Count() })
            .OrderByDescending(b => b.Monto).ToList();

        TopDeudores = filtradas
            .Where(f => f.Estado != EstadoFactura.Pagada)
            .GroupBy(f => f.ClienteNombre)
            .Select(g => new BalanceItem { Nombre = g.Key, Monto = g.Sum(f => f.SaldoPendiente), Cantidad = g.Count() })
            .OrderByDescending(b => b.Monto).Take(10).ToList();

        UltimosPagos = pagosFiltrados.OrderByDescending(p => p.FechaPago).Take(10).ToList();

        // ─────────────────────────────────────────────────────────────────
        // Vista POR CLIENTE — ranking completo
        // ─────────────────────────────────────────────────────────────────
        TotalFacturadoGlobal = filtradas.Sum(f => f.Monto);

        RankingClientes = filtradas
            .GroupBy(f => f.ClienteNombre)
            .Select(g => new ClienteStats
            {
                Cliente         = g.Key,
                TotalFacturado  = g.Sum(f => f.Monto),
                TotalPagado     = g.Sum(f => f.PagadoALaFecha),
                TotalPendiente  = g.Where(f => f.Estado != EstadoFactura.Pagada).Sum(f => f.SaldoPendiente),
                TotalVencido    = g.Where(f => f.Estado == EstadoFactura.Vencida).Sum(f => f.SaldoPendiente),
                NumFacturas     = g.Count()
            })
            .OrderByDescending(c => c.TotalFacturado)
            .ToList();

        // Top 3 por deuda pendiente
        Top3Deuda = RankingClientes
            .Where(c => c.TotalPendiente > 0)
            .OrderByDescending(c => c.TotalPendiente)
            .Take(3)
            .ToList();

        // ─────────────────────────────────────────────────────────────────
        // Datos mensuales para gráfico de líneas (Chart.js)
        // ─────────────────────────────────────────────────────────────────
        TotalCobradoGlobal = Stats.TotalCobrado;

        var mesInicio = new DateTime(FechaDesde.Year, FechaDesde.Month, 1);
        var mesFin    = new DateTime(FechaHasta.Year, FechaHasta.Month, 1);
        var meses     = new List<DateTime>();
        for (var m = mesInicio; m <= mesFin; m = m.AddMonths(1))
            meses.Add(m);

        var cult = new System.Globalization.CultureInfo("es-DO");
        var datosMensuales = meses.Select(m => new
        {
            label     = m.ToString("MMM yy", cult),
            facturado = filtradas
                            .Where(f => f.FechaFactura.Year == m.Year && f.FechaFactura.Month == m.Month)
                            .Sum(f => f.Monto),
            cobrado   = pagosFiltrados
                            .Where(p => p.FechaPago.Year == m.Year && p.FechaPago.Month == m.Month)
                            .Sum(p => p.MontoPagado)
        }).ToList();

        DatosMensualesJson = System.Text.Json.JsonSerializer.Serialize(datosMensuales);

        int numMeses = Math.Max(1, meses.Count);
        PromedioMensualCobrado = Math.Round(TotalCobradoGlobal / numMeses, 2);
    }
}

// ── Modelos ───────────────────────────────────────────────────────────────

public class DashboardStats
{
    public int     TotalFacturas   { get; set; }
    public decimal MontoTotal      { get; set; }
    public int     TotalPendientes { get; set; }
    public decimal MontoPendiente  { get; set; }
    public int     TotalPagadas    { get; set; }
    public decimal MontoPagado     { get; set; }
    public int     TotalParciales  { get; set; }
    public decimal MontoParcial    { get; set; }
    public int     TotalVencidas   { get; set; }
    public decimal MontoVencido    { get; set; }
    public decimal TotalCobrado    { get; set; }
}

public class BalanceItem
{
    public string  Nombre   { get; set; } = string.Empty;
    public decimal Monto    { get; set; }
    public int     Cantidad { get; set; }
}

public class ClienteStats
{
    public string  Cliente        { get; set; } = string.Empty;
    public decimal TotalFacturado { get; set; }
    public decimal TotalPagado    { get; set; }
    public decimal TotalPendiente { get; set; }
    public decimal TotalVencido   { get; set; }
    public int     NumFacturas    { get; set; }
    public decimal PorcentajePago => TotalFacturado > 0
        ? Math.Round(TotalPagado / TotalFacturado * 100, 1) : 0;
}
