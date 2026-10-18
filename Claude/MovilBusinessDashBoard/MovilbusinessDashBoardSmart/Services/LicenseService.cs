using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DashVisitas.Services;

// ── Licencia del producto (fecha de vencimiento + módulos activos/inactivos) ────────
// Pedido explícito del usuario (2026-09-30): el sistema se instala en varios servidores
// de distintos clientes, y la fecha de vencimiento + los 9 módulos deben poder activarse/
// desactivarse "escondido" en un archivo de configuración, pero de forma que SOLO AM
// COMERCIAL pueda modificarlo — no basta con esconder el archivo, cualquiera con acceso
// al servidor lo podría editar.
//
// Por eso NO es un valor en appsettings.json ni una variable de entorno (ambos son texto
// plano, editables por cualquier administrador del servidor del cliente). Es un archivo
// FIRMADO digitalmente (RSA-2048 + SHA256): App_Data/license.lic contiene un payload
// (JSON en Base64 con la fecha de expiración y los flags de módulo) + una firma. Esta
// clase solo trae la llave PÚBLICA (abajo), que puede VERIFICAR que la firma es válida,
// pero no puede generarla — solo quien tiene la llave PRIVADA (ver LicenseTool/, carpeta
// hermana de la solución, nunca desplegada) puede emitir o renovar una licencia. Si el
// archivo falta, está corrupto, o la firma no coincide (p.ej. alguien lo editó a mano),
// se trata como licencia inválida — "fail closed", nunca "fail open".
public class LicenseService
{
    // Llave PÚBLICA del par RSA-2048 — generada una sola vez con
    // "dotnet run -- claves" en LicenseTool/. Esta llave es pública a propósito: con
    // ella se VERIFICA una firma, pero no se puede FIRMAR nada nuevo. La llave privada
    // correspondiente vive únicamente en la máquina de AM COMERCIAL, fuera de este repo.
    private const string PublicKeyXml = "<RSAKeyValue><Modulus>zMFmPaZJocNcc2XrZODBk0n6kcF7BxQuCgcCeoUzuo6ZvyFJWjTej12NZuDkus59obYChtHk89xph150FnHvcHRnOxtgDn41GQv4rW8tNFtTyhZ6lXWl9NFqX223Cs1rHTlm/JJggGVxvnPt12oS93OKOszLh2sER+aPcpY/Luy0PBM/3NSi903NyDGCEhS1wDNADAskVJFr843T0IFa6cuDsPzKG+l7F7AzeMUcnGuWAJT0rvdhpLN1E0lBGZBIX15DsWQCSCjXpabq91YT91qlho8ib8vbbfrFExHTF+Pw4COEmCWNa12gIYWCUMRePgdPXIR6jjozrnScMI34lQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    // Lista canónica de los 9 módulos — debe coincidir EXACTAMENTE con las claves que
    // emite LicenseTool y con los [RequiereModulo("...")] de los controllers.
    public static readonly string[] Modulos =
    [
        "torre", "visitas", "geo", "seguimientos", "productividad",
        "historicos", "supervision", "presupuestos", "presupuestosGestion"
    ];

    private readonly string _rutaLicencia;
    private readonly ILogger<LicenseService> _log;
    private readonly object _lock = new();
    private static readonly TimeSpan TtlCache = TimeSpan.FromMinutes(10);

    private LicenseInfo? _info;
    private bool _firmaValida;
    private DateTime _ultimaLectura = DateTime.MinValue;

    // ── Huella del servidor ─────────────────────────────────────────────────────────
    // Pedido del usuario (2026-10-04): una licencia puede ir "atada" a un servidor (campo
    // opcional "huella" dentro del payload FIRMADO) para que copiar la carpeta a otra máquina
    // no sirva; si la licencia NO trae huella, funciona en cualquier servidor (como antes).
    // La huella sale del MachineGuid de Windows (estable ante actualizaciones del sistema;
    // cambia con un servidor nuevo / reinstalación de Windows → ahí se emite licencia nueva).
    // Límites honestos: una VM clonada comparte el MachineGuid, y un administrador del
    // servidor que sepa lo que hace podría falsear ese valor — sube el esfuerzo, no es infalible.
    // El sistema SIEMPRE se instala y arranca (muestra su huella para que se la pasen al
    // administrador); solo se bloquea si la licencia trae huella y no coincide con este equipo.
    public string? HuellaServidor { get; }

