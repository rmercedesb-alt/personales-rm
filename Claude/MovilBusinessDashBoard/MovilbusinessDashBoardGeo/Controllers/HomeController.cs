using Microsoft.AspNetCore.Mvc;

namespace DashVisitas.Controllers;

// App standalone de Geolocalizaciones: la raiz "/" lleva directo al dashboard Geo.
[Route("")]
public class HomeController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        if (HttpContext.Session.GetString("user_login") is null)
            return RedirectToAction("Login", "Account");
        return RedirectToAction("Index", "Geo");
    }

    // Compatibilidad: /menu no existe en el standalone -> llevar al dashboard Geo
    [HttpGet("/menu")]
    public IActionResult Menu() => RedirectToAction("Index", "Geo");
}
