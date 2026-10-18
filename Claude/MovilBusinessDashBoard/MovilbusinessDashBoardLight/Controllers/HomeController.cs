using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DashVisitas.Controllers;

[Route("")]
public class HomeController(IConfiguration config) : Controller
{
    private SqlConnection NewConn() =>
        new(config.GetConnectionString("Default"));

    // ── GET / ──────────────────────────────────────────────────────────────────
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // Verificar sesion
        if (HttpContext.Session.GetString("user_login") is null)
            return RedirectToAction("Login", "Account");

        // Cargar info de la empresa desde la BD
        string empNombre  = "";
        bool   empHasLogo = false;

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            await using var cmd = new SqlCommand("SELECT TOP 1 * FROM Empresa WHERE EmpLogo IS NOT NULL ORDER BY EmpID", cn);
            await using var rd  = await cmd.ExecuteReaderAsync();

            if (await rd.ReadAsync())
            {
                // Columnas disponibles (insensible a mayusculas)
                var cols = Enumerable.Range(0, rd.FieldCount)
                                     .Select(i => rd.GetName(i))
                                     .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Intentar varias columnas de nombre
                foreach (var col in new[] { "EmpNombre", "EmpRazonSocial", "EmpDescripcion", "EmpNombreComercial", "Nombre" })
                {
                    if (!cols.Contains(col)) continue;
                    var idx = rd.GetOrdinal(col);
                    if (rd.IsDBNull(idx)) continue;
                    empNombre = rd.GetString(idx).Trim();
                    if (!string.IsNullOrEmpty(empNombre)) break;
                }

                // Verificar si hay logo binario
                foreach (var col in new[] { "EmpLogo", "EmpImagen", "EmpFoto", "Logo" })
                {
                    if (!cols.Contains(col)) continue;
                    var idx = rd.GetOrdinal(col);
                    if (!rd.IsDBNull(idx) && rd.GetFieldType(idx) != typeof(string))
                    {
                        empHasLogo = true;
                        break;
                    }
                }
            }
        }
        catch { /* si no hay tabla Empresa, usar valores por defecto */ }

        ViewBag.EmpNombre  = empNombre;
        ViewBag.EmpHasLogo = empHasLogo;

        return View();
    }
}
