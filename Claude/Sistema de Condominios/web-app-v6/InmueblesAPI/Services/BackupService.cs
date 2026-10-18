using System.Diagnostics;
using System.Reflection;
using ClosedXML.Excel;
using InmueblesAPI.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InmueblesAPI.Services;

public record BackupResultado(
    bool Ok,
    string Mensaje,
    string? ArchivoExcel,
    string? ArchivoBackup,
    bool SubidoDrive,
    List<string> Detalles
);

// V6 #8 — Respaldo automático: dump de PostgreSQL + espejo Excel consultable,
// copia local con rotación y subida opcional a Google Drive (rclone).
public class BackupService(AppDbContext db, IConfiguration config, ILogger<BackupService> logger)
{
    public async Task<BackupResultado> EjecutarAsync(string disparadoPor)
    {
        var detalles = new List<string>();
        var carpeta   = await GetConfig("BACKUP_CARPETA_LOCAL")   ?? @"C:\InmueblesV6-Backups";
        var cliente   = await GetConfig("BACKUP_CLIENTE_NOMBRE")  ?? "cliente";
        var retencion = int.TryParse(await GetConfig("BACKUP_RETENCION_DIAS"), out var r) ? Math.Max(1, r) : 8;
        var rcloneRem = (await GetConfig("BACKUP_RCLONE_REMOTO"))?.Trim() ?? "";
        var driveOn   = (await GetConfig("BACKUP_DRIVE_HABILITADO")) == "true" && rcloneRem.Length > 0;
        var safe      = new string((cliente).Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());
        if (safe.Length == 0) safe = "cliente";

        Directory.CreateDirectory(carpeta);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd");

        string? excel = null, backup = null;
        bool subido = false;

        // 1) Espejo Excel consultable (una pestaña por tabla)
        try
        {
            excel = Path.Combine(carpeta, $"Inmuebles_{safe}_{stamp}.xlsx");
            await GenerarExcelAsync(excel);
            detalles.Add($"Excel generado: {Path.GetFileName(excel)}");
        }
        catch (Exception ex) { logger.LogError(ex, "Error generando Excel de respaldo"); detalles.Add("ERROR Excel: " + ex.Message); excel = null; }

        // 2) Respaldo real de PostgreSQL (pg_dump, formato custom)
        try
        {
            backup = Path.Combine(carpeta, $"inmuebles_rd_{safe}_{stamp}.backup");
            await EjecutarPgDumpAsync(backup);
            detalles.Add($"Respaldo BD generado: {Path.GetFileName(backup)}");
        }
        catch (Exception ex) { logger.LogError(ex, "Error en pg_dump"); detalles.Add("ERROR pg_dump: " + ex.Message); backup = null; }

        // 3) Rotación local (conservar los últimos N de cada tipo)
        try
        {
            RotarLocal(carpeta, "Inmuebles_*.xlsx", retencion);
            RotarLocal(carpeta, "inmuebles_rd_*.backup", retencion);
            detalles.Add($"Rotación local: se conservan los últimos {retencion}.");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Error en rotación local"); detalles.Add("Aviso rotación: " + ex.Message); }

        // 4) Subida a Google Drive (rclone), opcional
        if (driveOn)
        {
            try
            {
                await SubirRcloneAsync(carpeta, rcloneRem, retencion);
                subido = true;
                detalles.Add($"Subido a Drive: {rcloneRem}");
            }
            catch (Exception ex) { logger.LogError(ex, "Error subiendo a Drive"); detalles.Add("ERROR Drive: " + ex.Message); }
        }
        else detalles.Add("Subida a Drive desactivada (configúrala en Configuración).");

        var ok = excel != null || backup != null;
        var msg = ok ? "Respaldo completado." : "El respaldo falló.";
        logger.LogInformation("Respaldo ({quien}): {msg} {det}", disparadoPor, msg, string.Join(" | ", detalles));
        return new BackupResultado(ok, msg, excel, backup, subido, detalles);
    }

    private Task<string?> GetConfig(string clave) =>
        db.Configuracion.Where(c => c.Clave == clave).Select(c => (string?)c.Valor).FirstOrDefaultAsync();

    // ─── Excel: una hoja por tabla ────────────────────────────────────────────
    private async Task GenerarExcelAsync(string ruta)
    {
        using var wb = new XLWorkbook();
        await Hoja(wb, "Propietarios", db.Propietarios);
        await Hoja(wb, "Inmuebles",    db.Inmuebles);
        await Hoja(wb, "Inquilinos",   db.Inquilinos);
        await Hoja(wb, "Contratos",    db.Contratos);
        await Hoja(wb, "CxC_Cargos",   db.CxCCargos);
        await Hoja(wb, "CxC_Pagos",    db.CxCPagos);
        await Hoja(wb, "Sesiones_Caja", db.SesionesCaja);
        await Hoja(wb, "Liquidaciones", db.Liquidaciones);
        await Hoja(wb, "Adelantos",    db.Adelantos);
        await Hoja(wb, "Abonos",       db.AbonosCuenta);
        wb.SaveAs(ruta);
    }

    private static async Task Hoja<T>(XLWorkbook wb, string nombre, IQueryable<T> query) where T : class
    {
        var datos = await query.AsNoTracking().ToListAsync();
        var ws = wb.Worksheets.Add(nombre.Length > 31 ? nombre[..31] : nombre);
        // Excluir binarios grandes (byte[])
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType != typeof(byte[]))
            .ToArray();

        for (int c = 0; c < props.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = props[c].Name;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B4F8C");
            cell.Style.Font.FontColor = XLColor.White;
        }
        for (int i = 0; i < datos.Count; i++)
            for (int c = 0; c < props.Length; c++)
            {
                var v = props[c].GetValue(datos[i]);
                var cell = ws.Cell(i + 2, c + 1);
                switch (v)
                {
                    case null: break; // celda vacía
                    case DateTime dt: cell.Value = dt; break;
                    case decimal d:    cell.Value = d;  break;
                    case int n:        cell.Value = n;  break;
                    case long l:       cell.Value = l;  break;
                    case bool b:       cell.Value = b;  break;
                    default:           cell.Value = v.ToString(); break;
                }
            }
        ws.SheetView.FreezeRows(1);
        try { ws.Columns().AdjustToContents(); } catch { /* ancho por defecto */ }
    }

