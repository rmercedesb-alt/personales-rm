var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(o => o.ServiceName = "DashProd");

builder.Services.AddControllersWithViews()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = null);

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout        = TimeSpan.FromHours(8);
    o.Cookie.HttpOnly    = true;
    o.Cookie.IsEssential = true;
    o.Cookie.Name        = ".DashProd.Session";
});

var app = builder.Build();

// Sub-aplicacion IIS: leer PathBase desde appsettings.json
// Ejemplo: "PathBase": "/dashproductividadBeta"
var pathBase = builder.Configuration["PathBase"] ?? "";
if (!string.IsNullOrEmpty(pathBase))
    app.UsePathBase(pathBase);

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.MapControllers();

app.Run();
