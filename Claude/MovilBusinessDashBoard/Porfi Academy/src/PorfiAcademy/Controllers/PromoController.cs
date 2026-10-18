using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorfiAcademy.Data;
using PorfiAcademy.Services;

namespace PorfiAcademy.Controllers;

/// <summary>Imágenes del carrusel y conteo de vistas/clics de cada diapositiva.</summary>
public class PromoController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly MediaStore _media;

    public PromoController(ApplicationDbContext db, MediaStore media)
    {
        _db = db;
        _media = media;
    }

    [HttpGet("/media/promo/{file}")]
    public IActionResult Image(string file)
    {
        if (!MediaStore.IsSafeName(file)) return NotFound();
        var path = _media.PathFor(file);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "public, max-age=86400";
        return PhysicalFile(path, MediaStore.ContentType(file));
    }

    [HttpGet("/promo/click/{id:int}")]
    public async Task<IActionResult> Click(int id)
    {
        var slide = await _db.PromoSlides.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (slide is null) return Redirect("/Subscribe");
        await _db.PromoSlides.Where(s => s.Id == id).ExecuteUpdateAsync(u => u.SetProperty(s => s.Clicks, s => s.Clicks + 1));
        return Redirect(SafeTarget(slide.CtaUrl));
    }

    [HttpPost("/promo/view/{id:int}")]
    public async Task<IActionResult> Seen(int id)
    {
        await _db.PromoSlides.Where(s => s.Id == id).ExecuteUpdateAsync(u => u.SetProperty(s => s.Views, s => s.Views + 1));
        return NoContent();
    }

    /// <summary>Solo rutas internas o enlaces http(s); por defecto lleva a suscribirse.</summary>
    public static string SafeTarget(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "/Subscribe";
        url = url.Trim();
        if (url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\")) return url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return uri.ToString();
        return "/Subscribe";
    }
}
