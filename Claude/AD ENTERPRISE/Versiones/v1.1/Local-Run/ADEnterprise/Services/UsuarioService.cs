using ADEnterprise.Models;
using BC = BCrypt.Net.BCrypt;

namespace ADEnterprise.Services;

public class UsuarioService
{
    private readonly GoogleSheetsService _gs;

    public UsuarioService(GoogleSheetsService gs) => _gs = gs;

    public async Task<List<Usuario>> ObtenerTodosAsync()
    {
        var filas = await _gs.LeerAsync(GoogleSheetsService.HOJA_USUARIOS);
        return filas.Skip(1) // saltar encabezado
            .Where(f => f.Count >= 6)
            .Select(MapearUsuario)
            .ToList();
    }

    public async Task<Usuario?> BuscarPorUsuarioAsync(string nombreUsuario)
    {
        var todos = await ObtenerTodosAsync();
        return todos.FirstOrDefault(u =>
            u.NombreUsuario.Equals(nombreUsuario, StringComparison.OrdinalIgnoreCase)
            && u.Estado == EstadoUsuario.Activo);
    }

    public async Task<bool> ValidarCredencialesAsync(string usuario, string password)
    {
        var u = await BuscarPorUsuarioAsync(usuario);
        if (u == null) return false;
        return BC.Verify(password, u.PasswordHash);
    }

    public async Task CrearAsync(Usuario u, string password)
    {
        u.Id = Guid.NewGuid().ToString("N")[..8].ToUpper();
        u.PasswordHash = BC.HashPassword(password);
        u.FechaCreacion = DateTime.Now;

        await _gs.AgregarFilaAsync(GoogleSheetsService.HOJA_USUARIOS, new object[]
        {
            u.Id, u.NombreCompleto, u.NombreUsuario, u.PasswordHash,
            u.Correo, u.Perfil.ToString(), u.Estado.ToString(),
            u.FechaCreacion.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    public async Task ActualizarAsync(Usuario u)
    {
        var filas = await _gs.LeerAsync(GoogleSheetsService.HOJA_USUARIOS);
        for (int i = 1; i < filas.Count; i++)
        {
            if (GoogleSheetsService.Str(filas[i], 0) == u.Id)
            {
                await _gs.ActualizarFilaAsync(GoogleSheetsService.HOJA_USUARIOS, i + 1,
                    new object[]
                    {
                        u.Id, u.NombreCompleto, u.NombreUsuario, u.PasswordHash,
                        u.Correo, u.Perfil.ToString(), u.Estado.ToString(),
                        u.FechaCreacion.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                return;
            }
        }
    }

    /// <summary>Crea el admin inicial si no existe ningún usuario.</summary>
    public async Task SeedAdminInicialAsync()
    {
        var todos = await ObtenerTodosAsync();
        if (todos.Any()) return;

        await CrearAsync(new Usuario
        {
            NombreCompleto = "Administrador",
            NombreUsuario  = "admin",
            Correo         = "",
            Perfil         = PerfilUsuario.Administrador,
            Estado         = EstadoUsuario.Activo
        }, "Admin2024!");
    }

    // ─── Mapeo ────────────────────────────────────────────────────────────────

    private static Usuario MapearUsuario(IList<object> f) => new()
    {
        Id             = GoogleSheetsService.Str(f, 0),
        NombreCompleto = GoogleSheetsService.Str(f, 1),
        NombreUsuario  = GoogleSheetsService.Str(f, 2),
        PasswordHash   = GoogleSheetsService.Str(f, 3),
        Correo         = GoogleSheetsService.Str(f, 4),
        Perfil         = Enum.TryParse<PerfilUsuario>(GoogleSheetsService.Str(f, 5), out var p)
                             ? p : PerfilUsuario.Consultas,
        Estado         = Enum.TryParse<EstadoUsuario>(GoogleSheetsService.Str(f, 6), out var e)
                             ? e : EstadoUsuario.Activo,
        FechaCreacion  = GoogleSheetsService.Fecha(f, 7) ?? DateTime.MinValue
    };
}
