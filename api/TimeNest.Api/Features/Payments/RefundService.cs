using Microsoft.EntityFrameworkCore;
using Stripe;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Payments;

public sealed class RefundService(TimeNestDbContext database, IStripeRefundGateway stripe, TimeProvider clock)
{
    public async Task Reconcile(Guid id, CancellationToken ct)
    {
        database.ChangeTracker.Clear();
        PaymentRefund? payment;
        await using (var tx = await database.Database.BeginTransactionAsync(ct))
        {
            payment = await Locked(id, ct);
            if (payment is null || payment.Status is RefundStatus.Failed or RefundStatus.RecoveryRequired) return;
            var now = clock.GetUtcNow();
            payment.LastCheckedAt = now;
            if (payment.StripeRefundId is null && payment.AttemptedAt is not null &&
                now - payment.AttemptedAt >= TimeSpan.FromHours(23))
            {
                payment.Status = RefundStatus.RecoveryRequired;
                await database.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
            }
            // Persist before network I/O: an exception does not establish that no refund exists.
            payment.AttemptedAt ??= now;
            await database.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        var fee = await database.Bookings.Where(x => x.Id == id).Select(x => x.FeeCents).SingleAsync(ct);
        var refund = payment.StripeRefundId is null
            ? await stripe.Create(payment, fee > 0, ct) : await stripe.Get(payment.StripeRefundId, ct);
        await Apply(id, refund, ct);
    }

    public async Task Receive(string refundId, CancellationToken ct)
    {
        var refund = await stripe.Get(refundId, ct);
        if (Guid.TryParse(refund.Metadata?.GetValueOrDefault("timenest_booking_id"), out var id))
            await Apply(id, refund, ct);
    }

    private async Task Apply(Guid id, Refund refund, CancellationToken ct)
    {
        database.ChangeTracker.Clear();
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        var payment = await Locked(id, ct);
        if (payment is null) return;
        if (string.IsNullOrWhiteSpace(refund.Id) || refund.Amount != payment.AmountCents ||
            refund.Currency != payment.Currency || refund.PaymentIntentId != payment.PaymentIntentId ||
            refund.Metadata?.GetValueOrDefault("timenest_booking_id") != id.ToString("N") ||
            (payment.StripeRefundId is not null && payment.StripeRefundId != refund.Id) ||
            (refund.Status == "succeeded" && string.IsNullOrWhiteSpace(refund.TransferReversalId)))
            throw new StripeConnectUnavailableException();
        var status = refund.Status switch
        {
            "succeeded" => RefundStatus.Succeeded,
            "failed" or "canceled" => RefundStatus.Failed,
            "pending" => RefundStatus.Pending,
            _ => RefundStatus.RecoveryRequired
        };
        // A delayed pending response cannot undo success. Banks can report a later failure.
        if (payment.Status == RefundStatus.Failed ||
            (payment.Status == RefundStatus.Succeeded && status != RefundStatus.Failed)) return;
        payment.StripeRefundId = refund.Id;
        payment.Status = status;
        if (status == RefundStatus.Succeeded) payment.SucceededAt ??= clock.GetUtcNow();
        await database.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private Task<PaymentRefund?> Locked(Guid id, CancellationToken ct) => database.PaymentRefunds
        .FromSqlInterpolated($"SELECT * FROM PaymentRefunds WITH (UPDLOCK, ROWLOCK) WHERE BookingId = {id}")
        .SingleOrDefaultAsync(ct);
}
