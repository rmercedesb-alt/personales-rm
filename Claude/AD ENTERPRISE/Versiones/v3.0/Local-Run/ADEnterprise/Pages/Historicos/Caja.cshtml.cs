using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Historicos;

public class CajaModel : PageModel
{
    private readonly HistoricoService _svc;
    public CajaModel(HistoricoService svc) => _svc = svc;

    public List<string>         EncabezadosCuadre { get; set; } = new();
    public IList<IList<object>> FilasCuadre       { get; set; } = new List<IList<object>>();
    public List<string>         EncabezadosCaja   { get; set; } = new();
    public IList<IList<object>> FilasCaja         { get; set; } = new List<IList<object>>();
    public bool                 ExisteCuadre      { get; set; }
    public bool                 ExisteCaja        { get; set; }

    public async Task OnGetAsync()
    {
        ExisteCuadre = await _svc.ExisteAsync(HistoricoService.HOJA_CUADRE_H);
        if (ExisteCuadre)
            (EncabezadosCuadre, FilasCuadre) = await _svc.LeerAsync(HistoricoService.HOJA_CUADRE_H);

        ExisteCaja = await _svc.ExisteAsync(HistoricoService.HOJA_CAJA_H);
        if (ExisteCaja)
            (EncabezadosCaja, FilasCaja) = await _svc.LeerAsync(HistoricoService.HOJA_CAJA_H);
    }
}
