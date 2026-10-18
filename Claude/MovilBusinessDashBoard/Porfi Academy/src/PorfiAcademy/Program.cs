using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorfiAcademy.Data;
using PorfiAcademy.Services;

var builder = WebApplication.CreateBuilder(args);

// Carpeta de datos (base de datos, llaves de cifrado, credenciales). En desarrollo vive fuera de OneDrive
// para no corromper SQLite con la sincronización; en producción: App_Data o la ruta de "DataDir".
var dataDir = builder.Configuration["DataDir"]
    ?? (builder.Environment.IsDevelopment()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PorfiAcademy")
        : Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
var paths = new DataPaths(dataDir);
builder.Services.AddSingleton(paths);

var dpBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("PorfiAcademy")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
if (OperatingSystem.IsWindows()) dpBuilder.ProtectKeysWithDpapi();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"DataSource={Path.Combine(dataDir, "porfiacademy.db")}"));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false; // sin servidor de correo todavía
        options.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddSingleton<ISecretStore, FileSecretStore>();
builder.Services.AddSingleton<MuxTokenSigner>();
builder.Services.AddSingleton<MediaStore>();
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<SettingsService>();

// Detrás de un túnel o proxy HTTPS: respeta el esquema y el host públicos (necesario para la subida a Mux).
// No se confía en X-Forwarded-For: así nadie puede falsear su IP.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddHttpClient<MuxService>(c => c.BaseAddress = new Uri("https://api.mux.com/"));
builder.Services.AddControllersWithViews();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
}

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Instalación inicial: mientras no se complete, todo redirige a /Setup (que pide la API de Mux y crea al administrador).
app.Use(async (ctx, next) =>
{
    var done = ctx.RequestServices.GetRequiredService<ISecretStore>().IsSetupComplete;
    var onSetup = ctx.Request.Path.StartsWithSegments("/Setup");
    if (!done && !onSetup) { ctx.Response.Redirect("/Setup"); return; }
    if (done && onSetup) { ctx.Response.Redirect("/"); return; }
    await next();
});

app.UseAuthentication();

// Quien ya tiene la sesión iniciada no necesita «Entrar» ni «Crear cuenta»: se le lleva a la portada con un aviso.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true && HttpMethods.IsGet(ctx.Request.Method)
        && (ctx.Request.Path.StartsWithSegments("/Identity/Account/Register", StringComparison.OrdinalIgnoreCase)
            || ctx.Request.Path.StartsWithSegments("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase)))
    {
        ctx.Response.Redirect("/?ya=1");
        return;
    }
    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();
