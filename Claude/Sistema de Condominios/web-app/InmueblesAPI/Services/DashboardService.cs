using InmueblesAPI.Models;

namespace InmueblesAPI.Services;

public class DashboardService
{
    private readonly GoogleSheetsService _sheets;

    public DashboardService(GoogleSheetsService sheets) => _sheets = sheets;

    public async Task<DashboardKPIs> GetKPIsAsync()
    {
        var now = DateTime.Now;
        var iniciomes = new DateTime(now.Year, now.Month, 1);

        var cxcs = await _sheets.GetCxCCargosAsync();
        var pagos = await _sheets.GetPagosAsync();
        var inmuebles = await _sheets.GetInmueblesAsync();
        var contratos = await _sheets.GetContratosAsync(soloVigentes: true);

        // KPIs financieros
        var cxcMes = cxcs.Where(c => c.FechaVencimiento >= iniciomes && c.FechaVencimiento < iniciomes.AddMonths(1))
                         .Sum(c => c.MontoOriginal);
        var cobradoMes = pagos.Where(p => p.FechaPago >= iniciomes).Sum(p => p.TotalPagado);
        var enAdmin = pagos.Where(p => p.EstadoCustodia == "EN_ADMINISTRACION").Sum(p => p.TotalPagado);
        var liquidadoMes = pagos.Where(p => p.EstadoCustodia == "LIQUIDADO" && p.FechaPago >= iniciomes).Sum(p => p.TotalPagado);
        var pendiente = cxcs.Where(c => c.Estado == "PENDIENTE" || c.Estado == "EN_MORA").Sum(c => c.SaldoPendiente);
        var tasa = cxcMes > 0 ? Math.Round(cobradoMes / cxcMes * 100, 1) : 0;

        // Inmuebles
        var ocupados = inmuebles.Count(i => i.Estado == "OCUPADO");
        var total = inmuebles.Count;

        // Sesion hoy
        var sesion = await _sheets.GetSesionHoyAsync();
        var sesionHoy = sesion?.TotalGeneral ?? 0;

        // Contratos por vencer (próximos 30 días)
        var porVencer = contratos.Count(c => c.FechaFin <= now.AddDays(30) && c.FechaFin >= now);
        var enMora = cxcs.Count(c => c.Estado == "EN_MORA");

        // Alertas
        var alertas = new List<AlertaDashboard>();
        if (enMora > 0) alertas.Add(new AlertaDashboard("MORA", $"{enMora} contratos en mora", null));
        if (porVencer > 0) alertas.Add(new AlertaDashboard("VENCIMIENTO", $"{porVencer} contratos vencen en 30 días", null));
        if (sesion?.Estado == "ABIERTA" && now.Hour >= 20)
            alertas.Add(new AlertaDashboard("CAJA", "Sesión de caja abierta después de las 8 PM", sesion?.Id));

        // Art. 1762 — inquilinos con 2+ meses impagos consecutivos
        var art1762 = DetectarArt1762(cxcs);
        if (art1762.Count > 0)
            alertas.Add(new AlertaDashboard("ART1762", $"{art1762.Count} inquilinos con 2+ meses sin pagar (Art. 1762)", null));

        return new DashboardKPIs(cxcMes, cobradoMes, enAdmin, liquidadoMes, pendiente,
            tasa, ocupados, total, porVencer, enMora, sesionHoy, alertas);
    }

    private static List<string> DetectarArt1762(List<CxCCargo> cxcs)
    {
        var porInquilino = cxcs
            .Where(c => c.TipoCargo == "ALQUILER" && (c.Estado == "PENDIENTE" || c.Estado == "EN_MORA"))
            .GroupBy(c => c.IdInquilino)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToList();
        return porInquilino;
    }
}
