using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace DashVisitas.Controllers;

[Route("[controller]")]
public class AccountController(IConfiguration config) : Controller
{
    // ── Helpers ────────────────────────────────────────────────────────────────
    private SqlConnection NewConn() =>
        new(config.GetConnectionString("Default"));

    private static string Md5Upper(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToUpper();

    // ── GET /account/login ─────────────────────────────────────────────────────
    [HttpGet("login")]
    public IActionResult Login()
    {
        if (HttpContext.Session.GetString("user_login") is not null)
            return RedirectToAction("Index", "Home");
        return View();
    }

    // ── POST /account/login ────────────────────────────────────────────────────
    // Recibe JSON {usuario, clave} y devuelve JSON {ok, error?}
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.Usuario) || string.IsNullOrEmpty(req?.Clave))
            return Json(new { ok = false, error = "Complete usuario y contrasena" });

        const int MAX_INTENTOS = 5;
        const int BLOQUEO_MINS = 15;

        try
        {
            await using var cn = NewConn();
            await cn.OpenAsync();

            // ── Buscar usuario ────────────────────────────────────────────────
            string? login = null, nombres = null, apellidos = null, claveDB = null;
            bool    estatus      = false;
            int     intentos     = 0;
            DateTime? ultimoIntento = null;

            await using (var cmd = new SqlCommand(@"
                SELECT UsuInicioSesion, UsuNombres, UsuApellidos,
                       UsuEstatus, UsuClave,
                       ISNULL(UsuIntentosClave, 0),
                       UsuUltimoIntento
                FROM   UsuarioSistema
                WHERE  UsuInicioSesion = @u", cn))
            {
                cmd.Parameters.AddWithValue("@u", req.Usuario.Trim());
                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return Json(new { ok = false, error = "Usuario o contrasena incorrectos" });

                login         = rd.GetString(0);
                nombres       = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim();
                apellidos     = rd.IsDBNull(2) ? "" : rd.GetString(2).Trim();
                estatus       = !rd.IsDBNull(3) && Convert.ToBoolean(rd.GetValue(3));
                claveDB       = rd.IsDBNull(4) ? "" : rd.GetString(4).Trim().ToUpper();
                intentos      = rd.IsDBNull(5) ? 0 : Convert.ToInt32(rd.GetValue(5));
                ultimoIntento = rd.IsDBNull(6) ? null : rd.GetDateTime(6);
            }

            // ── Verificar estatus ─────────────────────────────────────────────
            if (!estatus)
                return Json(new { ok = false, error = "Usuario inactivo. Contacte al administrador" });

            // ── Verificar bloqueo por intentos ────────────────────────────────
            if (intentos >= MAX_INTENTOS && ultimoIntento.HasValue)
            {
                var minsPasados = (DateTime.Now - ultimoIntento.Value).TotalMinutes;
                if (minsPasados < BLOQUEO_MINS)
                {
                    var restMin = (int)(BLOQUEO_MINS - minsPasados) + 1;
                    return Json(new { ok = false, error = $"Cuenta bloqueada. Intente en {restMin} min" });
                }
                // Periodo vencido — resetear contador
                try
                {
                    await using var cmdRst = new SqlCommand(
                        "UPDATE UsuarioSistema SET UsuIntentosClave = 0 WHERE UsuInicioSesion = @u", cn);
                    cmdRst.Parameters.AddWithValue("@u", req.Usuario.Trim());
                    await cmdRst.ExecuteNonQueryAsync();
                }
                catch { /* Ventas puede no tener permiso UPDATE — ignorar */ }
                intentos = 0;
            }

            // ── Comparar contrasena (MD5 UTF-8 en mayusculas) ─────────────────
            if (Md5Upper(req.Clave) != claveDB)
            {
                try
                {
                    await using var cmdFail = new SqlCommand(@"
                        UPDATE UsuarioSistema
                        SET    UsuIntentosClave = ISNULL(UsuIntentosClave, 0) + 1,
                               UsuUltimoIntento = GETDATE()
                        WHERE  UsuInicioSesion = @u", cn);
                    cmdFail.Parameters.AddWithValue("@u", req.Usuario.Trim());
                    await cmdFail.ExecuteNonQueryAsync();
                }
                catch { /* sin permiso UPDATE — ignorar */ }

                var restantes = MAX_INTENTOS - intentos - 1;
                var msg = restantes switch
                {
                    <= 0   => $"Cuenta bloqueada por {BLOQUEO_MINS} minutos",
                    1 or 2 => $"Usuario o contrasena incorrectos ({restantes} intento{(restantes > 1 ? "s" : "")} restante{(restantes > 1 ? "s" : "")})",
                    _      => "Usuario o contrasena incorrectos"
                };
                return Json(new { ok = false, error = msg });
            }

            // ── Login exitoso — resetear contador ─────────────────────────────
            try
            {
                await using var cmdOk = new SqlCommand(@"
                    UPDATE UsuarioSistema
                    SET    UsuIntentosClave      = 0,
                           UsuFechaActualizacion = GETDATE()
                    WHERE  UsuInicioSesion = @u", cn);
                cmdOk.Parameters.AddWithValue("@u", req.Usuario.Trim());
                await cmdOk.ExecuteNonQueryAsync();
            }
            catch { /* sin permiso UPDATE — el login sigue siendo valido */ }

            HttpContext.Session.SetString("user_login",  login!);
            HttpContext.Session.SetString("user_nombre", $"{nombres} {apellidos}".Trim());

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── GET /account/logout ────────────────────────────────────────────────────
    [HttpGet("logout")]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}

public record LoginRequest(string? Usuario, string? Clave);
