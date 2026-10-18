using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Mantenimiento;

public class ImportarModel : PageModel
{
    private readonly MigracionService _migracion;
    private readonly IWebHostEnvironment _env;

    public ImportarModel(MigracionService migracion, IWebHostEnvironment env)
    {
        _migracion = migracion;
        _env       = env;
    }

    public ResultadoMigracion?   Resultado      { get; set; }
    public ResultadoImportacion? ResultadoFinal { get; set; }
    public string ArchivoTemp { get; set; } = string.Empty;

    public void OnGet() => SetViewData();

    // ── Descargar plantilla ────────────────────────────────────────────────
    public IActionResult OnGetPlantilla()
    {
        var bytes = _migracion.GenerarPlantillaExcel();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Plantilla_Importacion_AD_Enterprise.xlsx");
    }

    // ── Validar archivo antes de importar ─────────────────────────────────
    public async Task<IActionResult> OnPostValidarAsync(IFormFile Archivo)
    {
        SetViewData();
        if (Archivo == null || Archivo.Length == 0)
        {
            TempData["Error"] = "Seleccione un archivo Excel.";
            return Page();
        }

        var tempDir = Path.Combine(_env.ContentRootPath, "temp");
        Directory.CreateDirectory(tempDir);
        ArchivoTemp = Path.Combine(tempDir, $"{Guid.NewGuid()}.xlsx");

        using (var fs = new FileStream(ArchivoTemp, FileMode.Create))
            await Archivo.CopyToAsync(fs);

        using var stream = new FileStream(ArchivoTemp, FileMode.Open);
        var usuario = User.FindFirst("NombreCompleto")?.Value ?? "Sistema";
        Resultado = _migracion.Validar(stream, usuario);

        return Page();
    }

    // ── Confirmar e importar ───────────────────────────────────────────────
    public async Task<IActionResult> OnPostConfirmarAsync(string ArchivoTemp)
    {
        SetViewData();
        if (string.IsNullOrEmpty(ArchivoTemp) || !System.IO.File.Exists(ArchivoTemp))
        {
            TempData["Error"] = "El archivo temporal no se encontró. Vuelva a subir el archivo.";
            return RedirectToPage();
        }

        try
        {
            var usuario = User.FindFirst("NombreCompleto")?.Value ?? "Sistema";
            using var stream = new FileStream(ArchivoTemp, FileMode.Open);
            ResultadoFinal = await _migracion.ImportarAsync(stream, usuario);

            TempData["Exito"] = $"✅ Importación completada. {ResultadoFinal.Resumen}";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"❌ Error durante la importación: {ex.Message}";
        }
        finally
        {
            if (System.IO.File.Exists(ArchivoTemp))
                System.IO.File.Delete(ArchivoTemp);
        }

        return RedirectToPage();
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
