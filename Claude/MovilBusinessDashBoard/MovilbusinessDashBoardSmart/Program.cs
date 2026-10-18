var builder = WebApplication.CreateBuilder(args);

// Permite correr como Servicio de Windows (sin consola abierta)
builder.Host.UseWindowsService(o => o.ServiceName = "DashSmart");

// MVC + JSON con nombres PascalCase (el JS espera RepCodigo, no repCodigo)
builder.Services.AddControllersWithViews()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = null);

// Servicio de fondo: snapshots de Torre de Control cada 30 min (ventana 8:20-16:00)
builder.Services.AddHostedService<DashVisitas.Services.TorreSnapshotService>();

// Cache en memoria: curvas de estacionalidad de Presupuestos (hasta 182+67 curvas
// posibles, recalcularlas en vivo en cada request sería inaceptable) — 24h de vida.
builder.Services.AddMemoryCache();

// Precalienta la curva "General" (la más pedida, ~17s en frío) apenas arranca la app,
// para que el primer usuario del día no la pague — ver PresupuestosWarmupService.
builder.Services.AddHostedService<DashVisitas.Services.PresupuestosWarmupService>();

// Servicio de fondo: envío programado GENÉRICO de reportes de PDF Presentaciones
// (revisa cada minuto ReportesProgramados_RM — cada programación tiene su propio
// reporte+filtros+hora+destinatarios) — ver ReportesProgramadosSchedulerService.
// Sin efecto si no hay programaciones activas o si "Smtp" no está configurado en
// appsettings.json (loguea y sigue esperando, nunca bloquea el arranque de la app).
builder.Services.AddHostedService<DashVisitas.Services.ReportesProgramadosSchedulerService>();

// Auto-arranca el sidecar Node "WhatsAppSender" (envío del Cierre Diario por WhatsApp) si
// no está corriendo todavía — pedido explícito del usuario 2026-09-23 ("¿por qué no
// incluyes que levantemos ese servicio con el proyecto?"). Queda como proceso DETACHED
// (su propia ventana), no como hijo del Dashboard — ver WhatsAppSenderLauncherService.
builder.Services.AddHostedService<DashVisitas.Services.WhatsAppSenderLauncherService>();

// Sesion en memoria (equivalente a Session de Classic ASP)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout        = TimeSpan.FromHours(8);
    o.Cookie.HttpOnly    = true;
    o.Cookie.IsEssential = true;
    o.Cookie.Name        = ".DashSmart.Session";
});

// Licencia del producto (fecha de vencimiento + los 9 módulos activos/inactivos) —
// ver Services/LicenseService.cs para el diseño completo (archivo firmado RSA,
// App_Data/license.lic, emitido solo con LicenseTool/).
builder.Services.AddSingleton<DashVisitas.Services.LicenseService>();

var app = builder.Build();

// Instancia la licencia YA al arrancar (no en el primer request): así la huella del servidor
// queda escrita en App_Data\huella_servidor.txt apenas inicia el sistema.
app.Services.GetRequiredService<DashVisitas.Services.LicenseService>();

// Sub-aplicacion IIS: leer PathBase desde appsettings.json
// Ejemplo: "PathBase": "/movilbusinessdashboardBeta"
var pathBase = builder.Configuration["PathBase"] ?? "";
if (!string.IsNullOrEmpty(pathBase))
    app.UsePathBase(pathBase);

app.UseStaticFiles();
app.UseRouting();
app.UseSession();

// Gate global de licencia — si venció (o no hay license.lic válido), corta TODAS las
// rutas con un aviso, salvo /version (diagnóstico remoto) y los archivos estáticos (ya
// servidos arriba por UseStaticFiles, nunca llegan aquí). No importa si el usuario está
// logueado o no — "el sistema queda disponible" solo mientras la licencia es válida.
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/version") { await next(); return; }
    var licencia = context.RequestServices.GetRequiredService<DashVisitas.Services.LicenseService>();
    if (!licencia.EsValida)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(DashVisitas.Services.LicenseService.PaginaNoDisponibleHtml(licencia));
        return;
    }
    await next();
});

app.MapControllers();

// Version desplegada (para confirmar que la actualizacion quedo aplicada): /version
app.MapGet("/version", () =>
{
    var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
    var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(loc);
    return Results.Json(new
    {
        app     = "DashSmart",
        version = fvi.ProductVersion ?? fvi.FileVersion ?? "?",
        built   = System.IO.File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm")
    });
});

app.Run();
