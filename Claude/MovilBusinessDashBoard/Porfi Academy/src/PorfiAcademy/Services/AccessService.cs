using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PorfiAcademy.Data;

namespace PorfiAcademy.Services;

public record AccessInfo(bool IsSignedIn, bool IsAdmin, bool HasPaid, DateTime? PaidUntil, string? PlanName)
{
    /// <summary>Puede ver videos de pago (administradores siempre).</summary>
    public bool CanWatchPaid => IsAdmin || HasPaid;
    public int? DaysLeft => PaidUntil is DateTime d ? Math.Max(0, (int)Math.Ceiling((d - DateTime.UtcNow).TotalDays)) : null;
}

/// <summary>
/// Reglas de acceso: crear cuenta da acceso a lo gratuito; lo de pago exige una suscripción vigente.
/// El acceso vence solo: se calcula contra ExpiresAt, no hay roles que quitar a mano.
/// </summary>
public class AccessService
{
    // PIN: 12 caracteres sin letras/números confundibles (sin I, O, 0, 1) → ~59 bits.
    private const string PinAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly ConcurrentDictionary<string, (int Failures, DateTime Since)> RedeemFailures = new();

    private readonly ApplicationDbContext _db;

    public AccessService(ApplicationDbContext db) => _db = db;

    public async Task<AccessInfo> GetAsync(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return new AccessInfo(false, false, false, null, null);
        var isAdmin = user.IsInRole("Admin");
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return new AccessInfo(true, isAdmin, false, null, null);
        return await GetForUserAsync(userId, isAdmin);
    }

    public async Task<AccessInfo> GetForUserAsync(string userId, bool isAdmin = false)
    {
        var now = DateTime.UtcNow;
        var subs = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == userId && !s.Revoked && s.ExpiresAt > now)
            .OrderBy(s => s.StartsAt).ToListAsync();
        var current = subs.FirstOrDefault(s => s.StartsAt <= now);
        if (current is null) return new AccessInfo(true, isAdmin, false, null, null);
        return new AccessInfo(true, isAdmin, true, subs.Max(s => s.ExpiresAt), current.Plan?.Name);
    }

    /// <summary>Suma días de acceso. Si ya hay acceso vigente, los días se agregan al final del actual.</summary>
    public async Task<Subscription> GrantAsync(string userId, Plan plan, int? days, SubscriptionSource source, string? note, string? by)
    {
        var now = DateTime.UtcNow;
        var latest = await _db.Subscriptions
            .Where(s => s.UserId == userId && !s.Revoked && s.ExpiresAt > now)
            .MaxAsync(s => (DateTime?)s.ExpiresAt);
        var start = latest ?? now;
        var sub = new Subscription
        {
            UserId = userId,
            PlanId = plan.Id,
            StartsAt = start,
            ExpiresAt = start.AddDays(days ?? plan.DurationDays),
            Source = source,
            Note = note,
            CreatedBy = by
        };
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();
        return sub;
    }

    /// <summary>Suspende todo el acceso de pago vigente o futuro de un usuario.</summary>
    public async Task<int> RevokeAsync(string userId)
    {
        var now = DateTime.UtcNow;
        return await _db.Subscriptions
            .Where(s => s.UserId == userId && !s.Revoked && s.ExpiresAt > now)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Revoked, true));
    }

    public async Task<(bool Ok, string Message)> RedeemAsync(string userId, string? rawCode)
    {
        var now = DateTime.UtcNow;
        if (RedeemFailures.TryGetValue(userId, out var f) && now - f.Since < TimeSpan.FromMinutes(15) && f.Failures >= 5)
            return (false, "Demasiados intentos con códigos incorrectos. Espera 15 minutos e inténtalo de nuevo.");

        var code = NormalizeCode(rawCode);
        var item = code.Length == 0 ? null : await _db.AccessCodes.Include(c => c.Plan).FirstOrDefaultAsync(c => c.Code == code);
        var valid = item is not null && !item.Cancelled && item.RedeemedAt == null && (item.ValidUntil == null || item.ValidUntil > now);
        if (!valid)
        {
            RedeemFailures.AddOrUpdate(userId,
                _ => (1, now),
                (_, old) => now - old.Since > TimeSpan.FromMinutes(15) ? (1, now) : (old.Failures + 1, old.Since));
            return (false, "El código no es válido, ya fue usado o venció. Revisa que lo hayas escrito bien.");
        }

        // Canje atómico: solo uno gana si dos personas usan el mismo PIN a la vez.
        var claimed = await _db.AccessCodes
            .Where(c => c.Id == item!.Id && c.RedeemedAt == null && !c.Cancelled)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.RedeemedAt, now).SetProperty(c => c.RedeemedByUserId, userId));
        if (claimed != 1) return (false, "Ese código ya fue usado.");

        var sub = await GrantAsync(userId, item!.Plan!, item.DurationDays, SubscriptionSource.Pin, $"PIN {Format(item.Code)}", null);
        RedeemFailures.TryRemove(userId, out _);
        return (true, $"¡Listo! Tu acceso está activo hasta el {sub.ExpiresAt.ToLocalTime():dd/MM/yyyy}.");
    }

    public async Task<List<AccessCode>> CreateCodesAsync(Plan plan, int count, int? days, string? note, DateTime? validUntil)
    {
        var codes = new List<AccessCode>();
        var used = new HashSet<string>();
        while (codes.Count < count)
        {
            var code = NewPin();
            if (!used.Add(code) || await _db.AccessCodes.AnyAsync(c => c.Code == code)) continue;
            codes.Add(new AccessCode { Code = code, PlanId = plan.Id, DurationDays = days, Note = note, ValidUntil = validUntil });
        }
        _db.AccessCodes.AddRange(codes);
        await _db.SaveChangesAsync();
        return codes;
    }

    public static string NormalizeCode(string? raw) =>
        new string((raw ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>XXXX-XXXX-XXXX</summary>
    public static string Format(string code) =>
        code.Length == 12 ? $"{code[..4]}-{code[4..8]}-{code[8..]}" : code;

    private static string NewPin()
    {
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++) chars[i] = PinAlphabet[RandomNumberGenerator.GetInt32(PinAlphabet.Length)];
        return new string(chars);
    }
}
