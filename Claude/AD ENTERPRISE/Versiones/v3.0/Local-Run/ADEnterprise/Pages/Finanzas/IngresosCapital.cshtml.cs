using ADEnterprise.Models;
using ADEnterprise.Helpers;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Finanzas;

public class IngresosCapitalModel : PageModel
{
    private readonly IngresosCapitalService _svc;
    private readonly CajaService            _caja;

    public IngresosCapitalModel(IngresosCapitalService svc, CajaService caja)
    {
        _svc  = svc;
        _caja = caja;
    }

    public List<IngresoCapital> Ingresos         { get; set; } = new();
    public CuadreCaja?          CajaAbierta      { get; set; }
    public string               FormToken        { get; set; } = "";
    public string?              DuplicateWarning { get; set; }

    [BindProperty] public IngresoCapital Nuevo            { get; set; } = new();
    [BindProperty] public bool           ConfirmDuplicate { get; set; }

    // Filtros
    [BindProperty(SupportsGet = true)] public DateTime? Desde { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Hasta { get; set; }

    private string Usuario => User.FindFirst("NombreCompleto")?.Value
                           ?? User.Identity?.Name ?? "Sistema";

    public async Task<IActionResult> OnGetAsync()
    {
        SetViewData();
        var (ok, caja, msg) = await _caja.ValidarCajaAsync();
        CajaAbierta = caja;

        if (!ok)
        {
            TempData["Advertencia"] = msg;
            if (caja != null) return RedirectToPage("/Finanzas/Caja");
        }

        Ingresos  = await _svc.ObtenerTodosAsync(Desde, Hasta);
        Nuevo.Fecha = DateTime.Today;
        FormToken   = FormTokenHelper.Generar(TempData, "Token_IngresosCapital");
        return Page();
    }

    public async Task<IActionResult> OnPostRegistrarAsync()
    {
        SetViewData();
        var (ok, cajaAb, msg) = await _caja.ValidarCajaAsync();
        CajaAbierta = cajaAb;
        if (!ok) { TempData["Error"] = msg; return RedirectToPage(); }

        // Validar token de envío único
        var tokenEnviado = Request.Form["FormToken"].ToString();
        if (!FormTokenHelper.Validar(TempData, "Token_IngresosCapital", tokenEnviado))
        {
            TempData["Advertencia"] = "⚠️ Esta transacción ya fue procesada o el formulario expiró. Verifique los ingresos registrados.";
            return RedirectToPage();
        }

        if (!ModelState.IsValid)
        {
            Ingresos  = await _svc.ObtenerTodosAsync();
            FormToken = FormTokenHelper.Generar(TempData, "Token_IngresosCapital");
            return Page();
        }

        // Detectar posible duplicado (mismo nombre + mismo monto, hoy)
        if (!ConfirmDuplicate)
        {
            var hoy = DateTime.Today;
            var existentes = await _svc.ObtenerTodosAsync(hoy, hoy);
            var posibleDup = existentes.FirstOrDefault(i =>
                string.Equals(i.Nombre, Nuevo.Nombre, StringComparison.OrdinalIgnoreCase) &&
                i.Monto == Nuevo.Monto);

            if (posibleDup != null)
            {
                Ingresos         = await _svc.ObtenerTodosAsync(Desde, Hasta);
                DuplicateWarning = $"Ya existe un ingreso similar hoy: <strong>{posibleDup.Nombre}</strong> por <strong>RD$ {posibleDup.Monto:N2}</strong> ({posibleDup.Secuencia}). ¿Desea registrar otro de todas formas?";
                FormToken        = FormTokenHelper.Generar(TempData, "Token_IngresosCapital");
                return Page();
            }
        }

        try
        {
            var seq = await _svc.RegistrarAsync(Nuevo, Usuario);
            TempData["Exito"] = $"Ingreso de capital {seq} registrado — RD$ {Nuevo.Monto:N2} acreditado a caja.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error: {ex.Message}";
            Ingresos  = await _svc.ObtenerTodosAsync();
            FormToken = FormTokenHelper.Generar(TempData, "Token_IngresosCapital");
            return Page();
        }
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
