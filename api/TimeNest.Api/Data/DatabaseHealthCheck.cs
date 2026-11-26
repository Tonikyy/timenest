using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TimeNest.Api.Data;

public sealed class DatabaseHealthCheck(TimeNestDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check the migrated schema as well as the connection.
            await database.ServiceTypes.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("The database is not ready.", exception);
        }
    }
}
