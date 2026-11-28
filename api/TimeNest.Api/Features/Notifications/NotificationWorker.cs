namespace TimeNest.Api.Features.Notifications;

public sealed class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                for (var i = 0; i < 20 && !stoppingToken.IsCancellationRequested; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if (!await scope.ServiceProvider.GetRequiredService<NotificationDelivery>().DeliverNext(stoppingToken)) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Notification delivery will retry after a worker failure."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
