using System.Text;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

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

// ─── Services ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<GoogleSheetsService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PagoService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddHostedService<ProcesoNocturnoService>();

var app = builder.Build();

app.UseCors("frontend");

// Global error handler — returns JSON with the real exception message
app.UseExceptionHandler(err => err.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    ctx.Response.StatusCode = 500;
    ctx.Response.ContentType = "application/json";
    var msg = ex?.Message ?? "Error interno del servidor";
    await ctx.Response.WriteAsJsonAsync(new { success = false, message = msg });
}));

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health check
app.MapGet("/api/health", () => new { status = "ok", time = DateTime.Now });

app.Run();
