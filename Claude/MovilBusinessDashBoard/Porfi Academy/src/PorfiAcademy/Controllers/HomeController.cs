using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorfiAcademy.Data;
using PorfiAcademy.Models;

namespace PorfiAcademy.Controllers;

public class SlideViewModel
{
    public int Id { get; set; }               // 0 = diapositiva de ejemplo (sin conteo)
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public SlideKind Kind { get; set; }
    public string? ImageUrl { get; set; }
    public string? PlaybackId { get; set; }
    public string CtaText { get; set; } = "Suscríbete";
    public string Href { get; set; } = "/Subscribe";
    public int Variant { get; set; }          // fondo de color cuando no hay imagen
}

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;

    public HomeController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var slides = await _db.PromoSlides.AsNoTracking()
            .Where(s => s.IsActive && (s.StartsAt == null || s.StartsAt <= now) && (s.EndsAt == null || s.EndsAt > now))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync();

        var model = slides.Select((s, i) => new SlideViewModel
        {
            Id = s.Id,
            Title = s.Title,
            Subtitle = s.Subtitle,
            Kind = s.Kind,
            ImageUrl = s.ImageFile is null ? null : "/media/promo/" + s.ImageFile,
            PlaybackId = s.MuxPlaybackId,
            CtaText = string.IsNullOrWhiteSpace(s.CtaText) ? "Suscríbete" : s.CtaText!,
            Href = "/promo/click/" + s.Id,
            Variant = i % 3
        }).ToList();

        // Mientras el dueño no cree promociones, se muestran estas de ejemplo (todas llevan a suscribirse).
        if (model.Count == 0)
        {
            model.AddRange(new[]
            {
                new SlideViewModel { Title = "Entrena como un profesional", Subtitle = "Cursos en video por niveles, con el seguimiento de tu entrenador.", CtaText = "Suscríbete ahora", Href = "/Subscribe", Variant = 0 },
                new SlideViewModel { Title = "Evaluación gratuita de tu lanzamiento", Subtitle = "Crea tu cuenta y envía tu primer video.", CtaText = "Quiero mi evaluación", Href = "/Subscribe?tipo=evaluacion", Variant = 1 },
                new SlideViewModel { Title = "Más velocidad, paso a paso", Subtitle = "Desbloquea cada nivel cuando tu entrenador apruebe tu avance.", CtaText = "Ver planes", Href = "/Subscribe", Variant = 2 }
            });
        }
        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
