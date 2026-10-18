using OptimizadorRutas.Components;
using OptimizadorRutas.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Servicios ────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// RutaService como Scoped (una instancia por sesion de usuario)
builder.Services.AddScoped<RutaService>();

// ── Pipeline ─────────────────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
