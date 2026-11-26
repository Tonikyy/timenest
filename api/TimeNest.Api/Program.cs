using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
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
