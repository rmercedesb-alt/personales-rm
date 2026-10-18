namespace ADEnterprise.Models;

public class Usuario
{
    public string Id { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string NombreUsuario { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public PerfilUsuario Perfil { get; set; } = PerfilUsuario.Consultas;
    public EstadoUsuario Estado { get; set; } = EstadoUsuario.Activo;
    public DateTime FechaCreacion { get; set; } = DateTime.Now;

    public bool EsAdministrador => Perfil == PerfilUsuario.Administrador;
    public bool EsOperativo => Perfil == PerfilUsuario.Operativo;
    public bool SoloConsultas => Perfil == PerfilUsuario.Consultas;
}
