using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace ADEnterprise.Pages;

[AllowAnonymous]
public class SetupModel : PageModel
{
    private readonly SetupService   _setup;
    private readonly UsuarioService _usuarios;
    private readonly IWebHostEnvironment _env;

    public SetupModel(SetupService setup, UsuarioService usuarios, IWebHostEnvironment env)
    {
        _setup    = setup;
        _usuarios = usuarios;
        _env      = env;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "El nombre de la empresa es requerido")]
        public string NombreEmpresa { get; set; } = string.Empty;

        public IFormFile? LogoFile { get; set; }

        [Required(ErrorMessage = "El ID del Spreadsheet es requerido")]
        public string SpreadsheetId { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre del administrador es requerido")]
        public string AdminNombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El usuario es requerido")]
        public string AdminUsuario { get; set; } = string.Empty;

        public string AdminCorreo { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es requerida")]
        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
        public string AdminPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirme la contraseña")]
        [Compare("AdminPassword", ErrorMessage = "Las contraseñas no coinciden")]
        public string AdminPasswordConfirm { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        // Si ya está configurado, redirigir al login
        if (_setup.EstaConfigurado())
            return RedirectToPage("/Login");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        try
        {
            // 1. Guardar configuración con el SpreadsheetId proporcionado
            var credPath = Path.Combine(_env.ContentRootPath, "credentials.json");
            if (!System.IO.File.Exists(credPath))
            {
                ErrorMessage = "No se encontró el archivo credentials.json. Colóquelo en la carpeta del proyecto.";
                return Page();
            }

            var spreadsheetId = Input.SpreadsheetId.Trim();
            var nombreArchivo  = $"AD Enterprise.{Input.NombreEmpresa}";

            // Inicializar hojas maestras en el Spreadsheet existente
            await _setup.InicializarSpreadsheetExistenteAsync(
                spreadsheetId, Input.NombreEmpresa, credPath);

            // Guardar configuración
            await _setup.GuardarConfiguracionPublicaAsync(
                Input.NombreEmpresa, spreadsheetId, nombreArchivo);

            // 2. Guardar logo si se subió
            if (Input.LogoFile != null && Input.LogoFile.Length > 0)
            {
                var imgPath = Path.Combine(_env.WebRootPath, "images", "logo-empresa.png");
                Directory.CreateDirectory(Path.GetDirectoryName(imgPath)!);
                using var stream = new FileStream(imgPath, FileMode.Create);
                await Input.LogoFile.CopyToAsync(stream);
            }

            // 3. Crear el usuario Administrador inicial
            var admin = new Usuario
            {
                NombreCompleto = Input.AdminNombre,
                NombreUsuario  = Input.AdminUsuario,
                Correo         = Input.AdminCorreo,
                Perfil         = PerfilUsuario.Administrador,
                Estado         = EstadoUsuario.Activo
            };
            await _usuarios.CrearAsync(admin, Input.AdminPassword);

            TempData["Exito"] = $"¡Sistema configurado exitosamente para {Input.NombreEmpresa}! " +
                                 $"Archivo creado en Google Drive: AD Enterprise.{Input.NombreEmpresa}";
            return RedirectToPage("/Login");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al configurar: {ex.Message}. " +
                           "Verifique que credentials.json sea válido y tenga acceso a Google Drive.";
            return Page();
        }
    }
}
