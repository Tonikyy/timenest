using TimeNest.Api.Features.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe.Checkout;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Bookings;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.Api.Features.Payments;

public sealed class CheckoutService(TimeNestDbContext database, Reservations reservations,
    IStripeConnectGateway connect, IStripeCheckoutGateway stripe, IOptions<StripeConnectOptions> options, TimeProvider clock)
{
    public async Task<Guid> Start(Guid vacancyId, string customerId, CancellationToken ct)
    {
        var existing = await database.Bookings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.VacancyId == vacancyId && x.IsActive && x.CustomerId == customerId, ct);
        if (existing is not null) return existing.Id;
        var accountId = await database.StripeConnections.Where(x => database.Vacancies
            .Any(v => v.Id == vacancyId && v.BusinessId == x.BusinessId && v.Business.IsDemo))
            .Select(x => x.AccountId).SingleOrDefaultAsync(ct);
        if (!options.Value.IsConfigured || accountId is null || !(await connect.GetAccount(accountId, ct)).Ready)
            throw new BookingConflictException("paymentsUnavailable");
        return (await reservations.Reserve(vacancyId, customerId, options.Value.ApplicationFeeCents, ct)).Id;
    }

    public async Task Abandon(Guid bookingId, CancellationToken ct)
    {
        database.ChangeTracker.Clear();
        await using (var tx = await database.Database.BeginTransactionAsync(ct))
        {
            var booking = await Locked(bookingId, ct);
            if (booking.Status is BookingStatus.Expired or BookingStatus.Cancelled) return;
            if (booking.Status != BookingStatus.Pending) throw new BookingConflictException("paymentCompleted");
            booking.CheckoutCancellationRequestedAt ??= clock.GetUtcNow();
            await database.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        await Reconcile(bookingId, ct);
    }

    public async Task<string?> Continue(Guid bookingId, CancellationToken ct)
    {
        var snapshot = await database.Bookings.AsNoTracking().SingleAsync(x => x.Id == bookingId, ct);
        if (snapshot.Status == BookingStatus.Pending &&
            !await database.CheckoutPayments.AnyAsync(x => x.BookingId == bookingId, ct) &&
            (!options.Value.IsConfigured || !(await connect.GetAccount(snapshot.RecipientAccountId, ct)).Ready))
            throw new BookingConflictException("paymentsUnavailable");
        database.ChangeTracker.Clear();
        await using (var transaction = await database.Database.BeginTransactionAsync(ct))
        {
            var booking = await Locked(bookingId, ct);
            if (booking.Status != BookingStatus.Pending || booking.CheckoutCancellationRequestedAt is not null) return null;
            var payment = await database.CheckoutPayments.SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
            if (payment is null)
            {
                if (clock.GetUtcNow() >= booking.CreatedAt.AddMinutes(4))
                {
                    await Expire(booking, ct);
                    await database.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return null;
                }
                payment = new CheckoutPayment { BookingId = booking.Id, CreatedAt = clock.GetUtcNow(),
                    PublicOrigin = options.Value.PublicOrigin.TrimEnd('/') };
                database.CheckoutPayments.Add(payment);
                await database.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        return await Reconcile(bookingId, ct);
    }

    public async Task<string?> Reconcile(Guid bookingId, CancellationToken ct)
    {
        var booking = await database.Bookings.AsNoTracking().SingleAsync(x => x.Id == bookingId, ct);
        if (booking.Status != BookingStatus.Pending) return null;
        var payment = await database.CheckoutPayments.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (payment is null)
        {
            if (booking.CheckoutCancellationRequestedAt is null && clock.GetUtcNow() < booking.HoldExpiresAt) return null;
            database.ChangeTracker.Clear();
            await using var tx = await database.Database.BeginTransactionAsync(ct);
            booking = await Locked(bookingId, ct);
            // Serialize expiry with recording the first provider attempt.
            if (booking.Status == BookingStatus.Pending &&
                !await database.CheckoutPayments.AnyAsync(x => x.BookingId == bookingId, ct))
            {
                await Expire(booking, ct);
                await database.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
            return null;
        }
        if (payment.Status == CheckoutStatus.RecoveryRequired) return null;
        await database.CheckoutPayments.Where(x => x.BookingId == bookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastCheckedAt, clock.GetUtcNow()), ct);
        if (payment.SessionId is null && clock.GetUtcNow() - payment.CreatedAt >= TimeSpan.FromHours(23))
        {
            await database.CheckoutPayments.Where(x => x.BookingId == bookingId && x.SessionId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CheckoutStatus.RecoveryRequired), ct);
            return null;
        }
        // A persisted attempt means a session may exist, even when its response was lost.
        var session = payment.SessionId is null
            ? await stripe.Create(booking, payment, ct)
            : await stripe.Get(payment.SessionId, ct);
        if (booking.CheckoutCancellationRequestedAt is not null && session.Status == "open")
        {
            try { await stripe.Expire(session.Id, ct); }
            catch (StripeConnectUnavailableException) { /* Read back: expiry may have succeeded or payment may have won. */ }
            session = await stripe.Get(session.Id, ct);
        }
        await Apply(bookingId, session, ct);
        if (session.Status != "open" || booking.CheckoutCancellationRequestedAt is not null) return null;
        if (!Uri.TryCreate(session.Url, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            url.Host != "checkout.stripe.com" || !url.IsDefaultPort || url.UserInfo.Length != 0)
            throw new StripeConnectUnavailableException();
        return session.Url;
    }

    public async Task Receive(string sessionId, CancellationToken ct)
    {
        var session = await stripe.Get(sessionId, ct);
        if (!Guid.TryParse(session.ClientReferenceId, out var bookingId) ||
            !await database.CheckoutPayments.AnyAsync(x => x.BookingId == bookingId, ct)) return;
        await Apply(bookingId, session, ct);
    }

    private async Task Apply(Guid bookingId, Session session, CancellationToken ct)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var booking = await Locked(bookingId, ct);
        var payment = await database.CheckoutPayments.SingleAsync(x => x.BookingId == bookingId, ct);
        if (session.Livemode || session.Mode != "payment" || session.ClientReferenceId != booking.Id.ToString("N") ||
            session.Metadata?.GetValueOrDefault("timenest_booking_id") != booking.Id.ToString("N") ||
            session.AmountTotal != booking.PriceCents || session.Currency != booking.Currency ||
            (payment.SessionId is not null && payment.SessionId != session.Id))
            throw new StripeConnectUnavailableException();

        // Never allow a stale event to undo a terminal transition.
        if (booking.Status != BookingStatus.Pending) return;
        payment.SessionId = session.Id;
        var intent = session.PaymentIntent;
        if (session.Status == "complete" && session.PaymentStatus == "paid")
        {
            if (intent is null || intent.Livemode || intent.Status != "succeeded" ||
                intent.AmountReceived != booking.PriceCents || intent.Currency != booking.Currency ||
                intent.ApplicationFeeAmount != booking.FeeCents || intent.TransferData?.DestinationId != booking.RecipientAccountId ||
                intent.Metadata?.GetValueOrDefault("timenest_booking_id") != booking.Id.ToString("N"))
                throw new StripeConnectUnavailableException();
            var changed = await database.Vacancies.Where(x => x.Id == booking.VacancyId && x.Status == VacancyStatus.Held)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, VacancyStatus.Booked), ct);
            if (changed != 1) throw new StripeConnectUnavailableException();
            booking.Status = BookingStatus.Confirmed; booking.ConfirmedAt = clock.GetUtcNow();
            payment.Status = CheckoutStatus.Paid; payment.PaidAt = clock.GetUtcNow(); payment.PaymentIntentId = intent.Id;
            await BookingNotifications.Record(database, booking, NotificationKind.BookingConfirmed, clock.GetUtcNow(), ct);
        }
        else if (session.Status == "expired" && session.PaymentStatus == "unpaid" &&
            (intent is null || intent.Status is "canceled" or "requires_payment_method"))
        {
            await Expire(booking, ct);
            payment.Status = CheckoutStatus.Expired;
        }
        else payment.Status = CheckoutStatus.Open;
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private Task<Booking> Locked(Guid id, CancellationToken ct) => database.Bookings
        .FromSqlInterpolated($"SELECT * FROM Bookings WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}").SingleAsync(ct);

    private async Task Expire(Booking booking, CancellationToken ct)
    {
        var changed = await database.Vacancies.Where(x => x.Id == booking.VacancyId && x.Status == VacancyStatus.Held)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, VacancyStatus.Available), ct);
        if (changed != 1) throw new StripeConnectUnavailableException();
        booking.IsActive = false;
        if (booking.CheckoutCancellationRequestedAt is not null)
        {
            booking.Status = BookingStatus.Cancelled;
            database.BookingCancellations.Add(new BookingCancellation
            {
                BookingId = booking.Id, Actor = CancellationActor.Customer, CancelledAt = clock.GetUtcNow()
            });
        }
        else { booking.Status = BookingStatus.Expired; booking.ExpiredAt = clock.GetUtcNow(); }
    }
}
