using TimeNest.Api.Features.Bookings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Features.Businesses;
using TimeNest.Api.Features.Payments;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.Api.Data;

public sealed class TimeNestDbContext(DbContextOptions<TimeNestDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<CheckoutPayment> CheckoutPayments => Set<CheckoutPayment>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<StripeConnection> StripeConnections => Set<StripeConnection>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<CheckoutPayment>(entity =>
        {
            entity.HasKey(x => x.BookingId);
            entity.Property(x => x.PublicOrigin).HasMaxLength(2048);
            entity.Property(x => x.SessionId).HasMaxLength(255);
            entity.Property(x => x.PaymentIntentId).HasMaxLength(255);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => x.SessionId).IsUnique();
            entity.HasIndex(x => x.PaymentIntentId).IsUnique();
            entity.HasOne<Booking>().WithOne().HasForeignKey<CheckoutPayment>(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.Property(x => x.Version).IsRowVersion();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Title).HasMaxLength(160);
            entity.Property(x => x.BusinessName).HasMaxLength(160);
            entity.Property(x => x.Address).HasMaxLength(240);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.ServiceTypeId).HasMaxLength(40);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.RecipientAccountId).HasMaxLength(255);
            entity.HasIndex(x => x.VacancyId).IsUnique().HasFilter("[IsActive] = 1");
            entity.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            entity.HasOne<Vacancy>().WithMany().HasForeignKey(x => x.VacancyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Bookings_PriceFee", "[PriceCents] >= 50 AND [FeeCents] >= 0 AND [FeeCents] < [PriceCents]");
                table.HasCheckConstraint("CK_Bookings_Active", "([Status] IN ('Pending', 'Confirmed') AND [IsActive] = 1) OR ([Status] = 'Expired' AND [IsActive] = 0)");
            });
        });
        modelBuilder.Entity<Business>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.OwnerId).IsUnique();
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Address).HasMaxLength(240);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.Location).HasColumnType("geography");
        });
        modelBuilder.Entity<StripeConnection>(entity =>
        {
            entity.HasKey(x => x.BusinessId);
            entity.Property(x => x.AccountId).HasMaxLength(255);
            entity.Property(x => x.ContactEmail).HasMaxLength(254);
            entity.Property(x => x.CreationApiVersion).HasDefaultValue(1);
            entity.HasIndex(x => x.AccountId).IsUnique();
            entity.HasOne<Business>().WithOne().HasForeignKey<StripeConnection>(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ServiceType>(entity =>
        {
            entity.Property(x => x.Id).HasMaxLength(40);
            entity.Property(x => x.Name).HasMaxLength(100);
        });
        modelBuilder.Entity<Vacancy>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(160);
            entity.Property(x => x.Version).IsRowVersion();
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
