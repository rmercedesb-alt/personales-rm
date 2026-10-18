using Microsoft.AspNetCore.Mvc;

namespace DashProductividad.Controllers;

[Route("menu")]
public class MenuController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        if (HttpContext.Session.GetString("user_login") is null)
            return RedirectToAction("Login", "Account");
        return View();
    }
}
