using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Bookings;

public sealed record CancellationView(string Actor, DateTimeOffset CancelledAt, int RefundAmountCents);
public sealed record RefundView(string Status, int AmountCents);

public sealed class BookingViews(TimeNestDbContext database, TimeProvider clock)
{
    public async Task<BookingView[]> Read(IReadOnlyList<Booking> rows, CancellationToken ct, bool business = false)
    {
        var ids = rows.Select(x => x.Id).ToArray();
        var cancellations = await database.BookingCancellations.AsNoTracking().Where(x => ids.Contains(x.BookingId))
            .ToDictionaryAsync(x => x.BookingId, ct);
        var refunds = await database.PaymentRefunds.AsNoTracking().Where(x => ids.Contains(x.BookingId))
            .ToDictionaryAsync(x => x.BookingId, ct);
        var recovery = await database.CheckoutPayments.Where(x => ids.Contains(x.BookingId) &&
            x.Status == Payments.CheckoutStatus.RecoveryRequired).Select(x => x.BookingId).ToListAsync(ct);
        return rows.Select(b => BookingView.From(b, recovery.Contains(b.Id)) with
        {
            CanCancel = b.Status == BookingStatus.Confirmed && (business || b.StartsAt > clock.GetUtcNow()),
            Cancellation = cancellations.TryGetValue(b.Id, out var c)
                ? new CancellationView(c.Actor.ToString(), c.CancelledAt, c.RefundAmountCents) : null,
            Refund = refunds.TryGetValue(b.Id, out var r) ? new RefundView(r.Status.ToString(), r.AmountCents) : null
        }).ToArray();
    }
}
