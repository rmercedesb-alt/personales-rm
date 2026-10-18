using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorfiAcademy.Data;
using PorfiAcademy.Services;

namespace PorfiAcademy.Controllers;

public class ManageHubViewModel
{
    public int NewRequests { get; set; }
    public int ActiveMembers { get; set; }
    public int ExpiringSoon { get; set; }
    public int AvailablePins { get; set; }
    public int ActiveSlides { get; set; }
}

public class RequestRow
{
    public SubscriptionRequest Request { get; set; } = null!;
    public bool HasAccount { get; set; }
}

public class SubscriberRow
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsAdmin { get; set; }
    public string Status { get; set; } = "Gratis";
    public string? PlanName { get; set; }
    public DateTime? PaidUntil { get; set; }
}

public class PinRow
{
    public AccessCode Code { get; set; } = null!;
    public string Status { get; set; } = "";
    public string? RedeemedBy { get; set; }
}

public class PlanForm
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [StringLength(300)] public string? Description { get; set; }
    [Range(1, 3650)] public int DurationDays { get; set; } = 30;
    [Range(0, 10000000)] public decimal? Price { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public class SlideForm
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Title { get; set; } = "";
    [StringLength(300)] public string? Subtitle { get; set; }
    [StringLength(100)] public string? CtaText { get; set; }
    [StringLength(300)] public string? CtaUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public IFormFile? Image { get; set; }
}

