using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Mantenimiento;

public class BackupModel : PageModel
{
    private readonly BackupService       _backup;
    private readonly MantenimientoService _mant;

    public BackupModel(BackupService backup, MantenimientoService mant)
    {
        _backup = backup;
        _mant   = mant;
    }

    public List<InfoBackup> Backups { get; set; } = new();

    public void OnGet()
    {
        SetViewData();
        Backups = _backup.ListarBackups();
    }

    // GET: descargar Excel completo actual
    public async Task<IActionResult> OnGetExportarExcel()
    {
        var bytes    = await _backup.ExportarExcelCompletoAsync();
        var filename = $"AD-Enterprise-Data-{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            filename);
    }

    // GET: descargar un backup específico como Excel
    public async Task<IActionResult> OnGetDescargarBackup(string archivo)
    {
        try
        {
            var bytes    = await _backup.DescargarBackupComoExcelAsync(archivo);
            var filename = archivo.Replace(".json", ".xlsx");
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                filename);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al descargar backup: {ex.Message}";
            return RedirectToPage();
        }
    }

    // POST: crear backup manual
    public async Task<IActionResult> OnPostCrearBackup()
    {
        SetViewData();
        var usuario = User.FindFirst("NombreCompleto")?.Value ?? User.Identity?.Name ?? "Sistema";
        try
        {
            var archivo = await _backup.CrearBackupAsync(usuario);
            TempData["Exito"] = $"✅ Backup creado exitosamente: {archivo}";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al crear backup: {ex.Message}";
        }
        return RedirectToPage();
    }

    // POST: restaurar backup
    public async Task<IActionResult> OnPostRestaurar(string archivo)
    {
        SetViewData();
        var usuario = User.FindFirst("NombreCompleto")?.Value ?? User.Identity?.Name ?? "Sistema";
        try
        {
            var backupSeguridad = await _backup.RestaurarAsync(archivo, usuario);
            TempData["Exito"] = $"✅ Sistema restaurado exitosamente desde '{archivo}'. " +
                                $"Se creó backup de seguridad: {backupSeguridad}";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al restaurar: {ex.Message}";
        }
        return RedirectToPage();
    }

    // POST: eliminar backup
    public IActionResult OnPostEliminar(string archivo)
    {
        try
        {
            _backup.EliminarBackup(archivo);
            TempData["Exito"] = $"Backup '{archivo}' eliminado.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al eliminar: {ex.Message}";
        }
        return RedirectToPage();
    }

    // POST: formatear encabezados y tabla en Google Sheets
    public async Task<IActionResult> OnPostFormatearHojasAsync()
    {
        SetViewData();
        try
        {
            var resultado = await _mant.FormatearTodasLasHojasAsync();
            TempData["Exito"] = resultado;
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al formatear hojas: {ex.Message}";
        }
        return RedirectToPage();
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
