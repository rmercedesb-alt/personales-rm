namespace OptimizadorRutas.Models;

/// <summary>
/// Representa un cliente en la ruta de visita de un representante.
/// Mapea directamente la consulta SQL principal.
/// </summary>
public class ClienteRuta
{
    public string RepCodigo        { get; set; } = "";
    public string RepNombre        { get; set; } = "";
    public int    CliID            { get; set; }
    public string CliNombre        { get; set; } = "";
    public string CliCodigo        { get; set; } = "";
    public int    ClaID            { get; set; }
    public string ClaDescripcion   { get; set; } = "";
    public string RutSemana1       { get; set; } = "0000000";
    public string RutSemana2       { get; set; } = "0000000";
    public string RutSemana3       { get; set; } = "0000000";
    public string RutSemana4       { get; set; } = "0000000";
    public int    RutPosicion      { get; set; }
    public double Latitud          { get; set; }
    public double Longitud         { get; set; }
    public decimal CliPromedioCompra    { get; set; }
    public decimal CliVentasAnioActual  { get; set; }
    public int    CliEstatus       { get; set; }

    // Calculados en memoria
    public int    NuevaPosicion    { get; set; }   // posicion tras optimizar (múltiplo de 5)
    public int    PosicionEditada  { get; set; }   // editable por el usuario en la tabla
    public string? DiaVisita       { get; set; }   // dia detectado para la semana seleccionada

    // Frecuencia v006
    public string Frecuencia         { get; set; } = "semanal";           // "semanal" | "quincenal_1_3" | "quincenal_2_4"
    public string FrecuenciaActual   { get; set; } = "semanal";           // snapshot antes del análisis
    public string FrecuenciaProposed { get; set; } = "semanal";           // propuesta del análisis automático

    public bool TieneCoordenadas =>
        Latitud  != 0 && Longitud != 0 &&
        Latitud  >= -90  && Latitud  <= 90 &&
        Longitud >= -180 && Longitud <= 180;
}