[Authorize(Roles = "Admin")]
public class ManageController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly AccessService _access;
    private readonly SettingsService _settings;
    private readonly MediaStore _media;
    private readonly MuxService _mux;
    private readonly UserManager<IdentityUser> _users;

    public ManageController(ApplicationDbContext db, AccessService access, SettingsService settings, MediaStore media,
        MuxService mux, UserManager<IdentityUser> users)
    {
        _db = db;
        _access = access;
        _settings = settings;
        _media = media;
        _mux = mux;
        _users = users;
    }

    // ───────────── Resumen ─────────────
    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var live = _db.Subscriptions.AsNoTracking().Where(s => !s.Revoked && s.ExpiresAt > now);
        var model = new ManageHubViewModel
        {
            NewRequests = await _db.SubscriptionRequests.CountAsync(r => r.Status == RequestStatus.New),
            ActiveMembers = await live.Where(s => s.StartsAt <= now).Select(s => s.UserId).Distinct().CountAsync(),
            ExpiringSoon = (await live.GroupBy(s => s.UserId).Select(g => g.Max(s => s.ExpiresAt)).ToListAsync())
                .Count(d => d < now.AddDays(7)),
            AvailablePins = await _db.AccessCodes.CountAsync(c => !c.Cancelled && c.RedeemedAt == null && (c.ValidUntil == null || c.ValidUntil > now)),
            ActiveSlides = await _db.PromoSlides.CountAsync(s => s.IsActive)
        };
        return View(model);
    }

    // ───────────── Solicitudes ─────────────
    public async Task<IActionResult> Requests(RequestStatus? status)
    {
        var query = _db.SubscriptionRequests.AsNoTracking().Include(r => r.Plan).AsQueryable();
        if (status is not null) query = query.Where(r => r.Status == status);
        var items = await query.OrderBy(r => r.Status).ThenByDescending(r => r.CreatedAt).Take(200).ToListAsync();

        var emails = items.Where(r => !string.IsNullOrWhiteSpace(r.Email)).Select(r => r.Email!.ToUpper()).Distinct().ToList();
        var known = (await _db.Users.AsNoTracking().Where(u => u.NormalizedEmail != null && emails.Contains(u.NormalizedEmail))
            .Select(u => u.NormalizedEmail!).ToListAsync()).ToHashSet();

        ViewBag.Plans = await _db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
        ViewBag.Status = status;
        return View(items.Select(r => new RequestRow
        {
            Request = r,
            HasAccount = r.UserId != null || (r.Email != null && known.Contains(r.Email.ToUpper()))
        }).ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestAction(int id, string action, int? planId, string? note)
    {
        var req = await _db.SubscriptionRequests.FindAsync(id);
        if (req is null) return NotFound();

        if (action == "approve")
        {
            var user = req.UserId is not null ? await _users.FindByIdAsync(req.UserId)
                : (string.IsNullOrWhiteSpace(req.Email) ? null : await _users.FindByEmailAsync(req.Email));
            var plan = await _db.Plans.FindAsync(planId ?? req.PlanId ?? 0);
            if (user is null)
            {
                TempData["Error"] = "Esa persona todavía no tiene cuenta. Pídele que se registre con ese mismo correo y vuelve a aprobar.";
                return RedirectToAction(nameof(Requests));
            }
            if (plan is null)
            {
                TempData["Error"] = "Elige el plan que vas a activar.";
                return RedirectToAction(nameof(Requests));
            }
            var sub = await _access.GrantAsync(user.Id, plan, null,
                req.Type == RequestType.Deposit ? SubscriptionSource.Deposit : SubscriptionSource.Admin,
                $"Solicitud #{req.Id}", User.Identity?.Name);
            req.Status = RequestStatus.Approved;
            TempData["Info"] = $"Acceso activado a {user.Email} hasta el {sub.ExpiresAt.ToLocalTime():dd/MM/yyyy}.";
        }
        else
        {
            req.Status = action switch
            {
                "contacted" => RequestStatus.Contacted,
                "reject" => RequestStatus.Rejected,
                "reopen" => RequestStatus.New,
                _ => req.Status
            };
        }

        req.HandledAt = DateTime.UtcNow;
        req.HandledBy = User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(note)) req.AdminNote = note.Trim();
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Requests));
    }

    // ───────────── Suscripciones ─────────────
    public async Task<IActionResult> Subscriptions(string? q)
    {
        var now = DateTime.UtcNow;
        var users = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToUpper();
            users = users.Where(u => u.NormalizedEmail != null && u.NormalizedEmail.Contains(term));
        }
        var list = await users.OrderBy(u => u.Email).Take(200).Select(u => new { u.Id, u.Email }).ToListAsync();
        var ids = list.Select(u => u.Id).ToList();
        var subs = await _db.Subscriptions.AsNoTracking().Include(s => s.Plan).Where(s => ids.Contains(s.UserId)).ToListAsync();
        var adminIds = (await _users.GetUsersInRoleAsync("Admin")).Select(a => a.Id).ToHashSet();

        var rows = list.Select(u =>
        {
            var mine = subs.Where(s => s.UserId == u.Id).ToList();
            var valid = mine.Where(s => !s.Revoked && s.ExpiresAt > now).ToList();
            var row = new SubscriberRow { UserId = u.Id, Email = u.Email ?? "(sin correo)", IsAdmin = adminIds.Contains(u.Id) };
            if (valid.Any(s => s.StartsAt <= now))
            {
                row.Status = "Activo";
                row.PaidUntil = valid.Max(s => s.ExpiresAt);
                row.PlanName = valid.Where(s => s.StartsAt <= now).OrderByDescending(s => s.ExpiresAt).First().Plan?.Name;
            }
            else if (mine.Any(s => s.Revoked && s.ExpiresAt > now)) row.Status = "Suspendido";
            else if (mine.Count > 0) { row.Status = "Vencido"; row.PaidUntil = mine.Max(s => s.ExpiresAt); }
            return row;
        })
        .OrderBy(r => r.Status switch { "Activo" => 0, "Vencido" => 1, "Suspendido" => 2, _ => 3 })
        .ThenBy(r => r.PaidUntil)
        .ToList();

        ViewBag.Plans = await _db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
        ViewBag.Query = q;
        return View(rows);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Grant(string email, int planId, int? days, string? note)
    {
        var user = string.IsNullOrWhiteSpace(email) ? null : await _users.FindByEmailAsync(email.Trim());
        var plan = await _db.Plans.FindAsync(planId);
        if (user is null) TempData["Error"] = "No existe un usuario con ese correo. Debe crear su cuenta primero.";
        else if (plan is null) TempData["Error"] = "Plan no válido.";
        else if (days is < 1 or > 3650) TempData["Error"] = "Los días deben estar entre 1 y 3650.";
        else
        {
            var sub = await _access.GrantAsync(user.Id, plan, days, SubscriptionSource.Admin, string.IsNullOrWhiteSpace(note) ? "Manual" : note.Trim(), User.Identity?.Name);
            TempData["Info"] = $"Acceso de {user.Email} activo hasta el {sub.ExpiresAt.ToLocalTime():dd/MM/yyyy}.";
        }
        return RedirectToAction(nameof(Subscriptions));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(string userId)
    {
        var n = await _access.RevokeAsync(userId);
        TempData["Info"] = n > 0 ? "Acceso de pago suspendido." : "Ese usuario no tenía acceso de pago vigente.";
        return RedirectToAction(nameof(Subscriptions));
    }

    // ───────────── PINs ─────────────
    public async Task<IActionResult> Pins()
    {
        var now = DateTime.UtcNow;
        var codes = await _db.AccessCodes.AsNoTracking().Include(c => c.Plan).OrderByDescending(c => c.CreatedAt).Take(100).ToListAsync();
        var redeemerIds = codes.Where(c => c.RedeemedByUserId != null).Select(c => c.RedeemedByUserId!).Distinct().ToList();
        var emails = await _db.Users.AsNoTracking().Where(u => redeemerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email);

        ViewBag.Plans = await _db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
        return View(codes.Select(c => new PinRow
        {
            Code = c,
            RedeemedBy = c.RedeemedByUserId is not null && emails.TryGetValue(c.RedeemedByUserId, out var e) ? e : null,
            Status = c.Cancelled ? "Cancelado" : c.RedeemedAt != null ? "Usado"
                : c.ValidUntil != null && c.ValidUntil <= now ? "Vencido" : "Disponible"
        }).ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePins(int planId, int count, int? days, int? validDays, string? note)
    {
        var plan = await _db.Plans.FindAsync(planId);
        if (plan is null || count is < 1 or > 50 || days is < 1 or > 3650 || validDays is < 1 or > 3650)
        {
            TempData["Error"] = "Revisa los datos: elige un plan, entre 1 y 50 códigos y duraciones válidas.";
            return RedirectToAction(nameof(Pins));
        }
        var codes = await _access.CreateCodesAsync(plan, count, days, note?.Trim(),
            validDays is int v ? DateTime.UtcNow.AddDays(v) : null);
        TempData["NewCodes"] = string.Join("\n", codes.Select(c => AccessService.Format(c.Code)));
        TempData["Info"] = $"{codes.Count} código(s) generados para el plan {plan.Name}. Cópialos ahora.";
        return RedirectToAction(nameof(Pins));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelPin(int id)
    {
        await _db.AccessCodes.Where(c => c.Id == id && c.RedeemedAt == null).ExecuteUpdateAsync(u => u.SetProperty(c => c.Cancelled, true));
        return RedirectToAction(nameof(Pins));
    }

    // ───────────── Planes ─────────────
    public async Task<IActionResult> Plans() =>
        View(await _db.Plans.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlan(PlanForm form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Revisa el plan: nombre obligatorio y duración entre 1 y 3650 días.";
            return RedirectToAction(nameof(Plans));
        }
        var plan = form.Id == 0 ? new Plan() : await _db.Plans.FindAsync(form.Id);
        if (plan is null) return NotFound();
        plan.Name = form.Name.Trim();
        plan.Description = form.Description?.Trim();
        plan.DurationDays = form.DurationDays;
        plan.Price = form.Price;
        plan.IsActive = form.IsActive;
        plan.SortOrder = form.SortOrder;
        if (form.Id == 0) _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        TempData["Info"] = "Plan guardado.";
        return RedirectToAction(nameof(Plans));
    }

    // ───────────── Promociones (carrusel) ─────────────
    public async Task<IActionResult> Slides() =>
        View(await _db.PromoSlides.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSlide(SlideForm form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Escribe al menos el título de la diapositiva.";
            return RedirectToAction(nameof(Slides));
        }
        var slide = form.Id == 0 ? new PromoSlide { Kind = SlideKind.Image } : await _db.PromoSlides.FindAsync(form.Id);
        if (slide is null) return NotFound();

        if (form.Image is { Length: > 0 })
        {
            var (ok, nameOrError) = await _media.SaveAsync(form.Image);
            if (!ok)
            {
                TempData["Error"] = nameOrError;
                return RedirectToAction(nameof(Slides));
            }
            _media.Delete(slide.ImageFile);
            slide.ImageFile = nameOrError;
        }

        slide.Title = form.Title.Trim();
        slide.Subtitle = form.Subtitle?.Trim();
        slide.CtaText = form.CtaText?.Trim();
        slide.CtaUrl = form.CtaUrl?.Trim();
        slide.SortOrder = form.SortOrder;
        slide.IsActive = form.IsActive;
        // Las fechas se escriben como día local; el fin incluye todo ese día.
        slide.StartsAt = form.StartsAt is DateTime s ? DateTime.SpecifyKind(s.Date, DateTimeKind.Local).ToUniversalTime() : null;
        slide.EndsAt = form.EndsAt is DateTime e ? DateTime.SpecifyKind(e.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime() : null;
        if (form.Id == 0) _db.PromoSlides.Add(slide);
        await _db.SaveChangesAsync();
        TempData["Info"] = "Diapositiva guardada.";
        return RedirectToAction(nameof(Slides));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSlide(int id)
    {
        var slide = await _db.PromoSlides.FindAsync(id);
        if (slide is not null)
        {
            _media.Delete(slide.ImageFile);
            _db.PromoSlides.Remove(slide);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Slides));
    }

    // Crea una diapositiva de video a partir de un clip PÚBLICO subido en Administración → Videos.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddClipSlide(string assetId)
    {
        var asset = await _mux.GetAssetAsync(assetId);
        if (asset is null || asset.Status != "ready" || asset.PlaybackPolicy != "public" || asset.PlaybackId is null)
        {
            TempData["Error"] = "Solo los clips promocionales públicos y ya procesados se pueden usar en el carrusel.";
            return Redirect("/Admin");
        }
        if (!await _db.PromoSlides.AnyAsync(s => s.MuxPlaybackId == asset.PlaybackId))
        {
            _db.PromoSlides.Add(new PromoSlide
            {
                Title = string.IsNullOrWhiteSpace(asset.Title) ? "Corte promocional" : asset.Title!,
                Kind = SlideKind.Video,
                MuxPlaybackId = asset.PlaybackId,
                CtaText = "Suscríbete",
                SortOrder = 100,
                IsActive = true
            });
            await _db.SaveChangesAsync();
        }
        TempData["Info"] = "Clip agregado al carrusel de la portada.";
        return RedirectToAction(nameof(Slides));
    }

    // ───────────── Ajustes ─────────────
    public async Task<IActionResult> Settings()
    {
        var (deposit, whatsapp, email) = await _settings.GetContactAsync();
        ViewBag.Deposit = deposit;
        ViewBag.WhatsApp = whatsapp;
        ViewBag.Email = email;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(string? deposit, string? whatsapp, string? email)
    {
        await _settings.SetAsync(SettingsService.DepositInstructions, (deposit ?? "").Trim());
        await _settings.SetAsync(SettingsService.WhatsApp, new string((whatsapp ?? "").Where(char.IsDigit).ToArray()));
        await _settings.SetAsync(SettingsService.ContactEmail, (email ?? "").Trim());
        TempData["Info"] = "Ajustes guardados.";
        return RedirectToAction(nameof(Settings));
    }
}
