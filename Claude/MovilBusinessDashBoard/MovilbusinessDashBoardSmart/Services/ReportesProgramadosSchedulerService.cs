using Microsoft.Data.SqlClient;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using DashVisitas.Controllers;

namespace DashVisitas.Services;

// ── Envío programado GENÉRICO de reportes de "PDF Presentaciones" ──────────────────
// Reemplaza (2026-09-03) a la primera versión (CierreDiarioSchedulerService), que solo
// mandaba "Cierre Diario" a las 6pm fijo, con filtros por defecto — pedido explícito
// del usuario tras probarla: "sugiero que esta herramienta la pongamos en cada base de
// los reportes, pero que tengamos un entorno de cuál reporte queremos que se envíe y a
// qué hora... debemos tener un botón... guardar para enviar... decidir cuál reporte le
// envío a quién, porque tengo varios suplidores, categorías y quizás lo que quiera ver
// con uno es diferente a lo que quiero enviar a otro".
//
// Modelo: N filas en ReportesProgramados_RM (Reporte + FiltrosJSON + HoraEnvio propios),
// cada una con su lista de destinatarios en ReportesProgramadosDestinatarios_RM (apunta
// a la libreta general CierreDiarioDestinatarios_RM). El scheduler revisa CADA MINUTO
// (no "una vez al día a una hora fija" como antes) cuáles programaciones activas
// coinciden con la hora actual y no se han enviado hoy todavía.
//
// v1 sigue igual que antes: correo como HTML bien formateado (no adjunto PDF — la
// exportación a PDF es 100% del navegador, un job de fondo no tiene navegador).
// WhatsApp: mismo diseño no-bloqueante que antes, sin proveedor real conectado.
public class ReportesProgramadosSchedulerService(IConfiguration config, ILogger<ReportesProgramadosSchedulerService> log) : BackgroundService
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await RevisarYEnviarPendientes(); }
            catch (Exception ex) { log.LogWarning(ex, "Revisión de Reportes Programados falló"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); } catch { break; }
        }
    }

    // HoraEnvio ahora es una LISTA de horas separadas por coma (ej. "10:00,12:00,
    // 15:00,17:15" — pedido explícito 2026-09-03, un mismo reporte/filtro/
    // destinatarios enviado varias veces al día), así que el match ya no se puede
    // hacer en SQL con DATEPART sobre una columna `time` — se compara en C#, una
    // programación a la vez. El guardado "no reenviar" YA NO es "una vez por día"
    // (rompería los envíos siguientes del mismo día) sino "no reenviar dentro del
    // mismo minuto" (protege contra 2 ticks del reloj de fondo cayendo en el
    // mismo minuto exacto) — por eso compara contra UltimoEnvio con margen de
    // 1 minuto, no contra la fecha.
    private async Task RevisarYEnviarPendientes()
    {
        using var cn = NewConn();
        cn.Open();
        var ahora = DateTime.Now;
        var hhmmAhora = ahora.ToString("HH:mm");
        var pendientes = new List<int>();
        try
        {
            using var cmd = new SqlCommand("SELECT ProgID, HoraEnvio, UltimoEnvio FROM dbo.ReportesProgramados_RM WITH (NOLOCK) WHERE Activo=1", cn) { CommandTimeout = 30 };
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var horas = (rd.IsDBNull(1) ? "" : rd.GetString(1)).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (Array.IndexOf(horas, hhmmAhora) < 0) continue;
                var ultimoEnvio = rd.IsDBNull(2) ? (DateTime?)null : rd.GetDateTime(2);
                if (ultimoEnvio.HasValue && (ahora - ultimoEnvio.Value).TotalMinutes < 1) continue; // ya se disparó este mismo minuto
                pendientes.Add(rd.GetInt32(0));
            }
        }
        catch (Exception ex) { log.LogInformation(ex, "ReportesProgramados_RM aún no existe — nada que revisar."); return; }
        foreach (var progId in pendientes)
        {
            try { await EnviarProgramado(cn, config, log, progId); }
            catch (Exception ex) { log.LogWarning(ex, "Envío de la programación {ProgId} falló", progId); }
        }
    }

    public sealed record ResultadoEnvio(bool Ok, int Destinatarios, string? Motivo);

    // Genera + envía UNA programación específica — usado por el reloj (arriba) Y por el
    // botón manual "Enviar ahora" de cada fila en pantalla (ApiPdfPresController).
    internal static async Task<ResultadoEnvio> EnviarProgramado(SqlConnection cn, IConfiguration config, ILogger log, int progId)
    {
        string reporte, nombre; JsonElement filtros;
        using (var cmd = new SqlCommand("SELECT Reporte, Nombre, FiltrosJSON FROM dbo.ReportesProgramados_RM WHERE ProgID=@id", cn) { CommandTimeout = 15 })
        {
            cmd.Parameters.AddWithValue("@id", progId);
            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return new(false, 0, "La programación ya no existe.");
            reporte = rd.GetString(0); nombre = rd.GetString(1);
            filtros = rd.IsDBNull(2) ? JsonDocument.Parse("{}").RootElement : JsonSerializer.Deserialize<JsonElement>(rd.GetString(2));
        }

        var destinatarios = new List<(string nombre, string email, string? telefono)>();
        using (var cmd = new SqlCommand(@"
SELECT dd.Nombre, dd.Email, dd.Telefono FROM dbo.ReportesProgramadosDestinatarios_RM d
JOIN dbo.CierreDiarioDestinatarios_RM dd ON dd.DesID=d.DesID
WHERE d.ProgID=@id AND dd.Activo=1", cn) { CommandTimeout = 15 })
        {
            cmd.Parameters.AddWithValue("@id", progId);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) destinatarios.Add((rd.GetString(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2)));
        }
        if (destinatarios.Count == 0)
        {
            ActualizarResultado(cn, progId, false, "Sin destinatarios activos.");
            return new(false, 0, "Sin destinatarios activos.");
        }

        string? JProp(string p) => filtros.TryGetProperty(p, out var el) && el.ValueKind == JsonValueKind.Array
            ? string.Join(",", el.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrWhiteSpace(s))) : null;
        string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        dynamic resultado;
        try
        {
            resultado = ApiPdfPresController.GenerarReporte(cn, reporte,
                NullIfEmpty(JProp("suplidor")), NullIfEmpty(JProp("categoria")), NullIfEmpty(JProp("centros")),
                NullIfEmpty(JProp("canales")), NullIfEmpty(JProp("supervisores")), NullIfEmpty(JProp("divisiones")), NullIfEmpty(JProp("vendedores")));
        }
        catch (Exception ex)
        {
            ActualizarResultado(cn, progId, false, "Error generando el reporte: " + ex.Message);
            return new(false, 0, ex.Message);
        }

        string html = (string)ArmarHtmlCorreo(reporte, nombre, resultado);
        var asunto = $"{nombre} — MovilBusiness Dashboard ({DateTime.Today:dd/MM/yyyy})";
        var (ok, motivo) = EnviarPorCorreoDirecto(config, destinatarios.Select(d => d.email).ToList(), asunto, html);

        ActualizarResultado(cn, progId, ok, motivo);
        if (ok) log.LogInformation("Programación {ProgId} ({Nombre}) enviada a {N} destinatario(s).", progId, nombre, destinatarios.Count);
        else log.LogWarning("Programación {ProgId} ({Nombre}) no se pudo enviar — {Motivo}", progId, nombre, motivo);

        // WhatsApp — no bloqueante a propósito, mismo diseño de siempre.
        try { await EnviarPorWhatsApp(config, log, reporte, nombre, resultado, destinatarios); }
        catch (Exception ex) { log.LogWarning(ex, "Envío por WhatsApp falló (no bloquea el correo)."); }

        return new(ok, destinatarios.Count, motivo);
    }

    private static void ActualizarResultado(SqlConnection cn, int progId, bool ok, string? motivo)
    {
        try
        {
            using var cmd = new SqlCommand(ok
                ? "UPDATE dbo.ReportesProgramados_RM SET UltimoEnvio=GETDATE(), UltimoError=NULL WHERE ProgID=@id"
                : "UPDATE dbo.ReportesProgramados_RM SET UltimoError=@e WHERE ProgID=@id", cn) { CommandTimeout = 15 };
            cmd.Parameters.AddWithValue("@id", progId);
            if (!ok) cmd.Parameters.AddWithValue("@e", (object?)motivo ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch { /* no bloquea el resultado ya calculado */ }
    }

    // Reusado tanto por el envío programado (arriba) como por el envío AD-HOC del
    // reporte que el usuario tiene en pantalla (ApiPdfPresController.EnviarHtmlAdHoc)
    // — toma direcciones sueltas (no requiere que estén en la libreta).
    public static (bool ok, string? motivo) EnviarPorCorreoDirecto(IConfiguration config, List<string> destinatarios, string asunto, string html)
    {
        var host = config["Smtp:Host"]; var user = config["Smtp:User"]; var pass = config["Smtp:AppPassword"];
        var portStr = config["Smtp:Port"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            return (false, "Smtp no configurado en appsettings.json (Host/User/AppPassword vacíos).");
        int port = int.TryParse(portStr, out var p) ? p : 587;
        try
        {
            using var smtp = new SmtpClient(host, port) { EnableSsl = true, Credentials = new NetworkCredential(user, pass) };
            using var msg = new MailMessage { From = new MailAddress(user, "MovilBusiness Dashboard"), Subject = asunto, Body = html, IsBodyHtml = true };
            foreach (var email in destinatarios) msg.To.Add(new MailAddress(email));
            smtp.Send(msg);
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // Un solo HttpClient estático reusado (buena práctica .NET — evita agotar sockets),
    // apuntando SIEMPRE a localhost: el sidecar de WhatsApp nunca se expone a internet.
    private static readonly HttpClient _whatsAppHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    // Envío real por WhatsApp — 2026-09-19, reemplaza el stub anterior (no conectado a
    // ningún proveedor). Mecanismo elegido por el usuario: WhatsApp Web NO oficial vía
    // Baileys (Node.js), corriendo como sidecar HTTP local ("WhatsAppSender/", puerto
    // configurable). .NET sigue siendo el dueño de TODOS los datos/HTML (igual que ya lo
    // es del correo) — el sidecar solo renderiza el HTML a imagen (Puppeteer) y lo manda
    // por Baileys; no conoce nada de presupuestos/ventas.
    // No bloqueante: un fallo acá nunca debe tumbar el envío por correo (ver try/catch del
    // llamador) ni un destinatario fallido debe frenar a los demás.
    private static async Task EnviarPorWhatsApp(IConfiguration config, ILogger log, string reporte, string nombreProgramacion, dynamic resultado, List<(string nombre, string email, string? telefono)> destinatarios)
    {
        bool activo = bool.TryParse(config["Smtp:WhatsApp:Activo"], out var a) && a;
        if (!activo) { log.LogInformation("Envío por WhatsApp deshabilitado (Smtp:WhatsApp:Activo=false)."); return; }

        var conTelefono = destinatarios.Where(d => !string.IsNullOrWhiteSpace(d.telefono)).ToList();
        if (conTelefono.Count == 0) { log.LogInformation("Envío por WhatsApp: ningún destinatario de esta programación tiene teléfono registrado."); return; }

        if (reporte != "cierres_cierre_diario")
        {
            log.LogInformation("Envío por WhatsApp: reporte '{Reporte}' sin plantilla compacta todavía — solo 'cierres_cierre_diario' la tiene por ahora.", reporte);
            return;
        }

        await EnviarWhatsAppCierreDiario(config, log, resultado, conTelefono.Select(d => (d.nombre, (string?)d.telefono, (string?)null)).ToList(), null, new WhatsAppPuntos());
    }

    // Núcleo del envío por WhatsApp del Cierre Diario — reusado por el envío PROGRAMADO
    // (arriba, con destinatarios de la libreta y opciones por defecto) y por el envío
    // AD-HOC "on demand" (ApiPdfPresController.WhatsAppEnviarAhora, con 1 destino elegido
    // en el momento y opciones personalizadas) — pedido explícito 2026-09-20. Manda 2
    // mensajes en secuencia por destinatario: (1) texto de introducción, (2) la imagen con
    // el cierre de firma como caption — "que luego traiga las imágenes... debe tener un
    // cierre del mensaje".
    internal static async Task EnviarWhatsAppCierreDiario(IConfiguration config, ILogger log, dynamic resultado, List<(string nombre, string? telefono, string? grupoId)> destinos, string? mensajeIntro, WhatsAppPuntos puntos)
    {
        string intro = string.IsNullOrWhiteSpace(mensajeIntro) ? MensajeIntroDefault : mensajeIntro;
        string cierre = "Feliz resto del día,\nAtentamente,\nGerencia Comercial,\nAM Comercial";
        string html = ArmarHtmlWhatsAppCierreDiario(resultado, puntos);
        var sidecarUrl = (config["Smtp:WhatsApp:SidecarUrl"] ?? "http://localhost:5099").TrimEnd('/');

        foreach (var d in destinos)
        {
            try
            {
                var body = JsonSerializer.Serialize(new { telefono = d.telefono, grupoId = d.grupoId, html, caption = cierre, textoIntro = intro });
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var resp = await _whatsAppHttp.PostAsync($"{sidecarUrl}/enviar", content);
                if (resp.IsSuccessStatusCode) log.LogInformation("WhatsApp enviado a {Nombre} ({Destino}).", d.nombre, d.telefono ?? d.grupoId);
                else log.LogWarning("WhatsApp a {Nombre} ({Destino}) respondió {Status}.", d.nombre, d.telefono ?? d.grupoId, resp.StatusCode);
            }
            catch (Exception ex) { log.LogWarning(ex, "WhatsApp a {Nombre} ({Destino}) falló — ¿el sidecar WhatsAppSender está corriendo en {Url}?", d.nombre, d.telefono ?? d.grupoId, sidecarUrl); }
        }
    }

    // Qué bloques entran en la imagen compacta — checklist pedido explícito 2026-09-20
    // ("decidir cual de los puntos se envían"). Ventas del día/Mes y Pensamiento quedan
    // true por defecto porque eran los únicos que ya existían antes de este pedido.
    public sealed record WhatsAppPuntos(bool VentasDia = true, bool ActivacionesDia = false, bool VentasMes = true, bool ActivacionesMes = false, bool ResumenEjecutivo = false, bool Pensamiento = true);

    internal const string MensajeIntroDefault = "Buenas, a continuación les compartimos el resultado del día:";

    // Resume los filtros ACTIVOS del reporte en una línea corta ("Colombina", "Colombina ·
    // Supervisor 12") para que la imagen diga a qué corresponde — pedido explícito
    // 2026-09-20: "cuando el reporte se filtre debemos colocar los datos del filtro...
    // debió decir Colombina". Sin filtros activos, dice "Todos los datos". Omite "Todos"
    // para las dimensiones sin filtrar (a diferencia del resumen del reporte completo en
    // pantalla) para no ensuciar una imagen que debe salir corta.
    private static string FiltroResumenWhatsApp(dynamic filtros)
    {
        var partes = new List<string>();
        void Agregar(IEnumerable<object> vals) { if (vals == null) return; foreach (var v in vals) { var s = v?.ToString(); if (!string.IsNullOrWhiteSpace(s)) partes.Add(s); } }
        Agregar(filtros.suplidor); Agregar(filtros.categoria); Agregar(filtros.centros); Agregar(filtros.canales);
        Agregar(filtros.divisiones); Agregar(filtros.supervisores); Agregar(filtros.vendedores);
        return partes.Count > 0 ? string.Join(" · ", partes) : "Todos los datos";
    }

    // Plantilla COMPACTA para WhatsApp — "versión sencilla", pensada para salir como una
    // sola imagen corta (Puppeteer la captura tal cual, ancho fijo). A diferencia del correo
    // (ArmarHtmlCorreoCierreDiario), acá se agrega también el acumulado del MES (nuevo punto
    // 1 del reporte) y los días transcurridos/restantes — pedido explícito 2026-09-19. Qué
    // bloques se incluyen lo decide 'puntos' (checklist, pedido 2026-09-20).
    // Totales de una sección (r.ventas/r.activaciones/r.mesVentas/r.mesActivaciones),
    // sumando porCentro — MISMA cuenta que ya hace el front en cdTablaAlcance/
    // cdAjustadaTotalCols/cdAlDiaTotalCols (ApiPdfPresController genera esos campos por fila,
    // acá se agregan los totales para la imagen compacta). presupuestoAjustada/
    // logroAjustadaPct (día, solo Ventas — MNTDCAT1DIA, columna de referencia desde que
    // MNTDCAT2DIA/"Meta lineal" es la primaria, 2026-09-24) y presupuestoAlDia/logroAlDiaPct
    // (mes) quedan en null cuando ninguna fila trae ese dato (p.ej. Activaciones nunca trae
    // ajustada).
    private sealed record TotalesSeccion(double Presupuesto, double Ejecutado, double? PresupuestoAjustada, double? EjecutadoAjustada, double? PresupuestoAlDia);
    private static TotalesSeccion SumarSeccion(dynamic sec)
    {
        double presu = 0, ejec = 0, presuAjustada = 0, ejecAjustada = 0, presuAlDia = 0;
        bool hayAjustada = false, hayAlDia = false;
        foreach (var f in sec.porCentro)
        {
            presu += (double)f.presupuesto; ejec += (double)f.ejecutado;
            // OJO: asignar directo un 'dynamic' a una variable 'double?' revienta en runtime
            // ("'double' does not contain a definition for 'HasValue'") — al boxear un
            // Nullable<double> con valor, el CLR lo boxea como 'double' plano (no como
            // Nullable<double>), y el binder dinámico de C# no siempre lo reconvierte bien
            // de vuelta a Nullable<double> en ese camino. Error real encontrado 2026-09-20.
            // Fix: pasar por 'object' primero y comparar contra null ahí, recién convertir
            // a 'double' (no nullable) una vez confirmado que no es null.
            object paObj = f.presupuestoAjustada;
            if (paObj != null) { hayAjustada = true; double pa = (double)paObj; double da = (double)f.diferenciaAjustada; presuAjustada += pa; ejecAjustada += pa + da; }
            object padObj = f.presupuestoAlDia;
            if (padObj != null) { hayAlDia = true; presuAlDia += (double)padObj; }
        }
        return new TotalesSeccion(presu, ejec, hayAjustada ? presuAjustada : (double?)null, hayAjustada ? ejecAjustada : (double?)null, hayAlDia ? presuAlDia : (double?)null);
    }

    // Pedido explícito 2026-09-20: "en cada punto el mensaje debe estar íntegro con todos
    // los campos" — el nivel de detalle de cada bloque de la imagen debe calzar con el que
    // tiene ese mismo punto en el reporte completo (Views/Supervision/Index.cshtml
    // cdTablaAlcance/cdSeccionHtml), no un subconjunto recortado.
    internal static string ArmarHtmlWhatsAppCierreDiario(dynamic r, WhatsAppPuntos puntos)
    {
        string Color(double? p) => p == null ? "#888" : (p >= 90 ? "#1f8b4c" : (p >= 80 ? "#b8860b" : "#c0392b"));
        string Pct(double? p) => p == null ? "—" : $"{p.Value:0.0}%";
        string Fila(string label, string valor, string? color = null, bool bold = false)
        {
            string b0 = bold ? "<b>" : ""; string b1 = bold ? "</b>" : "";
            string estilo = color != null ? $"color:{color};font-weight:700" : "";
            return $"<div style='display:flex;justify-content:space-between;font-size:13px;margin-bottom:2px'><span>{label}</span><span style='{estilo}'>{b0}{valor}{b1}</span></div>";
        }
        string N(double v) => v.ToString("#,0");

        // Tabla compacta de UN nivel (Centro/Canal/Supervisor/Suplidor) con TODAS las
        // columnas de ese punto — pedido explícito 2026-09-20: "el mensaje solo envía una
        // parte del reporte... lo necesito completo" — antes el bloque de WhatsApp solo
        // traía el TOTAL agregado, sin el desglose por nivel que sí tiene el reporte
        // completo en pantalla (cdSeccionHtml: Centro/Canal/Supervisor/Suplidor). Mismas
        // columnas que cdTablaAlcance (solo el nivel superior — sin el detalle anidado de
        // Vendedor/Categoría, igual que el modo "Resumido" del reporte completo).
        void TablaNivel(StringBuilder sb2, string nivel, dynamic filas, bool esMes, bool conAjustada)
        {
            bool hayFilas = false; foreach (var _ in filas) { hayFilas = true; break; }
            if (!hayFilas) return;
            const string th = "text-align:right;color:#8a94a6;font-weight:600;padding:1px 4px;white-space:nowrap";
            const string td = "text-align:right;padding:1px 4px;white-space:nowrap";
            const string tl = "text-align:left;padding:1px 4px";
            sb2.Append($"<div style='font-size:10px;color:#8a94a6;font-weight:700;margin:6px 0 2px;text-transform:uppercase'>{nivel}</div>");
            sb2.Append("<table style='width:100%;border-collapse:collapse;font-size:10.5px'><tr>");
            sb2.Append($"<td style='{th};text-align:left'>&nbsp;</td><td style='{th}'>Presup.</td>");
            if (esMes) sb2.Append($"<td style='{th}'>P.Día</td><td style='{th}'>L.Día</td>");
            sb2.Append($"<td style='{th}'>Ejec.</td><td style='{th}'>{(esMes ? "L.Acum" : "Logro")}</td><td style='{th}'>Dif.</td>");
            if (conAjustada) sb2.Append($"<td style='{th}'>P.Ajus</td><td style='{th}'>L.Ajus</td>");
            sb2.Append("</tr>");

            double tp = 0, te = 0, tpAlDia = 0, tpAj = 0, teAj = 0; bool hayAlDia = false, hayAj = false;
            foreach (var f in filas)
            {
                double presu = (double)f.presupuesto, ejec = (double)f.ejecutado, dif = (double)f.diferencia;
                double? pct = f.alcancePct;
                tp += presu; te += ejec;
                sb2.Append("<tr>");
                sb2.Append($"<td style='{tl}'>{System.Net.WebUtility.HtmlEncode((string)f.nombre)}</td>");
                sb2.Append($"<td style='{td}'>{N(presu)}</td>");
                if (esMes)
                {
                    object padObj = f.presupuestoAlDia;
                    double? pad = padObj != null ? (double)padObj : (double?)null;
                    double? logroAlDia = pad.HasValue && pad.Value != 0 ? 100.0 * ejec / pad.Value : (double?)null;
                    if (pad.HasValue) { tpAlDia += pad.Value; hayAlDia = true; }
                    sb2.Append($"<td style='{td}'>{(pad.HasValue ? N(pad.Value) : "—")}</td>");
                    sb2.Append($"<td style='{td};color:{Color(logroAlDia)}'>{Pct(logroAlDia)}</td>");
                }
                sb2.Append($"<td style='{td}'>{N(ejec)}</td>");
                sb2.Append($"<td style='{td};color:{Color(pct)}'>{Pct(pct)}</td>");
                sb2.Append($"<td style='{td};color:{(dif >= 0 ? "#1f8b4c" : "#c0392b")}'>{(dif >= 0 ? "+" : "")}{N(dif)}</td>");
                if (conAjustada)
                {
                    object paObj = f.presupuestoAjustada;
                    if (paObj != null)
                    {
                        double pa = (double)paObj; double da = (double)f.diferenciaAjustada;
                        double? pctAj = pa != 0 ? 100.0 * (pa + da) / pa : (double?)null;
                        tpAj += pa; teAj += pa + da; hayAj = true;
                        sb2.Append($"<td style='{td}'>{N(pa)}</td><td style='{td};color:{Color(pctAj)}'>{Pct(pctAj)}</td>");
                    }
                    else sb2.Append($"<td style='{td}'>—</td><td style='{td}'>—</td>");
                }
                sb2.Append("</tr>");
            }

            double? tpct = tp != 0 ? 100.0 * te / tp : (double?)null;
            double tdif = te - tp;
            sb2.Append($"<tr style='font-weight:700;border-top:1px solid #ddd'><td style='{tl}'>Total</td><td style='{td}'>{N(tp)}</td>");
            if (esMes)
            {
                double? tLogroAlDia = hayAlDia && tpAlDia != 0 ? 100.0 * te / tpAlDia : (double?)null;
                sb2.Append($"<td style='{td}'>{(hayAlDia ? N(tpAlDia) : "—")}</td><td style='{td};color:{Color(tLogroAlDia)}'>{Pct(tLogroAlDia)}</td>");
            }
            sb2.Append($"<td style='{td}'>{N(te)}</td><td style='{td};color:{Color(tpct)}'>{Pct(tpct)}</td>");
            sb2.Append($"<td style='{td};color:{(tdif >= 0 ? "#1f8b4c" : "#c0392b")}'>{(tdif >= 0 ? "+" : "")}{N(tdif)}</td>");
            if (conAjustada)
            {
                double? tpctAj = hayAj && tpAj != 0 ? 100.0 * teAj / tpAj : (double?)null;
                sb2.Append($"<td style='{td}'>{(hayAj ? N(tpAj) : "—")}</td><td style='{td};color:{Color(tpctAj)}'>{Pct(tpctAj)}</td>");
            }
            sb2.Append("</tr></table>");
        }

        // Ventas/Activaciones DEL DÍA — total (Presupuesto/Ejecutado/Logro/Diferencia ya
        // vienen calculados sobre "Meta lineal"/MNTDCAT2DIA desde 2026-09-24 — pedido
        // explícito del usuario, "los presupuestos, tómalos de la meta lineal... para todos
        // los niveles del informe" — ver AgruparAlcance/GenerarCierreDiario en
        // ApiPdfPresController.cs) + "Meta Ajustada" (MNTDCAT1DIA) como columna de
        // referencia cuando aplica, solo Ventas + desglose COMPLETO por
        // Centro/Canal/Supervisor/Suplidor — pedido explícito 2026-09-20.
        void BloqueDia(StringBuilder sb2, string titulo, dynamic sec, bool conAjustada)
        {
            // CAUSA RAÍZ REAL (confirmada con la traza del usuario, 2026-09-20): 'var t' con
            // un argumento dynamic hace que TODA la llamada quede tipada 'dynamic' en tiempo
            // de compilación (aunque SumarSeccion declare que devuelve TotalesSeccion) — así
            // que cada 't.Campo.HasValue' de acá abajo se resolvía en tiempo de ejecución y
            // reventaba con el mismo bug de boxing de Nullable<double>. Fix: anclar el tipo
            // explícito acá (fuerza la conversión dynamic->TotalesSeccion UNA sola vez, en
            // esta línea) para que todo lo que sigue sea 100% estático.
            TotalesSeccion t = SumarSeccion(sec);
            double? pct = t.Presupuesto != 0 ? 100.0 * t.Ejecutado / t.Presupuesto : (double?)null;
            double dif = t.Ejecutado - t.Presupuesto;
            sb2.Append($"<div style='font-size:12px;color:#57606a;font-weight:700;margin-bottom:4px'>{titulo}</div>");
            sb2.Append(Fila("Ejecutado", "RD$ " + t.Ejecutado.ToString("#,0"), bold: true));
            sb2.Append(Fila("Presupuesto", "RD$ " + t.Presupuesto.ToString("#,0")));
            sb2.Append(Fila("Logro del día", Pct(pct), Color(pct)));
            sb2.Append(Fila("Diferencia", (dif >= 0 ? "+" : "") + dif.ToString("#,0"), dif >= 0 ? "#1f8b4c" : "#c0392b"));
            if (conAjustada)
            {
                if (t.PresupuestoAjustada.HasValue)
                {
                    double difAj = t.EjecutadoAjustada!.Value - t.PresupuestoAjustada.Value;
                    double? pctAj = t.PresupuestoAjustada.Value != 0 ? 100.0 * t.EjecutadoAjustada.Value / t.PresupuestoAjustada.Value : (double?)null;
                    sb2.Append(Fila("Meta Ajustada", "RD$ " + t.PresupuestoAjustada.Value.ToString("#,0")));
                    sb2.Append(Fila("Logro Ajustado", Pct(pctAj), Color(pctAj)));
                    sb2.Append(Fila("Diferencia Ajustada", (difAj >= 0 ? "+" : "") + difAj.ToString("#,0"), difAj >= 0 ? "#1f8b4c" : "#c0392b"));
                }
                else sb2.Append(Fila("Meta Ajustada", "—"));
            }
            TablaNivel(sb2, "Centro", sec.porCentro, esMes: false, conAjustada: conAjustada);
            TablaNivel(sb2, "Canal", sec.porCanal, esMes: false, conAjustada: conAjustada);
            TablaNivel(sb2, "Supervisor", sec.porSupervisor, esMes: false, conAjustada: conAjustada);
            TablaNivel(sb2, "Suplidor", sec.porSuplidor, esMes: false, conAjustada: conAjustada);
            sb2.Append("<div style='margin-bottom:12px'></div>");
        }
        // Ventas/Activaciones ACUMULADO DEL MES — total (mismos 6 campos que el punto 1 en
        // pantalla) + desglose COMPLETO por Centro/Canal/Supervisor/Suplidor — pedido
        // explícito 2026-09-20.
        void BloqueMes(StringBuilder sb2, string titulo, dynamic sec)
        {
            TotalesSeccion t = SumarSeccion(sec); // mismo fix que BloqueDia — ver comentario ahí
            double? logroAcum = t.Presupuesto != 0 ? 100.0 * t.Ejecutado / t.Presupuesto : (double?)null;
            double? logroAlDia = t.PresupuestoAlDia.HasValue && t.PresupuestoAlDia.Value != 0 ? 100.0 * t.Ejecutado / t.PresupuestoAlDia.Value : (double?)null;
            double dif = t.Ejecutado - t.Presupuesto;
            sb2.Append($"<div style='font-size:12px;color:#57606a;font-weight:700;margin-bottom:4px'>{titulo}</div>");
            sb2.Append(Fila("Presupuesto", "RD$ " + t.Presupuesto.ToString("#,0")));
            sb2.Append(Fila("Presupuesto al Día", t.PresupuestoAlDia.HasValue ? "RD$ " + t.PresupuestoAlDia.Value.ToString("#,0") : "—"));
            sb2.Append(Fila("Logro al Día", Pct(logroAlDia), Color(logroAlDia)));
            sb2.Append(Fila("Ejecutado", "RD$ " + t.Ejecutado.ToString("#,0"), bold: true));
            sb2.Append(Fila("Logro Acumulado", Pct(logroAcum), Color(logroAcum)));
            sb2.Append(Fila("Diferencia", (dif >= 0 ? "+" : "") + dif.ToString("#,0"), dif >= 0 ? "#1f8b4c" : "#c0392b"));
            TablaNivel(sb2, "Centro", sec.porCentro, esMes: true, conAjustada: false);
            TablaNivel(sb2, "Canal", sec.porCanal, esMes: true, conAjustada: false);
            TablaNivel(sb2, "Supervisor", sec.porSupervisor, esMes: true, conAjustada: false);
            TablaNivel(sb2, "Suplidor", sec.porSuplidor, esMes: true, conAjustada: false);
            sb2.Append("<div style='margin-bottom:12px'></div>");
        }

        var sb = new StringBuilder();
        sb.Append("<html><body style='margin:0;padding:0;background:#fff'>");
        sb.Append("<div style='width:520px;font-family:Segoe UI,Arial,sans-serif;box-sizing:border-box;padding:16px'>");
        sb.Append("<div style='background:linear-gradient(115deg,#1c2f6e 0%,#2f7d3f 55%,#4f9a5c 100%);color:#fff;padding:14px 16px;border-radius:10px 10px 0 0'>");
        sb.Append($"<div style='font-size:16px;font-weight:700'>Cierre del día {((DateTime)DateTime.Parse((string)r.fecha)):dd/MM/yyyy}</div>");
        sb.Append($"<div style='opacity:.95;font-size:12px;font-weight:600;margin-top:2px'>{System.Net.WebUtility.HtmlEncode(FiltroResumenWhatsApp(r.filtros))}</div>");
        sb.Append("<div style='opacity:.85;font-size:11px'>MovilBusiness Dashboard — AM Comercial</div></div>");
        sb.Append("<div style='border:1px solid #ddd;border-top:0;padding:14px 16px;border-radius:0 0 10px 10px'>");

        if (puntos.VentasDia) BloqueDia(sb, "VENTAS DEL DÍA", r.ventas, conAjustada: true);
        if (puntos.ActivacionesDia) BloqueDia(sb, "ACTIVACIONES DEL DÍA", r.activaciones, conAjustada: false);
        if (puntos.VentasMes) BloqueMes(sb, "ACUMULADO DEL MES — VENTAS", r.mesVentas);
        if (puntos.ActivacionesMes) BloqueMes(sb, "ACUMULADO DEL MES — ACTIVACIONES", r.mesActivaciones);
        if (puntos.VentasMes || puntos.ActivacionesMes)
            sb.Append($"<div style='font-size:11px;color:#888;margin-top:-8px;margin-bottom:12px'>Día hábil {(int)r.diasTranscurridos} de {(int)r.diasDelMes} · faltan {(int)r.diasRestantes} día(s) hábil(es)</div>");

        if (puntos.ResumenEjecutivo)
        {
            sb.Append("<div style='font-size:12px;color:#57606a;font-weight:700;margin-bottom:4px'>RESUMEN EJECUTIVO</div>");
            sb.Append("<ul style='margin:0 0 12px;padding-left:16px;font-size:12px;color:#2d333b'>");
            int n = 0; foreach (var h in r.resumenEjecutivo.hallazgos) { if (n++ >= 3) break; sb.Append("<li style='margin-bottom:3px'>" + h + "</li>"); }
            sb.Append("</ul>");
        }

        if (puntos.Pensamiento)
        {
            sb.Append("<div style='padding:10px 12px;background:#fafafa;border-left:3px solid #2f7d3f;font-size:12px;font-style:italic'>");
            sb.Append("“" + r.pensamiento.frase + "”");
            sb.Append("</div>");
        }

        sb.Append("</div></div></body></html>");
        return sb.ToString();
    }

    // Dispatcher del cuerpo del correo según el tipo de reporte — cada uno tiene su
    // propia forma de resultado (GenerarCierreDiario vs GenerarProyeccionVentas).
    private static string ArmarHtmlCorreo(string reporte, string nombreProgramacion, dynamic r)
        => reporte switch
        {
            "cierres_cierre_diario" => ArmarHtmlCorreoCierreDiario(r),
            "gobierno_comercial_ventas_proyectadas" => ArmarHtmlCorreoVentasProyectadas(nombreProgramacion, r),
            _ => $"<p>Reporte '{reporte}' generado — sin plantilla de correo específica todavía.</p>",
        };

    private static string ArmarHtmlCorreoCierreDiario(dynamic r)
    {
        var sb = new StringBuilder();
        sb.Append("<div style='font-family:Segoe UI,Arial,sans-serif;max-width:720px;margin:0 auto'>");
        sb.Append($"<div style='background:linear-gradient(115deg,#1c2f6e 0%,#2f7d3f 55%,#4f9a5c 100%);color:#fff;padding:18px 22px;border-radius:10px 10px 0 0'>");
        sb.Append($"<h2 style='margin:0'>Cierre del día {((DateTime)DateTime.Parse((string)r.fecha)):dd/MM/yyyy}</h2>");
        sb.Append("<div style='opacity:.9;font-size:13px'>MovilBusiness Dashboard — AM Comercial, S.A.S.</div></div>");
        sb.Append("<div style='border:1px solid #ddd;border-top:0;padding:18px 22px;border-radius:0 0 10px 10px'>");

        double presuV = 0, ejecV = 0; foreach (var f in r.ventas.porCentro) { presuV += (double)f.presupuesto; ejecV += (double)f.ejecutado; }
        double presuA = 0, ejecA = 0; foreach (var f in r.activaciones.porCentro) { presuA += (double)f.presupuesto; ejecA += (double)f.ejecutado; }
        double? alcV = presuV != 0 ? 100.0 * ejecV / presuV : (double?)null;
        double? alcA = presuA != 0 ? 100.0 * ejecA / presuA : (double?)null;

        sb.Append("<table style='width:100%;border-collapse:collapse;margin-bottom:14px'>");
        sb.Append("<tr><td style='padding:10px;background:#f5f7fa;border-radius:8px'><b>Ventas hoy</b><br>RD$ " + ejecV.ToString("#,0") + " de RD$ " + presuV.ToString("#,0") + (alcV.HasValue ? $" ({alcV.Value:0.0}%)" : "") + "</td>");
        sb.Append("<td style='width:14px'></td><td style='padding:10px;background:#f5f7fa;border-radius:8px'><b>Activaciones hoy</b><br>" + ejecA.ToString("#,0") + " de " + presuA.ToString("#,0") + (alcA.HasValue ? $" ({alcA.Value:0.0}%)" : "") + "</td></tr>");
        sb.Append("</table>");

        sb.Append("<h3 style='margin:16px 0 6px'>Resumen ejecutivo</h3><ul style='margin:0;padding-left:20px'>");
        foreach (var h in r.resumenEjecutivo.hallazgos) sb.Append("<li style='margin-bottom:4px'>" + h + "</li>");
        sb.Append("</ul>");

        sb.Append("<div style='margin-top:18px;padding:14px;background:#fafafa;border-left:3px solid #2f7d3f;font-style:italic'>");
        sb.Append("“" + r.pensamiento.frase + "”");
        if (r.pensamiento.autor != null) sb.Append("<div style='text-align:right;font-style:normal;font-size:12px;color:#666'>— " + r.pensamiento.autor + "</div>");
        sb.Append("</div>");

        sb.Append("<div style='margin-top:18px;font-size:12px;color:#888'>Entra al sistema (Supervisión Dinámica → PDF Presentaciones → Cierres → Cierre Diario) para ver el reporte completo, todos los niveles de detalle, y descargarlo en PDF.</div>");
        sb.Append("</div></div>");
        return sb.ToString();
    }

    private static string ArmarHtmlCorreoVentasProyectadas(string nombreProgramacion, dynamic r)
    {
        var sb = new StringBuilder();
        sb.Append("<div style='font-family:Segoe UI,Arial,sans-serif;max-width:720px;margin:0 auto'>");
        sb.Append("<div style='background:linear-gradient(115deg,#1c2f6e 0%,#2f7d3f 55%,#4f9a5c 100%);color:#fff;padding:18px 22px;border-radius:10px 10px 0 0'>");
        sb.Append($"<h2 style='margin:0'>{nombreProgramacion}</h2>");
        sb.Append($"<div style='opacity:.9;font-size:13px'>Ventas Proyectadas · {(string)r.meta.desde} a {(string)r.meta.hasta} — MovilBusiness Dashboard</div></div>");
        sb.Append("<div style='border:1px solid #ddd;border-top:0;padding:18px 22px;border-radius:0 0 10px 10px'>");

        sb.Append("<table style='width:100%;border-collapse:collapse;margin-bottom:14px'>");
        sb.Append("<tr>"
          + "<td style='padding:10px;background:#f5f7fa;border-radius:8px'><b>Facturas</b><br>" + ((double)r.resumen.facturas).ToString("#,0") + "</td>"
          + "<td style='width:10px'></td><td style='padding:10px;background:#f5f7fa;border-radius:8px'><b>Cajas</b><br>" + ((double)r.resumen.cajas).ToString("#,0") + "</td>"
          + "<td style='width:10px'></td><td style='padding:10px;background:#f5f7fa;border-radius:8px'><b>Monto</b><br>RD$ " + ((double)r.resumen.monto).ToString("#,0") + "</td>"
          + "</tr>");
        sb.Append("</table>");

        if (r.vsPresupuesto != null)
        {
            var t = r.vsPresupuesto.total;
            string logro = t.logroAlDiaPct != null ? ((double)t.logroAlDiaPct).ToString("0.0") + "%" : "—";
            string proyPct = t.proyectadoPct != null ? ((double)t.proyectadoPct).ToString("0.0") + "%" : "—";
            sb.Append("<h3 style='margin:4px 0 6px'>Ventas vs. Presupuesto al día y proyectado</h3>");
            sb.Append("<table style='width:100%;border-collapse:collapse;margin-bottom:14px'><tr>"
              + "<td style='padding:10px;background:#f5f7fa;border-radius:8px'><b>Ejecutado</b><br>RD$ " + ((double)t.ejecutado).ToString("#,0") + "</td>"
              + "<td style='width:10px'></td><td style='padding:10px;background:#f5f7fa;border-radius:8px'><b>Logro al día</b><br>" + logro + "</td>"
              + "<td style='width:10px'></td><td style='padding:10px;background:#f5f7fa;border-radius:8px'><b>Proyectado cierre</b><br>RD$ " + ((double)t.proyectado).ToString("#,0") + " (" + proyPct + " del presupuesto)</td>"
              + "</tr></table>");
        }

        sb.Append("<div style='margin-top:18px;font-size:12px;color:#888'>Entra al sistema (Supervisión Dinámica → PDF Presentaciones → Gobierno Comercial) para ver el detalle completo por Centro/Canal/Supervisor/Suplidor y descargarlo en PDF.</div>");
        sb.Append("</div></div>");
        return sb.ToString();
    }
}
