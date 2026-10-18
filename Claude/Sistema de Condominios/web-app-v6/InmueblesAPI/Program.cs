using System.Text;
using InmueblesAPI.Data;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ─── CORS ────────────────────────────────────────────────────────────────────
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(opt => opt.AddPolicy("frontend", p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// ─── JWT ─────────────────────────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// ─── Base de datos PostgreSQL ─────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ─── Services ────────────────────────────────────────────────────────────────
builder.Services.AddScoped<InmueblesService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PagoService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddHostedService<ProcesoNocturnoService>();

var app = builder.Build();

// Crear/actualizar tablas automáticamente al iniciar
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    // Agregar columnas nuevas si no existen (idempotente)
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE sesiones_caja ADD COLUMN IF NOT EXISTS ""FechaRegistro"" timestamp;
        UPDATE sesiones_caja SET ""FechaRegistro"" = ""Fecha"" WHERE ""FechaRegistro"" IS NULL;
    ");
}

app.UseCors("frontend");

// Global error handler — returns JSON with the real exception message
app.UseExceptionHandler(err => err.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    ctx.Response.StatusCode = 500;
    ctx.Response.ContentType = "application/json";
    var inner = ex?.InnerException?.Message ?? "";
    var msg = ex != null
        ? $"{ex.GetType().Name}: {ex.Message}" + (inner.Length > 0 ? $" | Inner: {inner}" : "")
        : "Error interno del servidor";
    await ctx.Response.WriteAsJsonAsync(new { success = false, message = msg });
}));

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health check
app.MapGet("/api/health", () => new { status = "ok", time = DateTime.Now });

app.Run();
