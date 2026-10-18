using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorfiAcademy.Data;
using PorfiAcademy.Services;

namespace PorfiAcademy.Controllers;

public class WatchViewModel
{
    public string Title { get; set; } = "";
    public string PlaybackId { get; set; } = "";
    public PlaybackTokens Tokens { get; set; } = null!;
    public string UserLabel { get; set; } = "";
}

public class MemberVideo
{
    public MuxAssetInfo Asset { get; set; } = null!;
    public bool IsFree { get; set; }
    public bool Locked { get; set; }
}

public class MembersViewModel
{
    public List<MemberVideo> Videos { get; set; } = new();
    public AccessInfo Access { get; set; } = null!;
}

// Zona de miembros. Crear cuenta da acceso a los videos gratuitos; los de pago exigen una suscripción vigente
// (o ser administrador). El acceso vence solo según la fecha de la suscripción.
// Hoy lista todos los videos de Mux; luego se reemplaza por Cursos > Niveles > Videos.
[Authorize]
public class MembersController : Controller
{
    private readonly MuxService _mux;
    private readonly MuxTokenSigner _signer;
    private readonly AccessService _access;
    private readonly ApplicationDbContext _db;

    public MembersController(MuxService mux, MuxTokenSigner signer, AccessService access, ApplicationDbContext db)
    {
        _mux = mux;
        _signer = signer;
        _access = access;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var access = await _access.GetAsync(User);
        var (_, _, assets) = await _mux.ListAssetsAsync();
        var free = (await _db.VideoMetas.AsNoTracking().Where(v => v.IsFree).Select(v => v.AssetId).ToListAsync()).ToHashSet();

        var videos = assets
            .Where(a => a.Status == "ready" && a.PlaybackPolicy == "signed")
            .Select(a => new MemberVideo { Asset = a, IsFree = free.Contains(a.Id), Locked = !free.Contains(a.Id) && !access.CanWatchPaid })
            .OrderByDescending(v => v.IsFree)
            .ToList();
        return View(new MembersViewModel { Videos = videos, Access = access });
    }

    public async Task<IActionResult> Watch(string id)
    {
        if (!_signer.IsConfigured)
        {
            TempData["Error"] = "La reproducción protegida aún no está configurada (falta la clave de firma de Mux).";
            return RedirectToAction(nameof(Index));
        }

        var asset = await _mux.GetAssetAsync(id);
        if (asset is null || asset.Status != "ready" || asset.PlaybackId is null || asset.PlaybackPolicy != "signed")
            return NotFound();

        // Control de acceso: lo gratuito basta con tener cuenta; lo de pago exige suscripción vigente.
        var isFree = await _db.VideoMetas.AnyAsync(v => v.AssetId == asset.Id && v.IsFree);
        var access = await _access.GetAsync(User);
        if (!isFree && !access.CanWatchPaid)
        {
            TempData["Error"] = "Este video es solo para suscriptores con acceso vigente. Activa o renueva tu suscripción para verlo.";
            return Redirect("/Subscribe");
        }

        // El token vive lo que dura el video más 1 hora; luego hay que volver a abrir la página.
        var lifetime = TimeSpan.FromSeconds(asset.Duration ?? 3600) + TimeSpan.FromHours(1);
        return View(new WatchViewModel
        {
            Title = string.IsNullOrWhiteSpace(asset.Title) ? asset.Id : asset.Title!,
            PlaybackId = asset.PlaybackId,
            Tokens = _signer.Sign(asset.PlaybackId, lifetime),
            UserLabel = User.Identity?.Name ?? ""
        });
    }
}
