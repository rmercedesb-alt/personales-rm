using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorfiAcademy.Data;
using PorfiAcademy.Services;

namespace PorfiAcademy.Controllers;

public class RequestForm
{
    [Required(ErrorMessage = "Escribe tu nombre."), StringLength(120)]
    public string Name { get; set; } = "";
    [StringLength(40)] public string? Phone { get; set; }
    [EmailAddress(ErrorMessage = "Correo no válido."), StringLength(256)] public string? Email { get; set; }
    public RequestType Type { get; set; } = RequestType.Info;
    public int? PlanId { get; set; }
    [StringLength(1000)] public string? Message { get; set; }
    [StringLength(120)] public string? DepositReference { get; set; }
    /// <summary>Campo trampa para bots: las personas no lo ven ni lo llenan.</summary>
    public string? Website { get; set; }
}

public class SubscribeViewModel
{
    public List<Plan> Plans { get; set; } = new();
    public string Deposit { get; set; } = "";
    public string WhatsApp { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public AccessInfo Access { get; set; } = new(false, false, false, null, null);
    public RequestForm Form { get; set; } = new();
}

public class SubscribeController : Controller
{
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Since)> RequestsByIp = new();

    private readonly ApplicationDbContext _db;
    private readonly AccessService _access;
    private readonly SettingsService _settings;

    public SubscribeController(ApplicationDbContext db, AccessService access, SettingsService settings)
    {
        _db = db;
        _access = access;
        _settings = settings;
    }

    [HttpGet("/Subscribe")]
    public async Task<IActionResult> Index(string? tipo)
    {
        var model = await BuildAsync(new RequestForm
        {
            Type = tipo switch
            {
                "evaluacion" => RequestType.FreeTraining,
                "deposito" => RequestType.Deposit,
                _ => RequestType.Info
            },
            Email = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name : null
        });
        return View(model);
    }

    [HttpPost("/Subscribe/Redeem"), Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Redeem(string? code)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (ok, message) = await _access.RedeemAsync(userId, code);
        if (ok)
        {
            TempData["Info"] = message;
            return RedirectToAction("Index", "Members");
        }
        TempData["Error"] = message;
        return Redirect("/Subscribe#pin");
    }

    [HttpPost("/Subscribe/Request"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SendRequest(RequestForm form)
    {
        // Bots: se responde como si hubiera funcionado, sin guardar nada.
        if (!string.IsNullOrEmpty(form.Website)) return Redirect("/Subscribe?enviado=1#gracias");

        if (string.IsNullOrWhiteSpace(form.Phone) && string.IsNullOrWhiteSpace(form.Email))
            ModelState.AddModelError(nameof(form.Phone), "Déjanos un teléfono o un correo para contactarte.");

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var now = DateTime.UtcNow;
        var entry = RequestsByIp.AddOrUpdate(ip, _ => (1, now),
            (_, old) => now - old.Since > TimeSpan.FromHours(1) ? (1, now) : (old.Count + 1, old.Since));
        if (entry.Count > 20)
            ModelState.AddModelError("", "Has enviado muchas solicitudes. Inténtalo de nuevo más tarde.");

        if (!ModelState.IsValid)
        {
            var model = await BuildAsync(form);
            return View("Index", model);
        }

        _db.SubscriptionRequests.Add(new SubscriptionRequest
        {
            UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            Name = form.Name.Trim(),
            Phone = form.Phone?.Trim(),
            Email = form.Email?.Trim(),
            Type = form.Type,
            PlanId = form.PlanId,
            Message = form.Message?.Trim(),
            DepositReference = form.DepositReference?.Trim()
        });
        await _db.SaveChangesAsync();
        return Redirect("/Subscribe?enviado=1#gracias");
    }

    private async Task<SubscribeViewModel> BuildAsync(RequestForm form)
    {
        var (deposit, whatsapp, email) = await _settings.GetContactAsync();
        return new SubscribeViewModel
        {
            Plans = await _db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync(),
            Deposit = deposit,
            WhatsApp = whatsapp,
            ContactEmail = email,
            Access = await _access.GetAsync(User),
            Form = form
        };
    }
}
