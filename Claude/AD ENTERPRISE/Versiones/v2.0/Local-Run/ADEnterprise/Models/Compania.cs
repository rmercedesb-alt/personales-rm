namespace ADEnterprise.Models;

public class Compania
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Rnc { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public int CondicionPagoDias { get; set; } = 30;
    public bool Activo { get; set; } = true;
}
