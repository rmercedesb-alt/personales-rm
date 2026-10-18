using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorfiAcademy.Data;
using PorfiAcademy.Services;

namespace PorfiAcademy.Controllers;

public record UploadRequest(string? Title, bool IsPublicClip);

public class AdminVideosViewModel
{
    public List<MuxAssetInfo> Assets { get; set; } = new();
    public HashSet<string> FreeIds { get; set; } = new();
}

/// <summary>Conexión con Mux, clave de firma y videos. El resto de la administración está en /Manage.</summary>
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly MuxService _mux;
    private readonly ISecretStore _secrets;
    private readonly ApplicationDbContext _db;

    public AdminController(MuxService mux, ISecretStore secrets, ApplicationDbContext db)
    {
        _mux = mux;
        _secrets = secrets;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var credentials = _secrets.GetMux();
        ViewBag.Configured = credentials is not null;
        ViewBag.TokenIdHint = credentials is null ? null : credentials.TokenId[..Math.Min(6, credentials.TokenId.Length)] + "…";
        ViewBag.HasSigningKey = credentials?.HasSigningKey == true;
        var (ok, message, assets) = await _mux.ListAssetsAsync();
        ViewBag.MuxOk = ok;
        ViewBag.MuxMessage = message;
        var free = (await _db.VideoMetas.AsNoTracking().Where(v => v.IsFree).Select(v => v.AssetId).ToListAsync()).ToHashSet();
        return View(new AdminVideosViewModel { Assets = assets, FreeIds = free });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SigningKey(string keyId, string privateKey)
    {
        var credentials = _secrets.GetMux();
        if (credentials is null) return RedirectToAction(nameof(Index));

        keyId = (keyId ?? "").Trim();
        privateKey = (privateKey ?? "").Trim();
        if (keyId == "" || privateKey == "")
        {
            TempData["Error"] = "Escribe el ID y la clave privada de la clave de firma.";
        }
        else if (!MuxTokenSigner.TryLoadKey(privateKey, out var rsa, out var error))
        {
            TempData["Error"] = error;
        }
        else
        {
            rsa!.Dispose();
            credentials.SigningKeyId = keyId;
            credentials.SigningPrivateKey = privateKey;
            _secrets.SaveMux(credentials);
            TempData["Info"] = "Clave de firma guardada. Ya se pueden reproducir videos protegidos.";
        }
        return RedirectToAction(nameof(Index));
    }

    // Devuelve la URL de subida directa a Mux; el navegador sube el archivo directamente allí.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUpload([FromBody] UploadRequest request)
    {
        var origin = $"{Request.Scheme}://{Request.Host}";
        var title = request.Title?.Trim();
        if (title is { Length: > 120 }) title = title[..120];
        var (ok, message, url) = await _mux.CreateUploadAsync(origin, title, request.IsPublicClip);
        return ok ? Json(new { url }) : BadRequest(new { message });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAsset(string id)
    {
        var (ok, message) = await _mux.DeleteAssetAsync(id);
        if (ok) await _db.VideoMetas.Where(v => v.AssetId == id).ExecuteDeleteAsync();
        TempData[ok ? "Info" : "Error"] = message;
        return RedirectToAction(nameof(Index));
    }

    // Marca un video como gratuito (basta con tener cuenta) o de pago (exige suscripción vigente).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFree(string id)
    {
        var asset = await _mux.GetAssetAsync(id);
        if (asset is null) return NotFound();
        var meta = await _db.VideoMetas.FindAsync(id);
        if (meta is null) _db.VideoMetas.Add(new VideoMeta { AssetId = id, IsFree = true });
        else meta.IsFree = !meta.IsFree;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
