var builder = WebApplication.CreateBuilder(args);

// Permite correr como Servicio de Windows (sin consola abierta)
builder.Host.UseWindowsService(o => o.ServiceName = "DashVisitas");

// MVC + JSON con nombres PascalCase (el JS espera RepCodigo, no repCodigo)
builder.Services.AddControllersWithViews()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = null);

// Sesion en memoria (equivalente a Session de Classic ASP)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout        = TimeSpan.FromHours(8);
    o.Cookie.HttpOnly    = true;
    o.Cookie.IsEssential = true;
    o.Cookie.Name        = ".DashVisitas.Session";
});

var app = builder.Build();

// Sub-aplicacion IIS: leer PathBase desde appsettings.json
// Ejemplo: "PathBase": "/movilbusinessdashboardBeta"
var pathBase = builder.Configuration["PathBase"] ?? "";
if (!string.IsNullOrEmpty(pathBase))
    app.UsePathBase(pathBase);

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.MapControllers();

app.Run();