    // ─── pg_dump ──────────────────────────────────────────────────────────────
    private async Task EjecutarPgDumpAsync(string ruta)
    {
        var cs  = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Cadena de conexión no configurada.");
        var b   = new NpgsqlConnectionStringBuilder(cs);
        var bin = EncontrarPgBin() ?? throw new InvalidOperationException("No se encontró pg_dump (PostgreSQL).");

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(bin, "pg_dump.exe"),
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-h"); psi.ArgumentList.Add(b.Host ?? "localhost");
        psi.ArgumentList.Add("-p"); psi.ArgumentList.Add((b.Port == 0 ? 5432 : b.Port).ToString());
        psi.ArgumentList.Add("-U"); psi.ArgumentList.Add(b.Username ?? "postgres");
        psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(b.Database ?? "inmuebles_rd");
        psi.ArgumentList.Add("-F"); psi.ArgumentList.Add("c");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(ruta);
        psi.Environment["PGPASSWORD"] = b.Password ?? "";

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar pg_dump.");
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException("pg_dump falló: " + err);
    }

    private static string? EncontrarPgBin()
    {
        foreach (var raiz in new[] { @"C:\Program Files\PostgreSQL", @"C:\Program Files (x86)\PostgreSQL" })
        {
            if (!Directory.Exists(raiz)) continue;
            foreach (var dir in Directory.GetDirectories(raiz).OrderByDescending(d => d))
            {
                var bin = Path.Combine(dir, "bin");
                if (File.Exists(Path.Combine(bin, "pg_dump.exe"))) return bin;
            }
        }
        return null;
    }

    // ─── Rotación local ────────────────────────────────────────────────────────
    private static void RotarLocal(string carpeta, string patron, int conservar)
    {
        var archivos = Directory.GetFiles(carpeta, patron)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
        foreach (var viejo in archivos.Skip(conservar))
            try { viejo.Delete(); } catch { /* ignorar */ }
    }

    // ─── rclone → Google Drive ─────────────────────────────────────────────────
    private async Task SubirRcloneAsync(string carpeta, string remoto, int retencion)
    {
        // Copia los archivos nuevos al remoto y poda los más viejos que 'retencion' días.
        await CorrerRclone($"copy \"{carpeta}\" \"{remoto}\" --include \"Inmuebles_*.xlsx\" --include \"inmuebles_rd_*.backup\"");
        await CorrerRclone($"delete \"{remoto}\" --min-age {retencion}d --include \"Inmuebles_*.xlsx\" --include \"inmuebles_rd_*.backup\"");
    }

    private static async Task CorrerRclone(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "rclone", Arguments = args,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar rclone (¿está instalado y en el PATH?).");
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException("rclone falló: " + err);
    }
}
