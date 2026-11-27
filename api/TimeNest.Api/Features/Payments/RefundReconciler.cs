using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Payments;

public sealed class RefundReconciler(IServiceScopeFactory scopes, IOptions<StripeConnectOptions> options,
    TimeProvider clock, ILogger<RefundReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!options.Value.IsConfigured) continue;
            try
            {
                using var scan = scopes.CreateScope();
                var db = scan.ServiceProvider.GetRequiredService<TimeNestDbContext>();
                var recent = clock.GetUtcNow().AddDays(-35);
                // Also check recent successes: card refunds can fail later at the receiving bank.
                var ids = await db.PaymentRefunds.Where(x => x.Status == RefundStatus.Queued ||
                    x.Status == RefundStatus.Pending || (x.Status == RefundStatus.Succeeded && x.CreatedAt > recent))
                    .OrderBy(x => x.LastCheckedAt).ThenBy(x => x.BookingId)
                    .Select(x => x.BookingId).Take(50).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    try
                    {
                        using var work = scopes.CreateScope();
                        await work.ServiceProvider.GetRequiredService<RefundService>().Reconcile(id, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception) { logger.LogWarning("Refund reconciliation deferred for booking {BookingId}.", id); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Refund scan failed; durable obligations remain queued."); }
        }
    }
}
