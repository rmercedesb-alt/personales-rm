using ADEnterprise.Models;
using ADEnterprise.Helpers;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Finanzas;

public class EgresosModel : PageModel
{
    private readonly EgresosService _svc;
    private readonly CajaService    _caja;

    public EgresosModel(EgresosService svc, CajaService caja) { _svc = svc; _caja = caja; }

    public List<Egreso>  Lista            { get; set; } = new();
    public CuadreCaja?   CajaAbierta      { get; set; }
    public string[]      TiposEgreso      => EgresosService.TiposEgreso;
    public string        FormToken        { get; set; } = "";
    public string?       DuplicateWarning { get; set; }

    [BindProperty] public Egreso Nuevo            { get; set; } = new();
    [BindProperty] public bool   ConfirmDuplicate { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Desde   { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Hasta   { get; set; }
    [BindProperty(SupportsGet = true)] public string    FiltroTipo { get; set; } = "";

    private string Usuario => User.FindFirst("NombreCompleto")?.Value
                           ?? User.Identity?.Name ?? "Sistema";

    public async Task<IActionResult> OnGetAsync()
    {
        SetViewData();
        var (ok, caja, msg) = await _caja.ValidarCajaAsync();
        CajaAbierta = caja;
        if (!ok) { TempData["Advertencia"] = msg; if (caja != null) return RedirectToPage("/Finanzas/Caja"); }
        Lista = await _svc.ObtenerTodosAsync(Desde, Hasta, string.IsNullOrEmpty(FiltroTipo) ? null : FiltroTipo);
        Nuevo.Fecha = DateTime.Today;
        FormToken   = FormTokenHelper.Generar(TempData, "Token_Egresos");
        return Page();
    }

    public async Task<IActionResult> OnPostRegistrarAsync()
    {
        SetViewData();
        var (ok, caja, msg) = await _caja.ValidarCajaAsync();
        CajaAbierta = caja;
        if (!ok) { TempData["Error"] = msg; return RedirectToPage(); }

        // Validar token de envío único
        var tokenEnviado = Request.Form["FormToken"].ToString();
        if (!FormTokenHelper.Validar(TempData, "Token_Egresos", tokenEnviado))
        {
            TempData["Advertencia"] = "⚠️ Esta transacción ya fue procesada o el formulario expiró. Verifique los egresos registrados.";
            return RedirectToPage();
        }

        // Detectar posible duplicado (mismo tipo + mismo nombre + mismo monto, hoy)
        if (!ConfirmDuplicate)
        {
            var hoy = DateTime.Today;
            var existentes = await _svc.ObtenerTodosAsync(hoy, hoy);
            var posibleDup = existentes.FirstOrDefault(e =>
                e.TipoEgreso == Nuevo.TipoEgreso &&
                string.Equals(e.ANombreDe, Nuevo.ANombreDe, StringComparison.OrdinalIgnoreCase) &&
                e.Monto == Nuevo.Monto);

            if (posibleDup != null)
            {
                Lista = await _svc.ObtenerTodosAsync(Desde, Hasta, string.IsNullOrEmpty(FiltroTipo) ? null : FiltroTipo);
                DuplicateWarning = $"Ya existe un egreso similar hoy: <strong>{EgresosService.LabelTipo(posibleDup.TipoEgreso)}</strong> a nombre de <strong>{posibleDup.ANombreDe}</strong> por <strong>RD$ {posibleDup.Monto:N2}</strong> ({posibleDup.SeqEgreso}). ¿Desea registrar otro de todas formas?";
                FormToken = FormTokenHelper.Generar(TempData, "Token_Egresos");
                return Page();
            }
        }

        try
        {
            var seq = await _svc.RegistrarAsync(Nuevo, Usuario);
            TempData["Exito"] = $"Egreso {seq} registrado — RD$ {Nuevo.Monto:N2} debitado de caja.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            Lista     = await _svc.ObtenerTodosAsync();
            FormToken = FormTokenHelper.Generar(TempData, "Token_Egresos");
            return Page();
        }
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
