using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Bookings;

namespace TimeNest.Api.Features.Payments;

// Persistent pending rows are the work queue. Every transition is safe to repeat across instances.
public sealed class CheckoutReconciler(IServiceScopeFactory scopes, ILogger<CheckoutReconciler> logger,
    IOptions<StripeConnectOptions> options) : BackgroundService
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
                var ids = await (from b in db.Bookings
                    join p in db.CheckoutPayments on b.Id equals p.BookingId into payments
                    from p in payments.DefaultIfEmpty()
                    where b.Status == BookingStatus.Pending && (p == null || p.Status != CheckoutStatus.RecoveryRequired)
                    orderby p == null ? b.HoldExpiresAt : p.LastCheckedAt, b.Id
                    select b.Id).Take(50).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    try
                    {
                        using var work = scopes.CreateScope();
                        await work.ServiceProvider.GetRequiredService<CheckoutService>().Reconcile(id, stoppingToken);
                    }
                    catch (StripeConnectUnavailableException)
                    { logger.LogWarning("Checkout reconciliation deferred for booking {BookingId}.", id); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Provider payloads and hosted URLs are credentials; do not log exception bodies.
                logger.LogError("Checkout reconciliation failed; pending reservations remain held for retry.");
            }
        }
    }
}