    public LicenseService(IWebHostEnvironment env, ILogger<LicenseService> log)
    {
        _rutaLicencia = Path.Combine(env.ContentRootPath, "App_Data", "license.lic");
        _log = log;
        HuellaServidor = CalcularHuella();
        GuardarHuella(env.ContentRootPath);
        Recargar();
    }

    private static string? CalcularHuella()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var k = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = k?.GetValue("MachineGuid") as string;
            if (string.IsNullOrWhiteSpace(guid)) return null;
            var h = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("DashSmart|" + guid.Trim().ToLowerInvariant())));
            return string.Join("-", Enumerable.Range(0, 5).Select(i => h.Substring(i * 4, 4)));
        }
        catch { return null; }
    }

    private static string NormalizarHuella(string? s) =>
        new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    // Deja la huella en App_Data\huella_servidor.txt (para que el instalador y el
    // administrador la puedan leer sin entrar al sistema). Mejor esfuerzo: si no hay permiso
    // de escritura, simplemente no se guarda (la huella igual sale en pantalla y en /api/licencia).
    private void GuardarHuella(string contentRoot)
    {
        try
        {
            if (HuellaServidor is null) return;
            var dir = Path.Combine(contentRoot, "App_Data");
            Directory.CreateDirectory(dir);
            var ruta = Path.Combine(dir, "huella_servidor.txt");
            var txt = "ID de este servidor (huella): " + HuellaServidor + Environment.NewLine +
                      "Entregue este ID al administrador del sistema si su licencia debe quedar atada a este servidor." + Environment.NewLine;
            if (!File.Exists(ruta) || File.ReadAllText(ruta) != txt) File.WriteAllText(ruta, txt);
        }
        catch (Exception ex) { _log.LogInformation(ex, "No se pudo guardar la huella del servidor"); }
    }

    private void Recargar()
    {
        lock (_lock)
        {
            _info = null;
            _firmaValida = false;
            try
            {
                if (!File.Exists(_rutaLicencia))
                {
                    _log.LogWarning("Licencia: no se encontró {Ruta}", _rutaLicencia);
                    return;
                }
                var raw = File.ReadAllText(_rutaLicencia);
                var archivo = JsonSerializer.Deserialize<LicenseFile>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (archivo?.Payload is null || archivo.Signature is null)
                {
                    _log.LogWarning("Licencia: archivo con formato inválido");
                    return;
                }
                var payloadBytes = Convert.FromBase64String(archivo.Payload);
                var firmaBytes = Convert.FromBase64String(archivo.Signature);

                using var rsa = RSA.Create();
                rsa.FromXmlString(PublicKeyXml);
                _firmaValida = rsa.VerifyData(payloadBytes, firmaBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                if (!_firmaValida)
                {
                    _log.LogWarning("Licencia: firma inválida (el archivo no coincide con lo emitido, o fue alterado)");
                    return;
                }
                var json = Encoding.UTF8.GetString(payloadBytes);
                _info = JsonSerializer.Deserialize<LicenseInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Licencia: error leyendo/verificando {Ruta}", _rutaLicencia);
                _info = null;
                _firmaValida = false;
            }
            finally
            {
                _ultimaLectura = DateTime.UtcNow;
            }
        }
    }

    // Re-lee el archivo si pasaron más de 10 min desde la última lectura — así una
    // licencia renovada (se copia el .lic nuevo encima) se aplica sola, sin reiniciar
    // el servicio/IIS, pero sin pagar el costo de verificar la firma en cada request.
    private void RefrescarSiCorresponde()
    {
        if (DateTime.UtcNow - _ultimaLectura > TtlCache) Recargar();
    }

    public DateOnly? Expira
    {
        get
        {
            RefrescarSiCorresponde();
            return _info?.Expira is string s && DateOnly.TryParse(s, out var d) ? d : null;
        }
    }

    public string? Cliente { get { RefrescarSiCorresponde(); return _info?.Cliente; } }

    public enum EstadoLicencia { Valida, SinLicencia, Vencida, OtroServidor }

    // ¿La licencia vigente está atada a un servidor? (trae "huella" en el payload firmado)
    public bool AtadaAServidor { get { RefrescarSiCorresponde(); return _firmaValida && !string.IsNullOrWhiteSpace(_info?.Huella); } }

    public EstadoLicencia Estado
    {
        get
        {
            RefrescarSiCorresponde();
            if (!_firmaValida || _info is null) return EstadoLicencia.SinLicencia;
            var exp = Expira;
            if (exp is null) return EstadoLicencia.SinLicencia;
            // Licencia atada: tiene que coincidir con la huella de ESTE equipo. Si no se pudo
            // calcular la huella local, también se rechaza (fail closed).
            if (!string.IsNullOrWhiteSpace(_info.Huella) &&
                (HuellaServidor is null || NormalizarHuella(_info.Huella) != NormalizarHuella(HuellaServidor)))
                return EstadoLicencia.OtroServidor;
            return exp.Value >= DateOnly.FromDateTime(DateTime.Today) ? EstadoLicencia.Valida : EstadoLicencia.Vencida;
        }
    }

    public bool EsValida => Estado == EstadoLicencia.Valida;

    public bool ModuloActivo(string clave)
    {
        if (!EsValida) return false;
        return _info?.Modulos is not null && _info.Modulos.TryGetValue(clave, out var activo) && activo;
    }

    public Dictionary<string, bool> ModulosActivos()
    {
        if (!EsValida) return Modulos.ToDictionary(m => m, _ => false);
        return Modulos.ToDictionary(m => m, m => _info!.Modulos?.TryGetValue(m, out var activo) == true && activo);
    }

    public static string PaginaNoDisponibleHtml(LicenseService licencia)
    {
        var estado = licencia.Estado;
        var detalle = estado switch
        {
            EstadoLicencia.OtroServidor => "Esta licencia fue emitida para otro servidor y no puede usarse en este equipo.",
            EstadoLicencia.Vencida when licencia.Expira is { } exp => $"La licencia de este sistema venció el {exp:dd/MM/yyyy}.",
            _ => "Este sistema no tiene una licencia válida instalada."
        };
        var accion = estado == EstadoLicencia.OtroServidor ? "Contacte al administrador del sistema." : "Contacte al administrador del sistema para renovarla.";
        var idServidor = licencia.HuellaServidor is { } h
            ? $"<br><span style=\"font-size:11px;color:#8A978D\">ID de este servidor: <code style=\"font-family:Consolas,monospace\">{h}</code></span>"
            : "";
        return $$"""
        <!DOCTYPE html><html lang="es"><head><meta charset="UTF-8">
        <title>Sistema no disponible</title>
        <style>
        body{background:#F7F9F7;color:#1F2A22;font-family:'Segoe UI',Tahoma,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}
        .box{background:#FFFFFF;border:1px solid #D9E2D9;border-radius:14px;padding:40px;max-width:460px;text-align:center}
        .icon{font-size:42px;margin-bottom:12px}
        h1{font-size:17px;margin:0 0 10px}
        p{font-size:13px;color:#5C6B5F;line-height:1.6;margin:0}
        </style></head><body>
        <div class="box"><div class="icon">&#128274;</div>
        <h1>Sistema no disponible</h1>
        <p>{{detalle}}<br>{{accion}}{{idServidor}}</p>
        </div></body></html>
        """;
    }

    public static string PaginaModuloNoDisponibleHtml(string modulo) => $$"""
        <!DOCTYPE html><html lang="es"><head><meta charset="UTF-8">
        <title>Módulo no disponible</title>
        <style>
        body{background:#F7F9F7;color:#1F2A22;font-family:'Segoe UI',Tahoma,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}
        .box{background:#FFFFFF;border:1px solid #D9E2D9;border-radius:14px;padding:40px;max-width:460px;text-align:center}
        .icon{font-size:42px;margin-bottom:12px}
        h1{font-size:17px;margin:0 0 10px}
        p{font-size:13px;color:#5C6B5F;line-height:1.6;margin:0}
        </style></head><body>
        <div class="box"><div class="icon">&#128683;</div>
        <h1>Módulo no disponible</h1>
        <p>Este módulo no está incluido en su licencia actual.<br>Contacte al administrador del sistema si desea activarlo.</p>
        </div></body></html>
        """;

    private record LicenseFile(string? Payload, string? Signature);
    private record LicenseInfo(string? Cliente, string? Emitido, string? Expira, Dictionary<string, bool>? Modulos, string? Huella = null);
}
