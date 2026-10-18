using ADEnterprise.Models;
using ClosedXML.Excel;
using System.Text.Json;

namespace ADEnterprise.Services;

/// <summary>
/// Gestiona backups completos de toda la data en Google Sheets.
/// Los backups se guardan como archivos JSON en la carpeta Data/Backups/.
/// </summary>
public class BackupService
{
    private readonly GoogleSheetsService _gs;
    private readonly IWebHostEnvironment _env;

    // Carpeta donde se guardan los backups (fuera de wwwroot por seguridad)
    private string CarpetaBackups => Path.Combine(_env.ContentRootPath, "Data", "Backups");

    public BackupService(GoogleSheetsService gs, IWebHostEnvironment env)
    {
        _gs  = gs;
        _env = env;
        Directory.CreateDirectory(CarpetaBackups);
    }

    // ─── CREAR BACKUP ────────────────────────────────────────────────────────

    /// <summary>
    /// Lee todas las hojas y guarda un snapshot JSON con timestamp.
    /// Retorna el nombre del archivo creado.
    /// </summary>
    public async Task<string> CrearBackupAsync(string usuario)
    {
        var snapshot = new BackupSnapshot
        {
            FechaBackup       = DateTime.Now,
            UsuarioBackup     = usuario,
            // Tablas originales
            Facturas          = await _gs.LeerAsync(FacturaService.HOJA_FACTURAS),
            Pagos             = await _gs.LeerAsync(GoogleSheetsService.HOJA_PAGOS),
            Companias         = await _gs.LeerAsync(GoogleSheetsService.HOJA_COMPANIAS),
            Clientes          = await _gs.LeerAsync(GoogleSheetsService.HOJA_CLIENTES),
            Usuarios          = await _gs.LeerAsync(GoogleSheetsService.HOJA_USUARIOS),
            // Tablas v2.0 — Finanzas
            Caja              = await _gs.LeerAsync(CajaService.HOJA_CAJA),
            CuadreCaja        = await _gs.LeerAsync(CajaService.HOJA_CUADRE),
            Egresos           = await _gs.LeerAsync(EgresosService.HOJA),
            IngresosCapital   = await _gs.LeerAsync(IngresosCapitalService.HOJA),
        };

        var nombreArchivo = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var rutaCompleta  = Path.Combine(CarpetaBackups, nombreArchivo);

        var opciones = new JsonSerializerOptions { WriteIndented = true };
        var json     = JsonSerializer.Serialize(snapshot, opciones);
        await File.WriteAllTextAsync(rutaCompleta, json);

        return nombreArchivo;
    }

    // ─── LISTAR BACKUPS ──────────────────────────────────────────────────────

    public List<InfoBackup> ListarBackups()
    {
        return Directory.GetFiles(CarpetaBackups, "backup_*.json")
            .Select(ruta =>
            {
                var info = new FileInfo(ruta);
                return new InfoBackup
                {
                    NombreArchivo = info.Name,
                    FechaCreacion = info.CreationTime,
                    TamanoKB      = info.Length / 1024
                };
            })
            .OrderByDescending(b => b.FechaCreacion)
            .ToList();
    }

    // ─── RESTAURAR BACKUP ────────────────────────────────────────────────────

    /// <summary>
    /// Restaura un backup.
    /// ANTES de restaurar, crea automáticamente un backup de seguridad.
    /// </summary>
    public async Task<string> RestaurarAsync(string nombreArchivo, string usuario)
    {
        // 1. Backup de seguridad de la data actual
        var backupSeguridad = await CrearBackupAsync($"AUTO-ANTES-RESTAURACION-{usuario}");

        // 2. Leer el archivo de backup a restaurar
        var ruta = Path.Combine(CarpetaBackups, nombreArchivo);
        if (!File.Exists(ruta))
            throw new FileNotFoundException($"Backup '{nombreArchivo}' no encontrado.");

        var json     = await File.ReadAllTextAsync(ruta);
        var snapshot = JsonSerializer.Deserialize<BackupSnapshot>(json)
            ?? throw new InvalidDataException("El archivo de backup está corrupto.");

        // 3. Restaurar cada hoja (borrar y reescribir)
        await RestaurarHojaAsync(FacturaService.HOJA_FACTURAS,           snapshot.Facturas);
        await RestaurarHojaAsync(GoogleSheetsService.HOJA_PAGOS,         snapshot.Pagos);
        await RestaurarHojaAsync(GoogleSheetsService.HOJA_COMPANIAS,     snapshot.Companias);
        await RestaurarHojaAsync(GoogleSheetsService.HOJA_CLIENTES,      snapshot.Clientes);
        // Tablas v2.0 — Finanzas
        await RestaurarHojaAsync(CajaService.HOJA_CAJA,                  snapshot.Caja);
        await RestaurarHojaAsync(CajaService.HOJA_CUADRE,                snapshot.CuadreCaja);
        await RestaurarHojaAsync(EgresosService.HOJA,                    snapshot.Egresos);
        await RestaurarHojaAsync(IngresosCapitalService.HOJA,            snapshot.IngresosCapital);
        // Usuarios NO se restauran automáticamente por seguridad

        return backupSeguridad;
    }

