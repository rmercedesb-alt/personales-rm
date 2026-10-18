using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

// ── Página del módulo "Presupuestos Smart" (creación/edición) ──────────────
// Gate de acceso: Rol='Gerencia' en SupervisionUsuarioMapa_RM (mismo mecanismo
// que Supervisión Dinámica) — un usuario sin ese rol nunca ve esta pantalla.
[Route("presupuestos/gestion")]
[RequiereModulo("presupuestosGestion")]
public class PresupuestoGestionController(IConfiguration config) : Controller
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));

    [HttpGet("")]
    public IActionResult Index()
    {
        if (HttpContext.Session.GetString("user_login") is null)
            return RedirectToAction("Login", "Account");

        var user = HttpContext.Session.GetString("user_login") ?? "";
        string rol = "Ninguno";
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 Rol FROM dbo.SupervisionUsuarioMapa_RM WITH (NOLOCK) WHERE UsuInicioSesion=@u AND Activo=1", cn);
            cmd.Parameters.AddWithValue("@u", user);
            var v = cmd.ExecuteScalar();
            if (v != null && v != DBNull.Value) rol = v.ToString()!.Trim();
        }
        catch { }

        if (rol != "Gerencia")
            return Content("Acceso restringido: este módulo es solo para usuarios con rol Gerencia.", "text/plain");

        return View();
    }
}
