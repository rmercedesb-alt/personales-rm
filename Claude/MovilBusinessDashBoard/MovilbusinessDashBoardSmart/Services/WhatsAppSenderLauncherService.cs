using System.Diagnostics;
using System.Net.Http;

namespace DashVisitas.Services;

// ── Auto-arranque del sidecar "WhatsAppSender" ──────────────────────────────────────
// Pedido explícito del usuario 2026-09-23: "¿por qué no incluyes que levantemos ese
// servicio con el proyecto?" — hasta ahora había que abrir `node server.js` a mano cada
// vez, y si se te olvidaba, "Enviar por WhatsApp" fallaba con "el equipo de destino
// denegó la conexión" (nada escuchando en el puerto).
//
// Al arrancar el Dashboard, este servicio:
//   1) revisa si el sidecar YA está corriendo (GET /status con timeout corto);
//   2) si no responde, lo levanta como proceso DETACHED (UseShellExecute=true, su propia
//      ventana de consola — igual que INICIAR_WHATSAPP_SENDER.bat) apuntando a la carpeta
//      WhatsAppSender/ (hermana de esta, configurable).
//
// "Detached" a propósito: si quedara como proceso HIJO del Dashboard, se moriría cada vez
// que IIS recicla el app pool o que se reinicia CORRER_LOCAL.bat — tumbando la sesión de
// WhatsApp Web vinculada en cada recycle. Detached, sobrevive a eso (aunque NO sobrevive
// un reinicio completo de Windows — para eso, en el servidor de producción, sigue siendo
// mejor registrarlo aparte como Servicio de Windows con nssm; este auto-arranque es sobre
// todo para desarrollo local y como red de seguridad si el servicio de Windows no está
// configurado todavía).
//
// Nunca bloquea ni tumba el arranque del Dashboard: cualquier fallo (carpeta no existe,
// "node" no está en el PATH, etc.) solo se loguea.
public class WhatsAppSenderLauncherService(IConfiguration config, ILogger<WhatsAppSenderLauncherService> log, IHostEnvironment env) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        bool activo = bool.TryParse(config["Smtp:WhatsApp:Activo"], out var a) && a;
        if (!activo) { log.LogInformation("WhatsAppSender: auto-arranque saltado (Smtp:WhatsApp:Activo=false)."); return; }

        bool autoStart = !bool.TryParse(config["Smtp:WhatsApp:SidecarAutoStart"], out var az) || az; // default true
        if (!autoStart) { log.LogInformation("WhatsAppSender: auto-arranque deshabilitado (Smtp:WhatsApp:SidecarAutoStart=false) — se asume que ya corre como Servicio de Windows aparte."); return; }

        var sidecarUrl = (config["Smtp:WhatsApp:SidecarUrl"] ?? "http://localhost:5099").TrimEnd('/');

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = await http.GetAsync($"{sidecarUrl}/status", stop);
            if (resp.IsSuccessStatusCode) { log.LogInformation("WhatsAppSender ya está corriendo en {Url} — no hace falta levantarlo.", sidecarUrl); return; }
        }
        catch
        {
            // Sin respuesta = no está corriendo (o está caído) -> se intenta levantar abajo.
        }

        var carpeta = config["Smtp:WhatsApp:SidecarPath"];
        if (string.IsNullOrWhiteSpace(carpeta))
            carpeta = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "WhatsAppSender"));

        if (!Directory.Exists(carpeta) || !File.Exists(Path.Combine(carpeta, "server.js")))
        {
            log.LogWarning("WhatsAppSender: no se encontró server.js en '{Carpeta}' — no se pudo auto-arrancar. Levántalo a mano (node server.js) o ajusta Smtp:WhatsApp:SidecarPath en appsettings.json.", carpeta);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "server.js",
                WorkingDirectory = carpeta,
                UseShellExecute = true, // ventana propia, DETACHED del Dashboard a propósito (ver comentario arriba)
                WindowStyle = ProcessWindowStyle.Normal,
            };
            Process.Start(psi);
            log.LogInformation("WhatsAppSender: auto-arrancado desde '{Carpeta}' (ventana propia).", carpeta);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "WhatsAppSender: no se pudo auto-arrancar (¿'node' no está en el PATH del sistema?). Levántalo a mano: cd \"{Carpeta}\" && node server.js", carpeta);
        }
    }
}
