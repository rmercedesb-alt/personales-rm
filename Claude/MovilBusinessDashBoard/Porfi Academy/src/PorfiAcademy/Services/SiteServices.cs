using Microsoft.EntityFrameworkCore;
using PorfiAcademy.Data;

namespace PorfiAcademy.Services;

/// <summary>Ajustes no secretos del sitio que edita el dueño (instrucciones de depósito, WhatsApp, etc.).</summary>
public class SettingsService
{
    public const string DepositInstructions = "deposit.instructions";
    public const string WhatsApp = "contact.whatsapp";
    public const string ContactEmail = "contact.email";

    private readonly ApplicationDbContext _db;

    public SettingsService(ApplicationDbContext db) => _db = db;

    public async Task<string> GetAsync(string key, string fallback = "") =>
        (await _db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key))?.Value ?? fallback;

    public async Task SetAsync(string key, string value)
    {
        var item = await _db.SiteSettings.FindAsync(key);
        if (item is null) _db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
        else item.Value = value;
        await _db.SaveChangesAsync();
    }

    public async Task<(string Deposit, string WhatsApp, string Email)> GetContactAsync() =>
        (await GetAsync(DepositInstructions), await GetAsync(WhatsApp), await GetAsync(ContactEmail));
}

/// <summary>Guarda las imágenes del carrusel en la carpeta de datos (no en wwwroot, para que persistan y se validen).</summary>
public class MediaStore
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private readonly string _dir;

    public MediaStore(DataPaths paths)
    {
        _dir = Path.Combine(paths.DataDir, "uploads", "promo");
        Directory.CreateDirectory(_dir);
    }

    public string PathFor(string fileName) => Path.Combine(_dir, fileName);

    public static bool IsSafeName(string? name) =>
        name is not null && System.Text.RegularExpressions.Regex.IsMatch(name, @"^[0-9a-f]{32}\.(jpg|png|webp)$");

    public static string ContentType(string name) => Path.GetExtension(name) switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    /// <summary>Valida tamaño y contenido real (firma del archivo, no solo la extensión) antes de guardar.</summary>
    public async Task<(bool Ok, string? FileNameOrError)> SaveAsync(IFormFile file)
    {
        if (file.Length == 0) return (false, "El archivo está vacío.");
        if (file.Length > MaxBytes) return (false, "La imagen pesa más de 5 MB.");

        var header = new byte[12];
        await using (var s = file.OpenReadStream()) _ = await s.ReadAsync(header);
        string? ext = null;
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) ext = "jpg";
        else if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) ext = "png";
        else if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P') ext = "webp";
        if (ext is null) return (false, "Formato no permitido. Usa JPG, PNG o WebP.");

        var name = Guid.NewGuid().ToString("N") + "." + ext;
        await using var output = File.Create(PathFor(name));
        await file.CopyToAsync(output);
        return (true, name);
    }

    public void Delete(string? fileName)
    {
        if (!IsSafeName(fileName)) return;
        var path = PathFor(fileName!);
        if (File.Exists(path)) File.Delete(path);
    }
}
