using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Payments;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.Api.Features.Bookings;

public sealed class Cancellations(TimeNestDbContext database, TimeProvider clock)
{
    public async Task<bool> Cancel(Guid id, string userId, bool business, CancellationToken ct)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var booking = await database.Bookings
            .FromSqlInterpolated($"SELECT * FROM Bookings WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}")
            .SingleOrDefaultAsync(ct);
        if (booking is null || (business
            ? !await database.Businesses.AnyAsync(x => x.Id == booking.BusinessId && x.OwnerId == userId && x.IsDemo, ct)
            : booking.CustomerId != userId)) return false;
        if (booking.Status == BookingStatus.Cancelled) return true;
        if (booking.Status != BookingStatus.Confirmed)
            throw new BookingConflictException("notConfirmed");
        var now = clock.GetUtcNow();
        if (!business && booking.StartsAt <= now) throw new BookingConflictException("alreadyStarted");
        var payment = await database.CheckoutPayments.SingleOrDefaultAsync(x => x.BookingId == id, ct);
        if (payment?.Status != CheckoutStatus.Paid || payment.PaymentIntentId is null)
            throw new BookingConflictException("paymentUnverified");
        var amount = business || booking.CancellationPolicy == CancellationPolicy.LegacyFullRefundBeforeStart
            ? booking.PriceCents : 0;
        database.BookingCancellations.Add(new BookingCancellation
        {
            BookingId = id, Actor = business ? CancellationActor.Business : CancellationActor.Customer,
            CancelledAt = now, RefundAmountCents = amount
        });
        if (amount > 0)
            database.PaymentRefunds.Add(new PaymentRefund
            {
                BookingId = id, PaymentIntentId = payment.PaymentIntentId, AmountCents = amount,
                Currency = booking.Currency, CreatedAt = now
            });
        // Cancellation releases only this booking's slot; refund completion is an independent obligation.
        var changed = await database.Vacancies.Where(x => x.Id == booking.VacancyId && x.Status == VacancyStatus.Booked)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, business ? VacancyStatus.Cancelled : VacancyStatus.Available)
                .SetProperty(x => x.CancelledAt, business ? now : (DateTimeOffset?)null), ct);
        if (changed != 1) throw new BookingConflictException("slotChanged");
        booking.Status = BookingStatus.Cancelled; booking.IsActive = false;
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
