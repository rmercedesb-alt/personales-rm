using ADEnterprise.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ── Razor Pages ──────────────────────────────────────────────
builder.Services.AddRazorPages(options =>
{
    // Páginas que NO requieren login
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Setup");
    options.Conventions.AllowAnonymousToPage("/Error");

    // Todo lo demás requiere autenticación
    options.Conventions.AuthorizeFolder("/");
});

// ── Autenticación por Cookie ──────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/AccesoDenegado";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "ADEnterprise.Auth";
    });

builder.Services.AddAuthorization();

// ── Servicios de Google Sheets ────────────────────────────────
builder.Services.AddSingleton<GoogleSheetsService>();
builder.Services.AddScoped<UsuarioService>();
builder.Services.AddScoped<CompaniaService>();
builder.Services.AddScoped<ClienteService>();
builder.Services.AddScoped<FacturaService>();
builder.Services.AddScoped<PagoService>();
builder.Services.AddScoped<MigracionService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<ReporteService>();
builder.Services.AddScoped<SetupService>();
builder.Services.AddScoped<MantenimientoService>();

// ── Session (para mensajes flash) ────────────────────────────
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// ── Pipeline ──────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(opt => opt.Run(async ctx =>
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var msg = feature?.Error?.Message ?? "Error inesperado";
        await ctx.Response.WriteAsync($"""
            <!DOCTYPE html><html><head><meta charset="utf-8"/>
            <title>Error — AD Enterprise</title>
            <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css" rel="stylesheet"/>
            </head><body class="bg-light">
            <div class="container mt-5"><div class="card shadow border-danger">
            <div class="card-header bg-danger text-white fw-bold">⚠️ Error del sistema</div>
            <div class="card-body">
            <p class="mb-1"><strong>Detalle:</strong> {System.Web.HttpUtility.HtmlEncode(msg)}</p>
            <hr/><a href="/" class="btn btn-primary">← Volver al inicio</a>
            </div></div></div></body></html>
            """);
    }));
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
