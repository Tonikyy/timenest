using TimeNest.Api.Features.Bookings;
using TimeNest.Api.Features.Accounts;
using TimeNest.Api.Features.Payments;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options => options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<TimeNestDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("TimeNest")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:TimeNest. See scripts/setup-local.ps1."),
        sql => sql.UseNetTopologySuite()));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("sql-server", tags: ["ready"]);

builder.Services.AddAccounts(builder.Environment);
builder.Services.AddOptions<StripeConnectOptions>().BindConfiguration("Stripe")
    .Validate(options => options.IsValid(builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")),
        "Stripe requires a sandbox secret key and a trusted HTTPS origin (local HTTP is allowed in development).")
    .ValidateOnStart();
builder.Services.AddHttpClient("stripe", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<IStripeConnectGateway, StripeConnectGateway>();
builder.Services.AddRateLimiter(options => options.AddPolicy("stripe-connect", context =>
    System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.AddScoped<Reservations>();
builder.Services.AddScoped<Cancellations>();
builder.Services.AddScoped<BookingViews>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<RefundService>();
builder.Services.AddScoped<IStripeRefundGateway, StripeRefundGateway>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<RefundReconciler>();
builder.Services.AddScoped<IStripeCheckoutGateway, StripeCheckoutGateway>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<CheckoutReconciler>();
builder.Services.AddRateLimiter(options => options.AddPolicy("checkout", context =>
    System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
var app = builder.Build();
if (args.Contains("--migrate") || args.Contains("--seed-demo"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Local migration and demo seeding commands require the Development environment.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
    await database.Database.MigrateAsync();
    if (args.Contains("--seed-demo"))
    {
        await DemoData.SeedAsync(database, scope.ServiceProvider.GetRequiredService<TimeProvider>());
    }

    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.MapControllers();
app.Run();

public partial class Program;
