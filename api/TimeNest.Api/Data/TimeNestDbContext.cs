using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Features.Businesses;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.Api.Data;

public sealed class TimeNestDbContext(DbContextOptions<TimeNestDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Business>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Address).HasMaxLength(240);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.Location).HasColumnType("geography");
        });
        modelBuilder.Entity<ServiceType>(entity =>
        {
            entity.Property(x => x.Id).HasMaxLength(40);
            entity.Property(x => x.Name).HasMaxLength(100);
        });
        modelBuilder.Entity<Vacancy>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(160);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.ServiceTypeId).HasMaxLength(40);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.Status, x.StartsAt });
            entity.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ServiceType).WithMany().HasForeignKey(x => x.ServiceTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Vacancies_PriceCents", "[PriceCents] > 0");
                table.HasCheckConstraint("CK_Vacancies_DurationMinutes", "[DurationMinutes] BETWEEN 5 AND 480");
            });
        });
    }
}
