namespace OptimizadorRutas.Models;

public class RepresentanteInfo
{
    public string RepCodigo  { get; set; } = "";
    public string RepNombre  { get; set; } = "";
    public int    NroClientes { get; set; }

    public string Display => $"{RepCodigo} — {RepNombre} ({NroClientes} cli)";
}
