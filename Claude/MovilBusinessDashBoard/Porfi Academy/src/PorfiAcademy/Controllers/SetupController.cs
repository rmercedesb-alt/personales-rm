using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PorfiAcademy.Services;

namespace PorfiAcademy.Controllers;

public class SetupViewModel
{
    [Required(ErrorMessage = "Escribe el correo del administrador.")]
    [EmailAddress(ErrorMessage = "Correo no válido.")]
    [Display(Name = "Correo del administrador")]
    public string AdminEmail { get; set; } = "";

    [Required(ErrorMessage = "Escribe una contraseña.")]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string AdminPassword { get; set; } = "";

    [DataType(DataType.Password)]
    [Compare(nameof(AdminPassword), ErrorMessage = "Las contraseñas no coinciden.")]
    [Display(Name = "Confirmar contraseña")]
    public string ConfirmPassword { get; set; } = "";

    [Required(ErrorMessage = "Pega el Token ID de Mux.")]
    [Display(Name = "Mux Token ID")]
    public string MuxTokenId { get; set; } = "";

    [Required(ErrorMessage = "Pega el Token Secret de Mux.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mux Token Secret")]
    public string MuxTokenSecret { get; set; } = "";

    [Display(Name = "Guardar sin verificar con Mux")]
    public bool SkipVerify { get; set; }

    [Display(Name = "Código de instalación")]
    public string? SetupCode { get; set; }
}

public class SetupController : Controller
{
    private readonly ISecretStore _secrets;
    private readonly MuxService _mux;
    private readonly UserManager<IdentityUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly IConfiguration _config;

    public SetupController(ISecretStore secrets, MuxService mux, UserManager<IdentityUser> users,
        RoleManager<IdentityRole> roles, IConfiguration config)
    {
        _secrets = secrets;
        _mux = mux;
        _users = users;
        _roles = roles;
        _config = config;
    }

    // Seguridad: la instalación solo se permite desde el propio servidor (localhost) o con el código "Setup:Code" configurado.
    private bool IsLocal => IPAddress.IsLoopback(HttpContext.Connection.RemoteIpAddress ?? IPAddress.None);
    private string? ConfiguredCode => _config["Setup:Code"];

    [HttpGet("/Setup")]
    public IActionResult Index()
    {
        ViewBag.NeedsCode = !IsLocal;
        return View(new SetupViewModel());
    }

    [HttpPost("/Setup")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SetupViewModel model)
    {
        ViewBag.NeedsCode = !IsLocal;

        if (!IsLocal && (string.IsNullOrEmpty(ConfiguredCode) || model.SetupCode != ConfiguredCode))
        {
            ModelState.AddModelError("", "Instalación no autorizada desde esta ubicación. Instala desde el servidor o configura un código de instalación.");
            return View(model);
        }
        if (!ModelState.IsValid) return View(model);

        var credentials = new MuxCredentials { TokenId = model.MuxTokenId.Trim(), TokenSecret = model.MuxTokenSecret.Trim() };
        if (!model.SkipVerify)
        {
            var (ok, message) = await _mux.TestAsync(credentials);
            if (!ok)
            {
                ModelState.AddModelError(nameof(model.MuxTokenSecret), message + " Revisa los valores o marca «Guardar sin verificar».");
                return View(model);
            }
        }

        if (!await _roles.RoleExistsAsync("Admin"))
            await _roles.CreateAsync(new IdentityRole("Admin"));

        var admin = await _users.FindByEmailAsync(model.AdminEmail);
        if (admin is null)
        {
            admin = new IdentityUser { UserName = model.AdminEmail, Email = model.AdminEmail, EmailConfirmed = true };
            var created = await _users.CreateAsync(admin, model.AdminPassword);
            if (!created.Succeeded)
            {
                foreach (var e in created.Errors) ModelState.AddModelError(nameof(model.AdminPassword), e.Description);
                return View(model);
            }
        }
        await _users.AddToRoleAsync(admin, "Admin");

        _secrets.SaveMux(credentials); // al guardar, la instalación queda completa
        TempData["Info"] = "Instalación completada. Ya puedes iniciar sesión como administrador.";
        return Redirect("/Identity/Account/Login");
    }
}
