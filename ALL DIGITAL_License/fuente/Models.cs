namespace MfSoftLicense;

// Lista canónica de los módulos del Dashboard Smart. Las CLAVES deben coincidir EXACTAMENTE
// con las de Services/LicenseService.cs y los [RequiereModulo("...")] del proyecto
// DashSmart. Si algún día se agrega un módulo nuevo al sistema, agregarlo aquí también.
public static class Mod
{
    public static readonly (string Clave, string Nombre)[] Todos =
    [
        ("torre", "Torre de Control"),
        ("visitas", "Visitas"),
        ("geo", "Geolocalizaciones"),
        ("seguimientos", "Seguimientos Semanales"),
        ("productividad", "Productividad"),
        ("historicos", "Históricos"),
        ("supervision", "Supervisión Dinámica"),
        ("presupuestos", "Presupuestos"),
        ("presupuestosGestion", "Presupuestos — Gestión"),
    ];
    public static readonly HashSet<string> Claves = Todos.Select(m => m.Clave).ToHashSet();
}

public record Emision(string Fecha, string Expira, List<string> Modulos, string Archivo, string? Huella = null);

public class Cliente
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nombre { get; set; } = "";
    public string Contacto { get; set; } = "";
    public string Notas { get; set; } = "";
    // Huella del servidor al que está atada la licencia vigente (null = sin atar, funciona en cualquier servidor).
    public string? Huella { get; set; }
    public string Creado { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
    public List<Emision> Emisiones { get; set; } = new();
}

public record LoginReq(string? Clave);
public record ConfigReq(string? Clave, string? Confirmar, string? Modo, string? Ruta);
public record CambiarClaveReq(string? Actual, string? Nueva, string? Confirmar);
public record EmitirReq(string? Id, string? Nombre, string? Contacto, string? Notas, string? Expira, List<string>? Modulos, string? Huella = null);

// Freno básico contra adivinar la clave por fuerza bruta.
public static class Guardia
{
    private static readonly object Lk = new();
    private static int _fallos;
    private static DateTime _bloqueadoHasta = DateTime.MinValue;
    public static int SegundosBloqueo() { lock (Lk) { var s = (int)Math.Ceiling((_bloqueadoHasta - DateTime.UtcNow).TotalSeconds); return s > 0 ? s : 0; } }
    public static void Fallo() { lock (Lk) { if (++_fallos >= 5) { _fallos = 0; _bloqueadoHasta = DateTime.UtcNow.AddSeconds(30); } } }
    public static void Exito() { lock (Lk) { _fallos = 0; } }
}
