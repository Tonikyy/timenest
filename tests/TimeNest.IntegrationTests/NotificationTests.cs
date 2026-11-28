using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Bookings;
using TimeNest.Api.Features.Notifications;
using TimeNest.Api.Features.Payments;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class NotificationTests(ApiFixture fixture)
{
    private async Task<Notification[]> Read(Guid id)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TimeNestDbContext>().Notifications.AsNoTracking()
            .Where(x => x.BookingId == id).OrderBy(x => x.Sequence).ToArrayAsync();
    }

    [Fact]
    public async Task Verified_payment_and_duplicate_cancellations_notify_both_recipients_once()
    {
        var (customer, owner, id, _) = await CancellationTests.Create(fixture, false);
        using (customer) using (owner)
        {
            Assert.Empty(await Read(id));
            (await customer.Post($"/api/bookings/{id}/checkout", new { })).EnsureSuccessStatusCode();
            Assert.Empty(await Read(id));
            fixture.Stripe.CheckoutsByKey[$"timenest-checkout-v1-{id:N}"].Pay();
            var paid = await Task.WhenAll(customer.Post($"/api/bookings/{id}/refresh", new { }), customer.Post($"/api/bookings/{id}/refresh", new { }));
            Assert.All(paid, x => x.EnsureSuccessStatusCode());
            var confirmed = await Read(id);
            Assert.Equal(2, confirmed.Length);
            Assert.All(confirmed, x => { Assert.Equal(NotificationKind.BookingConfirmed, x.Kind); Assert.Equal(NotificationEmailStatus.Pending, x.EmailStatus); });
            Assert.Equal(customer.Email, Assert.Single(confirmed, x => x.Audience == NotificationAudience.Customer).RecipientEmail);
            Assert.Equal($"/business/bookings/{id}", Assert.Single(confirmed, x => x.Audience == NotificationAudience.Business).Link);
            var cancelled = await Task.WhenAll(owner.Post($"/api/business/bookings/{id}/cancel", new { }), owner.Post($"/api/business/bookings/{id}/cancel", new { }));
            Assert.All(cancelled, x => x.EnsureSuccessStatusCode());
            var messages = await Read(id);
            Assert.Equal(4, messages.Length);
            Assert.All(messages.Where(x => x.Kind == NotificationKind.BookingCancelled), x => Assert.Contains("jonossa", x.Body));
            Assert.True(messages[1].Sequence < messages[2].Sequence);
            Assert.DoesNotContain(customer.Email, messages.Last().Body);
        }
    }

    [Fact]
    public async Task Failed_notification_insert_rolls_back_confirmation_and_retry_recovers()
    {
        var (customer, owner, id, slot) = await CancellationTests.Create(fixture, false);
        using (customer) using (owner)
        {
            (await customer.Post($"/api/bookings/{id}/checkout", new { })).EnsureSuccessStatusCode();
            fixture.Stripe.CheckoutsByKey[$"timenest-checkout-v1-{id:N}"].Pay();
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectNotificationForTest ON Notifications AFTER INSERT AS THROW 51000, 'Simulated notification storage failure', 1;");
            try
            {
                Assert.Equal(HttpStatusCode.InternalServerError, (await customer.Post($"/api/bookings/{id}/refresh", new { })).StatusCode);
                Assert.Equal(BookingStatus.Pending, await db.Bookings.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());
                Assert.Equal(VacancyStatus.Held, await db.Vacancies.Where(x => x.Id == slot).Select(x => x.Status).SingleAsync());
                Assert.Empty(await Read(id));
            }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectNotificationForTest;"); }
            (await customer.Post($"/api/bookings/{id}/refresh", new { })).EnsureSuccessStatusCode();
            Assert.Equal(2, (await Read(id)).Length);
        }
    }

    [Fact]
    public async Task Customer_cancellation_snapshots_no_refund_and_unverified_email_still_has_inbox()
    {
        var (customer, owner, id, _) = await CancellationTests.Create(fixture);
        using (customer) using (owner)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            await db.Users.Where(x => x.Email == owner.Email).ExecuteUpdateAsync(s => s.SetProperty(x => x.EmailConfirmed, false));
            (await customer.Post($"/api/bookings/{id}/cancel", new { })).EnsureSuccessStatusCode();
            var messages = (await Read(id)).Where(x => x.Kind == NotificationKind.BookingCancelled).ToArray();
            Assert.All(messages, x => Assert.Contains("ei palauteta automaattisesti", x.Body));
            var business = Assert.Single(messages, x => x.Audience == NotificationAudience.Business);
            Assert.Equal(NotificationEmailStatus.Unavailable, business.EmailStatus); Assert.Null(business.RecipientEmail);
            await db.Users.Where(x => x.Email == customer.Email).ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, "changed@example.test"));
            Assert.Equal(customer.Email, Assert.Single(await Read(id), x => x.Kind == NotificationKind.BookingCancelled && x.Audience == NotificationAudience.Customer).RecipientEmail);
        }
    }

    [Fact]
    public async Task Refund_success_then_late_failure_notifies_once_for_each_outcome()
    {
        var (customer, owner, id, _) = await CancellationTests.Create(fixture);
        using (customer) using (owner)
            (await owner.Post($"/api/business/bookings/{id}/cancel", new { })).EnsureSuccessStatusCode();
        async Task Reconcile()
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RefundService>().Reconcile(id, default);
        }
        await Task.WhenAll(Reconcile(), Reconcile());
        Assert.Equal(2, (await Read(id)).Count(x => x.Kind == NotificationKind.RefundSucceeded));
        fixture.Stripe.RefundsByKey[$"timenest-refund-v1-{id:N}"].Status = "failed";
        await Task.WhenAll(Reconcile(), Reconcile());
        Assert.Equal(2, (await Read(id)).Count(x => x.Kind == NotificationKind.RefundFailed));
        Assert.Equal(8, (await Read(id)).Length);
    }

    [Fact]
    public async Task Abandoned_unpaid_reservation_does_not_send_booking_notifications()
    {
        var (customer, owner, id, _) = await CancellationTests.Create(fixture, false);
        using (customer) using (owner)
            (await customer.Post($"/api/bookings/{id}/abandon", new { })).EnsureSuccessStatusCode();
        Assert.Empty(await Read(id));
    }

    [Theory]
    [InlineData(0, "28.3.2027 klo 02.30")]
    [InlineData(1, "28.3.2027 klo 04.30")]
    public async Task Message_uses_Helsinki_daylight_saving_and_preserves_booking_snapshot(int hour, string expected)
    {
        var (customer, owner, id, _) = await CancellationTests.Create(fixture, false);
        using (customer) using (owner)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var booking = await db.Bookings.SingleAsync(x => x.Id == id);
            booking.StartsAt = new DateTimeOffset(2027, 3, 28, hour, 30, 0, TimeSpan.Zero);
            await BookingNotifications.Record(db, booking, NotificationKind.BookingConfirmed, ApiFixture.Now, default);
            await db.SaveChangesAsync(); await tx.CommitAsync();
            await db.Bookings.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, "Changed title"));
            Assert.All(await Read(id), x => { Assert.Contains(expected, x.Body); Assert.DoesNotContain("Changed title", x.Body); Assert.DoesNotContain("\uFFFD", x.Body); });
        }
    }
}
