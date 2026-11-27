using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Testcontainers.MsSql;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Businesses;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.IntegrationTests;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly MsSqlContainer database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public static DateTimeOffset Now { get; } = new(2027, 1, 15, 8, 0, 0, TimeSpan.Zero);
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public StripeStub Stripe { get; } = new();
    public TestAccountEmail Mail { get; } = new();
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:TimeNest"] = database.GetConnectionString(), ["Stripe:SecretKey"] = "sk_test_local_stub_only", ["Stripe:WebhookSecret"] = "whsec_local_stub_only" }));
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient("stripe").ConfigurePrimaryHttpMessageHandler(() => Stripe);
                services.AddSingleton<TimeProvider>(new FixedClock());
                services.AddSingleton<TimeNest.Api.Features.Accounts.IAccountEmail>(Mail);
                // Freeze discovery time, but keep cookie issuance and stamp validation on the same live clock.
                services.PostConfigureAll<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
                    options => options.TimeProvider = TimeProvider.System);
                services.PostConfigure<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>(
                    options => options.TimeProvider = TimeProvider.System);
            });
        });
        Client = Factory.CreateClient();
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        await context.Database.MigrateAsync();

        var center = new Business
        {
            Id = Guid.NewGuid(),
            Name = "Demo Center",
            Address = "Demo 1",
            City = "Helsinki",
            Location = new Point(24.9384, 60.1699) { SRID = 4326 },
            IsDemo = true
        };
        var near = new Business
        {
            Id = Guid.NewGuid(),
            Name = "Demo Nearby",
            Address = "Demo 2",
            City = "Helsinki",
            Location = new Point(24.9484, 60.1799) { SRID = 4326 },
            IsDemo = true
        };
        var far = new Business
        {
            Id = Guid.NewGuid(),
            Name = "Demo Far",
            Address = "Demo 3",
            City = "Tampere",
            Location = new Point(23.7610, 61.4978) { SRID = 4326 },
            IsDemo = true
        };
        context.Businesses.AddRange(center, near, far);
        context.ServiceTypes.AddRange(
            new ServiceType { Id = "hair", Name = "Hiukset" },
            new ServiceType { Id = "massage", Name = "Hieronta" });
        context.Vacancies.AddRange(
            Slot(center, "hair", 2),
            Slot(center, "massage", 3),
            Slot(near, "hair", 24),
            Slot(far, "hair", 2),
            Slot(center, "hair", -1),
            Slot(center, "hair", 0),
            Slot(center, "hair", 4, VacancyStatus.Booked),
            Slot(center, "hair", 5, VacancyStatus.Held),
            Slot(center, "hair", 6, VacancyStatus.Cancelled));
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await database.DisposeAsync();
    }

    private static Vacancy Slot(Business business, string type, int hours, VacancyStatus status = VacancyStatus.Available) =>
        new()
        {
            Id = Guid.NewGuid(),
            Business = business,
            ServiceTypeId = type,
            Title = "Test appointment",
            Description = "Fictional test data",
            StartsAt = Now.AddHours(hours),
            DurationMinutes = 45,
            PriceCents = 4500,
            Status = status
        };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
