using InmueblesAPI.Models;

namespace InmueblesAPI.Services;

public class DashboardService
{
    private readonly InmueblesService _svc;

    public DashboardService(InmueblesService sheets) => _svc = sheets;

    public async Task<DashboardKPIs> GetKPIsAsync()
    {
        var now = DateTime.Now;
        var iniciomes = new DateTime(now.Year, now.Month, 1);

        var cxcs = await _svc.GetCxCCargosAsync();
        var pagos = await _svc.GetPagosAsync();
        var inmuebles = await _svc.GetInmueblesAsync();
        var contratos = await _svc.GetContratosAsync(soloVigentes: true);

        // KPIs financieros
        var cxcMes = cxcs.Where(c => c.FechaVencimiento >= iniciomes && c.FechaVencimiento < iniciomes.AddMonths(1))
                         .Sum(c => c.MontoOriginal);
        var cobradoMes = pagos.Where(p => p.FechaPago >= iniciomes).Sum(p => p.TotalPagado);
        var pagosAdmin = pagos.Where(p => p.EstadoCustodia == "EN_ADMINISTRACION" && !p.Anulado).ToList();
        var enAdmin = pagosAdmin.Sum(p => p.TotalPagado);
        // Conteo de propietarios y pagos pendientes de liquidar
        var contratosTodos = await _svc.GetContratosAsync();
        var contratoProp = contratosTodos.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().IdPropietario);
        var enAdminPagos = pagosAdmin.Count;
        var enAdminProps = pagosAdmin
            .Where(p => !string.IsNullOrEmpty(p.IdContrato) && contratoProp.ContainsKey(p.IdContrato))
            .Select(p => contratoProp[p.IdContrato])
            .Where(idp => !string.IsNullOrEmpty(idp))
            .Distinct().Count();
        var liquidadoMes = pagos.Where(p => p.EstadoCustodia == "LIQUIDADO" && p.FechaPago >= iniciomes).Sum(p => p.TotalPagado);
        var pendiente = cxcs.Where(c => c.Estado == "PENDIENTE" || c.Estado == "EN_MORA").Sum(c => c.SaldoPendiente);
        var tasa = cxcMes > 0 ? Math.Round(cobradoMes / cxcMes * 100, 1) : 0;

        // Inmuebles
        var ocupados = inmuebles.Count(i => i.Estado == "OCUPADO");
        var total = inmuebles.Count;

        // Sesion hoy
        var sesion = await _svc.GetSesionHoyAsync();
        var sesionHoy = sesion?.TotalGeneral ?? 0;

        // Contratos por vencer (próximos 30 días)
        var porVencer = contratos.Count(c => c.FechaFin <= now.AddDays(30) && c.FechaFin >= now);
        var cxcsMora = cxcs.Where(c => c.Estado == "EN_MORA").ToList();
        var enMora = cxcsMora.Count;
        var montoEnMora = cxcsMora.Sum(c => c.SaldoPendiente);

        // Alertas
        var alertas = new List<AlertaDashboard>();
        if (enMora > 0) alertas.Add(new AlertaDashboard("MORA", $"{enMora} contratos en mora", null));
        if (porVencer > 0) alertas.Add(new AlertaDashboard("VENCIMIENTO", $"{porVencer} contratos vencen en 30 días", null));
        if (sesion?.Estado == "ABIERTA" && now.Hour >= 20)
            alertas.Add(new AlertaDashboard("CAJA", "Sesión de caja abierta después de las 8 PM", sesion?.Id));

        // Art. 1762 — inquilinos con 2+ meses impagos consecutivos
        var art1762Ids = DetectarArt1762Ids(cxcs);
        var montoArt1762 = cxcs
            .Where(c => art1762Ids.Contains(c.IdInquilino) &&
                        c.TipoCargo == "ALQUILER" &&
                        (c.Estado == "PENDIENTE" || c.Estado == "EN_MORA"))
            .Sum(c => c.SaldoPendiente);
        if (art1762Ids.Count > 0)
            alertas.Add(new AlertaDashboard("ART1762", $"{art1762Ids.Count} inquilinos con 2+ meses sin pagar (Art. 1762)", null));

        return new DashboardKPIs(cxcMes, cobradoMes, enAdmin, liquidadoMes, pendiente,
            tasa, ocupados, total, porVencer, enMora, sesionHoy, alertas,
            montoEnMora, art1762Ids.Count, montoArt1762, enAdminProps, enAdminPagos);
    }

    // ─── Detalle mora ──────────────────────────────────────────────────────────

    public async Task<List<MoraDetalleItem>> GetMoraDetalleAsync()
    {
        var cxcs = await _svc.GetCxCCargosAsync();
        return await BuildDetalle(cxcs.Where(c => c.Estado == "EN_MORA").ToList());
    }

    public async Task<List<MoraDetalleItem>> GetArt1762DetalleAsync()
    {
        var cxcs = await _svc.GetCxCCargosAsync();
        var art1762Ids = DetectarArt1762Ids(cxcs);
        var cxcsFiltradas = cxcs
            .Where(c => art1762Ids.Contains(c.IdInquilino) &&
                        c.TipoCargo == "ALQUILER" &&
                        (c.Estado == "PENDIENTE" || c.Estado == "EN_MORA"))
            .ToList();
        return await BuildDetalle(cxcsFiltradas);
    }

    private async Task<List<MoraDetalleItem>> BuildDetalle(List<CxCCargo> cxcs)
    {
        if (cxcs.Count == 0) return [];

        var contratos   = await _svc.GetContratosAsync();
        var propietarios = await _svc.GetPropietariosAsync();

        var contratoMap   = contratos.ToDictionary(c => c.Id);
        var propMap       = propietarios.ToDictionary(p => p.Id, p => p.Nombre);
        var hoy           = DateTime.Today;

        return cxcs.Select(c =>
        {
            var contrato        = contratoMap.GetValueOrDefault(c.IdContrato);
            var idProp          = contrato?.IdPropietario ?? "";
            var nombreProp      = propMap.GetValueOrDefault(idProp, "Sin propietario");
            var diasVencido     = Math.Max(0, (hoy - c.FechaVencimiento.Date).Days);
            var mesesVencidos   = diasVencido > 0 ? (int)Math.Ceiling(diasVencido / 30.0) : 0;
            var montoDeuda      = Math.Max(0, c.MontoOriginal - c.MontoPagado);
            return new MoraDetalleItem(
                c.Id, c.IdContrato, idProp, nombreProp,
                c.IdInquilino, c.NombreInquilino,
                c.Descripcion, c.FechaVencimiento,
                diasVencido, mesesVencidos,
                montoDeuda, c.MontoMora, c.SaldoPendiente
            );
        })
        .OrderBy(x => x.NombrePropietario)
        .ThenBy(x => x.NombreInquilino)
        .ThenBy(x => x.FechaVencimiento)
        .ToList();
    }

    private static HashSet<string> DetectarArt1762Ids(List<CxCCargo> cxcs)
    {
        return cxcs
            .Where(c => c.TipoCargo == "ALQUILER" && (c.Estado == "PENDIENTE" || c.Estado == "EN_MORA"))
            .GroupBy(c => c.IdInquilino)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToHashSet();
    }
}
