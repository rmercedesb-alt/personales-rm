using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Historicos;

public class EgresosModel : PageModel
{
    private readonly HistoricoService _svc;
    public EgresosModel(HistoricoService svc) => _svc = svc;

    public List<string>         Encabezados { get; set; } = new();
    public IList<IList<object>> Filas       { get; set; } = new List<IList<object>>();
    public bool                 Existe      { get; set; }

    public async Task OnGetAsync()
    {
        Existe = await _svc.ExisteAsync(HistoricoService.HOJA_EGRESOS_H);
        if (Existe)
            (Encabezados, Filas) = await _svc.LeerAsync(HistoricoService.HOJA_EGRESOS_H);
    }
}