    private async Task RestaurarHojaAsync(string hoja, IList<IList<object>> datos)
    {
        if (datos == null || datos.Count == 0) return;

        // Limpiar hoja actual (fila 1 en adelante, preservar encabezado si viene en datos)
        await _gs.LimpiarHojaAsync(hoja);

        // Escribir todas las filas del backup
        foreach (var fila in datos)
        {
            await _gs.AgregarFilaAsync(hoja, fila);
        }
    }

    // ─── EXPORTAR A EXCEL (DESCARGA COMPLETA) ────────────────────────────────

    /// <summary>
    /// Genera un Excel con todas las hojas de datos para descarga.
    /// </summary>
    public async Task<byte[]> ExportarExcelCompletoAsync()
    {
        var facturas        = await _gs.LeerAsync(FacturaService.HOJA_FACTURAS);
        var pagos           = await _gs.LeerAsync(GoogleSheetsService.HOJA_PAGOS);
        var companias       = await _gs.LeerAsync(GoogleSheetsService.HOJA_COMPANIAS);
        var clientes        = await _gs.LeerAsync(GoogleSheetsService.HOJA_CLIENTES);
        var caja            = await _gs.LeerAsync(CajaService.HOJA_CAJA);
        var cuadreCaja      = await _gs.LeerAsync(CajaService.HOJA_CUADRE);
        var egresos         = await _gs.LeerAsync(EgresosService.HOJA);
        var ingresosCapital = await _gs.LeerAsync(IngresosCapitalService.HOJA);

        using var wb = new XLWorkbook();

        AgregarHojaExcel(wb, "Facturas",         facturas);
        AgregarHojaExcel(wb, "Pagos",            pagos);
        AgregarHojaExcel(wb, "Companias",        companias);
        AgregarHojaExcel(wb, "Clientes",         clientes);
        AgregarHojaExcel(wb, "Caja",             caja);
        AgregarHojaExcel(wb, "CuadreCaja",       cuadreCaja);
        AgregarHojaExcel(wb, "Egresos",          egresos);
        AgregarHojaExcel(wb, "IngresosCapital",  ingresosCapital);

        // Pestaña de info
        var info = wb.Worksheets.Add("Info");
        info.Cell(1, 1).Value = "AD ALL DIGITAL ENTERPRISE — Exportación Completa";
        info.Cell(1, 1).Style.Font.Bold = true;
        info.Cell(1, 1).Style.Font.FontSize = 14;
        info.Cell(3, 1).Value = $"Fecha de exportación: {DateTime.Now:dd/MM/yyyy HH:mm}";
        info.Cell(4, 1).Value = "Este archivo contiene toda la data del sistema (9 tablas).";
        info.Cell(5, 1).Value = "Puede usarse para importación masiva o como respaldo.";
        info.Column(1).Width = 60;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Lee un archivo de backup y lo convierte a Excel para descarga.</summary>
    public async Task<byte[]> DescargarBackupComoExcelAsync(string nombreArchivo)
    {
        var ruta     = Path.Combine(CarpetaBackups, nombreArchivo);
        var json     = await File.ReadAllTextAsync(ruta);
        var snapshot = JsonSerializer.Deserialize<BackupSnapshot>(json)
            ?? throw new InvalidDataException("Backup corrupto.");

        using var wb = new XLWorkbook();

        AgregarHojaExcel(wb, "Facturas",        snapshot.Facturas);
        AgregarHojaExcel(wb, "Pagos",           snapshot.Pagos);
        AgregarHojaExcel(wb, "Companias",       snapshot.Companias);
        AgregarHojaExcel(wb, "Clientes",        snapshot.Clientes);
        AgregarHojaExcel(wb, "Caja",            snapshot.Caja);
        AgregarHojaExcel(wb, "CuadreCaja",      snapshot.CuadreCaja);
        AgregarHojaExcel(wb, "Egresos",         snapshot.Egresos);
        AgregarHojaExcel(wb, "IngresosCapital", snapshot.IngresosCapital);

        var info = wb.Worksheets.Add("Info");
        info.Cell(1, 1).Value = "AD ALL DIGITAL ENTERPRISE — Backup";
        info.Cell(1, 1).Style.Font.Bold = true;
        info.Cell(1, 1).Style.Font.FontSize = 14;
        info.Cell(3, 1).Value = $"Backup creado: {snapshot.FechaBackup:dd/MM/yyyy HH:mm}";
        info.Cell(4, 1).Value = $"Por usuario:   {snapshot.UsuarioBackup}";
        info.Column(1).Width = 60;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void AgregarHojaExcel(XLWorkbook wb, string nombre, IList<IList<object>> datos)
    {
        var ws = wb.Worksheets.Add(nombre);
        if (datos == null || datos.Count == 0) return;

        for (int r = 0; r < datos.Count; r++)
        {
            var fila = datos[r];
            for (int c = 0; c < fila.Count; c++)
            {
                ws.Cell(r + 1, c + 1).Value = fila[c]?.ToString() ?? "";
            }
            // Encabezado con estilo
            if (r == 0)
            {
                var rango = ws.Range(1, 1, 1, fila.Count);
                rango.Style.Font.Bold            = true;
                rango.Style.Fill.BackgroundColor = XLColor.FromHtml("#0a2463");
                rango.Style.Font.FontColor       = XLColor.White;
            }
        }
        ws.Columns().AdjustToContents();
    }

    /// <summary>Elimina un archivo de backup.</summary>
    public void EliminarBackup(string nombreArchivo)
    {
        // Solo permite borrar archivos de backup, no cualquier archivo
        if (!nombreArchivo.StartsWith("backup_") || !nombreArchivo.EndsWith(".json"))
            throw new InvalidOperationException("Nombre de archivo no válido.");

        var ruta = Path.Combine(CarpetaBackups, nombreArchivo);
        if (File.Exists(ruta))
            File.Delete(ruta);
    }
}

// ─── MODELOS ─────────────────────────────────────────────────────────────────

public class BackupSnapshot
{
    public DateTime FechaBackup   { get; set; }
    public string UsuarioBackup   { get; set; } = string.Empty;
    // Tablas originales
    public IList<IList<object>> Facturas        { get; set; } = new List<IList<object>>();
    public IList<IList<object>> Pagos           { get; set; } = new List<IList<object>>();
    public IList<IList<object>> Companias       { get; set; } = new List<IList<object>>();
    public IList<IList<object>> Clientes        { get; set; } = new List<IList<object>>();
    public IList<IList<object>> Usuarios        { get; set; } = new List<IList<object>>();
    // Tablas v2.0 — Finanzas
    public IList<IList<object>> Caja            { get; set; } = new List<IList<object>>();
    public IList<IList<object>> CuadreCaja      { get; set; } = new List<IList<object>>();
    public IList<IList<object>> Egresos         { get; set; } = new List<IList<object>>();
    public IList<IList<object>> IngresosCapital { get; set; } = new List<IList<object>>();
}

public class InfoBackup
{
    public string   NombreArchivo { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public long     TamanoKB      { get; set; }

    public string FechaFormateada => FechaCreacion.ToString("dd/MM/yyyy HH:mm:ss");
    public string NombreLegible   => $"Backup {FechaCreacion:dd/MM/yyyy} a las {FechaCreacion:HH:mm}";
    public bool   EsAutoSeguridad => NombreArchivo.Contains("AUTO-ANTES-RESTAURACION");
}
