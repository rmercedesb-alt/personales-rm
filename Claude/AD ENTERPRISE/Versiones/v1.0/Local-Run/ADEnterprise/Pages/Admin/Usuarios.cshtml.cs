using ADEnterprise.Models;
using ADEnterprise.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ADEnterprise.Pages.Admin;

public class UsuariosModel : PageModel
{
    private readonly UsuarioService _usuarios;
    public UsuariosModel(UsuarioService usuarios) => _usuarios = usuarios;

    public List<Usuario> Usuarios { get; set; } = new();

    public async Task OnGetAsync()
    {
        SetViewData();
        Usuarios = await _usuarios.ObtenerTodosAsync();
    }

    public async Task<IActionResult> OnPostCrearAsync(
        string NombreCompleto, string NombreUsuario, string Correo,
        string Password, string Perfil)
    {
        SetViewData();
        if (string.IsNullOrWhiteSpace(NombreCompleto) ||
            string.IsNullOrWhiteSpace(NombreUsuario) ||
            string.IsNullOrWhiteSpace(Password))
        {
            TempData["Error"] = "Complete todos los campos obligatorios.";
            return RedirectToPage();
        }

        // Verificar que no exista el usuario
        var existente = await _usuarios.BuscarPorUsuarioAsync(NombreUsuario);
        if (existente != null)
        {
            TempData["Error"] = $"El usuario '{NombreUsuario}' ya existe.";
            return RedirectToPage();
        }

        var nuevo = new Usuario
        {
            NombreCompleto = NombreCompleto,
            NombreUsuario  = NombreUsuario,
            Correo         = Correo ?? "",
            Perfil         = Enum.TryParse<PerfilUsuario>(Perfil, out var p) ? p : PerfilUsuario.Operativo,
            Estado         = EstadoUsuario.Activo
        };

        await _usuarios.CrearAsync(nuevo, Password);
        TempData["Exito"] = $"Usuario '{NombreCompleto}' creado exitosamente.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditarAsync(
        string Id, string NombreCompleto, string NombreUsuario,
        string Correo, string NuevoPassword, string Perfil)
    {
        SetViewData();
        var todos   = await _usuarios.ObtenerTodosAsync();
        var usuario = todos.FirstOrDefault(u => u.Id == Id);
        if (usuario == null)
        {
            TempData["Error"] = "Usuario no encontrado.";
            return RedirectToPage();
        }

        usuario.NombreCompleto = NombreCompleto;
        usuario.Correo         = Correo ?? "";
        usuario.Perfil         = Enum.TryParse<PerfilUsuario>(Perfil, out var p) ? p : usuario.Perfil;

        // Cambiar contraseña solo si se proporcionó una nueva
        if (!string.IsNullOrWhiteSpace(NuevoPassword))
            usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NuevoPassword);

        await _usuarios.ActualizarAsync(usuario);
        TempData["Exito"] = $"Usuario '{NombreCompleto}' actualizado correctamente.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleEstadoAsync(string id)
    {
        SetViewData();
        var todos   = await _usuarios.ObtenerTodosAsync();
        var usuario = todos.FirstOrDefault(u => u.Id == id);
        if (usuario != null)
        {
            usuario.Estado = usuario.Estado == EstadoUsuario.Activo
                ? EstadoUsuario.Inactivo
                : EstadoUsuario.Activo;
            await _usuarios.ActualizarAsync(usuario);
            TempData["Exito"] = $"Estado del usuario '{usuario.NombreCompleto}' actualizado.";
        }
        return RedirectToPage();
    }

    private void SetViewData()
    {
        ViewData["UsuarioNombre"] = User.FindFirst("NombreCompleto")?.Value;
        ViewData["UsuarioPerfil"] = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    }
}
