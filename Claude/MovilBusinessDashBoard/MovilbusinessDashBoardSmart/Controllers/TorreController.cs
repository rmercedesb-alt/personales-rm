using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

[Route("torre")]
[RequiereModulo("torre")]
public class TorreController(IConfiguration config) : Controller
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (HttpContext.Session.GetString("user_login") is null)
            return RedirectToAction("Login", "Account");

        string empNombre = "";
        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT TOP 1 * FROM Empresa ORDER BY EmpID", cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                var cols = Enumerable.Range(0, rd.FieldCount).Select(i => rd.GetName(i)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var col in new[] { "EmpNombre", "EmpRazonSocial", "EmpDescripcion", "Nombre" })
                {
                    if (!cols.Contains(col)) continue;
                    var idx = rd.GetOrdinal(col);
                    if (rd.IsDBNull(idx)) continue;
                    empNombre = rd.GetString(idx).Trim();
                    if (!string.IsNullOrEmpty(empNombre)) break;
                }
            }
        }
        catch { }
        ViewBag.EmpNombre = empNombre;
        ViewBag.ShowMenu = true;
        return View();
    }
}
