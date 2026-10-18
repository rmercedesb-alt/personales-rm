using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PorfiAcademy.Data;

public class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<AccessCode> AccessCodes => Set<AccessCode>();
    public DbSet<SubscriptionRequest> SubscriptionRequests => Set<SubscriptionRequest>();
    public DbSet<PromoSlide> PromoSlides => Set<PromoSlide>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<VideoMeta> VideoMetas => Set<VideoMeta>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Plan>().Property(p => p.Price).HasColumnType("decimal(12,2)");
        builder.Entity<Subscription>().HasIndex(s => new { s.UserId, s.ExpiresAt });
        builder.Entity<AccessCode>().HasIndex(c => c.Code).IsUnique();

        // Planes de ejemplo; el dueño ajusta nombres, duración y precios desde el panel.
        builder.Entity<Plan>().HasData(
            new Plan { Id = 1, Name = "Mensual", Description = "Acceso a todos los cursos y niveles.", DurationDays = 30, SortOrder = 1 },
            new Plan { Id = 2, Name = "Trimestral", Description = "Acceso a todos los cursos y niveles.", DurationDays = 90, SortOrder = 2 },
            new Plan { Id = 3, Name = "Anual", Description = "Acceso a todos los cursos y niveles.", DurationDays = 365, SortOrder = 3 });
    }
}
