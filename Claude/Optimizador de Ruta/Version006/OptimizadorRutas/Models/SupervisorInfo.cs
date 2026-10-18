namespace OptimizadorRutas.Models;

public class SupervisorInfo
{
    public string RepCodigo { get; set; } = "";
    public string RepNombre { get; set; } = "";

    public string Display => $"{RepCodigo} — {RepNombre}";
}
