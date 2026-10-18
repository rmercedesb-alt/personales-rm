using Microsoft.AspNetCore.Mvc;

namespace DashVisitas.Controllers;

[Route("")]
public class HomeController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        if (HttpContext.Session.GetString("user_login") is null)
            return RedirectToAction("Login", "Account");
        return RedirectToAction("Index", "Seguimientos");
    }

    // Compatibilidad: /menu no existe en el standalone -> al tablero
    [HttpGet("/menu")]
    public IActionResult Menu() => RedirectToAction("Index", "Seguimientos");
}
