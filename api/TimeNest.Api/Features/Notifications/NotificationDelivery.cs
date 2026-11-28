using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Notifications;

public sealed class NotificationDelivery(TimeNestDbContext database, INotificationEmail email,
    TimeProvider clock, ILogger<NotificationDelivery> logger)
{
    public async Task<bool> DeliverNext(CancellationToken ct)
    {
        if (!email.IsAvailable) return false;
        database.ChangeTracker.Clear();
        Notification? notification;
        var now = clock.GetUtcNow();
        var lease = Guid.NewGuid();
        await using (var tx = await database.Database.BeginTransactionAsync(ct))
        {
            notification = await database.Notifications.FromSqlInterpolated($"""
                SELECT TOP (1) n.* FROM Notifications n WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE n.EmailStatus = 'Pending' AND n.NextAttemptAt <= {now}
                    AND (n.LeaseExpiresAt IS NULL OR n.LeaseExpiresAt <= {now})
                    AND NOT EXISTS (SELECT 1 FROM Notifications earlier
                        WHERE earlier.BookingId = n.BookingId AND earlier.RecipientId = n.RecipientId
                            AND earlier.Sequence < n.Sequence AND earlier.EmailStatus = 'Pending')
                ORDER BY n.Sequence
                """).SingleOrDefaultAsync(ct);
            if (notification is null) return false;
            // A crash during the last attempt must eventually become an inspectable failure.
            if (notification.EmailAttempts >= 5)
            {
                notification.EmailStatus = NotificationEmailStatus.Failed;
                notification.LeaseId = null; notification.LeaseExpiresAt = null;
                await database.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                logger.LogWarning("Notification {NotificationId} exhausted delivery attempts.", notification.Id);
                return true;
            }
            notification.EmailAttempts++;
            notification.LeaseId = lease;
            notification.LeaseExpiresAt = now.AddMinutes(2);
            await database.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }

        var delivered = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await email.Send(notification, timeout.Token); delivered = true; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Provider exceptions may contain addresses or content; log only the safe identifier.
            logger.LogWarning("Notification {NotificationId} delivery attempt failed.", notification.Id);
        }
        now = clock.GetUtcNow();
        var status = delivered ? NotificationEmailStatus.Delivered :
            notification.EmailAttempts >= 5 ? NotificationEmailStatus.Failed : NotificationEmailStatus.Pending;
        var next = now.AddSeconds(30 * Math.Pow(2, notification.EmailAttempts - 1));
        await database.Notifications.Where(x => x.Id == notification.Id && x.LeaseId == lease)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EmailStatus, status)
                .SetProperty(x => x.EmailDeliveredAt, delivered ? (DateTimeOffset?)now : null)
                .SetProperty(x => x.NextAttemptAt, next)
                .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), ct);
        return true;
    }
}
