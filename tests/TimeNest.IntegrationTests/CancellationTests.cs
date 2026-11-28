using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Bookings;
using TimeNest.Api.Features.Payments;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class CancellationTests(ApiFixture fixture)
{
    internal static async Task<(AccountClient Customer, AccountClient Owner, Guid Id, Guid Slot)> Create(ApiFixture fixture, bool paid = true)
    {
        var customer = new AccountClient(fixture); await customer.Register(); await customer.Login();
        var owner = new AccountClient(fixture); await owner.Register(); await owner.Login();
        var seed = await ReservationTests.Seed(fixture);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            var ownerId = await db.Users.Where(x => x.Email == owner.Email).Select(x => x.Id).SingleAsync();
            var slot = await db.Vacancies.Include(x => x.Business).SingleAsync(x => x.Id == seed.SlotId);
            slot.Business.OwnerId = ownerId; await db.SaveChangesAsync();
        }
        var response = await customer.Post("/api/bookings", new { vacancyId = seed.SlotId });
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<Identifier>())!.Id;
        if (paid)
        {
            (await customer.Post($"/api/bookings/{id}/checkout", new { })).EnsureSuccessStatusCode();
            fixture.Stripe.CheckoutsByKey[$"timenest-checkout-v1-{id:N}"].Pay();
            (await customer.Post($"/api/bookings/{id}/refresh", new { })).EnsureSuccessStatusCode();
        }
        return (customer, owner, id, seed.SlotId);
    }
    private sealed record Identifier(Guid Id);

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 4500)]
    [InlineData(true, false, 4500)]
    public async Task Cancellation_preserves_policy_and_payment_and_is_duplicate_safe(bool business, bool legacy, int amount)
    {
        var (customer, owner, id, slot) = await Create(fixture);
        using (customer) using (owner)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            if (legacy) await db.Bookings.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CancellationPolicy, CancellationPolicy.LegacyFullRefundBeforeStart));
            var actor = business ? owner : customer;
            var url = business ? $"/api/business/bookings/{id}/cancel" : $"/api/bookings/{id}/cancel";
            var results = await Task.WhenAll(actor.Post(url, new { refundAmountCents = 99999 }), actor.Post(url, new { }));
            Assert.All(results, x => x.EnsureSuccessStatusCode());
            var view = (await results[0].Content.ReadFromJsonAsync<BookingView>())!;
            Assert.Equal("Cancelled", view.Status);
            Assert.Equal(amount, view.Cancellation!.RefundAmountCents);
            Assert.Equal(amount == 0 ? null : "Queued", view.Refund?.Status);
            Assert.Equal(1, await db.BookingCancellations.CountAsync(x => x.BookingId == id));
            Assert.Equal(amount == 0 ? 0 : 1, await db.PaymentRefunds.CountAsync(x => x.BookingId == id));
            Assert.Equal(CheckoutStatus.Paid, (await db.CheckoutPayments.FindAsync(id))!.Status);
            Assert.Equal(business ? VacancyStatus.Cancelled : VacancyStatus.Available, (await db.Vacancies.FindAsync(slot))!.Status);
            if (!business)
            {
                (await customer.Post("/api/bookings", new { vacancyId = slot })).EnsureSuccessStatusCode();
                Assert.Equal(2, await db.Bookings.CountAsync(x => x.VacancyId == slot));
            }
        }
    }

    [Fact]
    public async Task Cancellation_enforces_ownership_csrf_and_customer_start_cutoff()
    {
        var (customer, owner, id, _) = await Create(fixture);
        using (customer) using (owner)
        using (var other = new AccountClient(fixture))
        {
            await other.Register(); await other.Login();
            Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/bookings/{id}/cancel", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/bookings/{id}/abandon", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.Http.PostAsJsonAsync($"/api/bookings/{id}/abandon", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/business/bookings/{id}/cancel", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.Http.GetAsync($"/api/business/bookings/{id}")).StatusCode);
            Assert.DoesNotContain(id.ToString(), await other.Http.GetStringAsync("/api/business/bookings"));
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.Http.PostAsJsonAsync($"/api/bookings/{id}/cancel", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.Http.PostAsJsonAsync($"/api/business/bookings/{id}/cancel", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.PostAsJsonAsync($"/api/bookings/{id}/cancel", new { })).StatusCode);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            Assert.True((await customer.Http.GetFromJsonAsync<BookingView>($"/api/bookings/{id}"))!.CanCancel);
            await db.Bookings.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.StartsAt, ApiFixture.Now));
            Assert.False((await customer.Http.GetFromJsonAsync<BookingView>($"/api/bookings/{id}"))!.CanCancel);
            Assert.True((await owner.Http.GetFromJsonAsync<BookingView>($"/api/business/bookings/{id}"))!.CanCancel);
            Assert.Equal(HttpStatusCode.Conflict, (await customer.Post($"/api/bookings/{id}/cancel", new { })).StatusCode);
            (await owner.Post($"/api/business/bookings/{id}/cancel", new { })).EnsureSuccessStatusCode();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unpaid_abandonment_closes_checkout_before_releasing_the_slot(bool open)
    {
        var (customer, owner, id, slot) = await Create(fixture, false);
        using (customer) using (owner)
        {
            if (open) (await customer.Post($"/api/bookings/{id}/checkout", new { })).EnsureSuccessStatusCode();
            var response = await customer.Post($"/api/bookings/{id}/abandon", new { });
            response.EnsureSuccessStatusCode();
            Assert.Equal("Cancelled", (await response.Content.ReadFromJsonAsync<BookingView>())!.Status);
            (await customer.Post($"/api/bookings/{id}/abandon", new { })).EnsureSuccessStatusCode();
            if (open) Assert.Equal("expired", fixture.Stripe.CheckoutsByKey[$"timenest-checkout-v1-{id:N}"].Status);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            Assert.Equal(VacancyStatus.Available, (await db.Vacancies.FindAsync(slot))!.Status);
            Assert.False(await db.PaymentRefunds.AnyAsync(x => x.BookingId == id));
        }
    }

    [Fact]
    public async Task Unreachable_expiry_retains_hold_and_blocks_checkout_until_reconciled()
    {
        var (customer, owner, id, slot) = await Create(fixture, false);
        using (customer) using (owner)
        {
            (await customer.Post($"/api/bookings/{id}/checkout", new { })).EnsureSuccessStatusCode();
            fixture.Stripe.FailCheckoutExpiry = true;
            try
            {
                var response = await customer.Post($"/api/bookings/{id}/abandon", new { });
                response.EnsureSuccessStatusCode();
                var view = (await response.Content.ReadFromJsonAsync<BookingView>())!;
                Assert.Equal("Pending", view.Status); Assert.True(view.CancellationRequested);
                var continuation = await customer.Post($"/api/bookings/{id}/checkout", new { });
                Assert.Contains("\"url\":null", await continuation.Content.ReadAsStringAsync());
                await using var scope = fixture.Factory.Services.CreateAsyncScope();
                Assert.Equal(VacancyStatus.Held, (await scope.ServiceProvider.GetRequiredService<TimeNestDbContext>().Vacancies.FindAsync(slot))!.Status);
            }
            finally { fixture.Stripe.FailCheckoutExpiry = false; }
            var refreshed = await customer.Post($"/api/bookings/{id}/refresh", new { });
            Assert.Equal("Cancelled", (await refreshed.Content.ReadFromJsonAsync<BookingView>())!.Status);
        }
    }

    [Fact]
    public async Task Payment_winning_abandonment_race_stays_confirmed_until_explicit_paid_cancellation()
    {
        var (customer, owner, id, _) = await Create(fixture, false);
        using (customer) using (owner)
        {
            (await customer.Post($"/api/bookings/{id}/checkout", new { })).EnsureSuccessStatusCode();
            fixture.Stripe.PayDuringExpiry = true;
            try
            {
                var response = await customer.Post($"/api/bookings/{id}/abandon", new { });
                response.EnsureSuccessStatusCode();
                Assert.Equal("Confirmed", (await response.Content.ReadFromJsonAsync<BookingView>())!.Status);
            }
            finally { fixture.Stripe.PayDuringExpiry = false; }
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            Assert.False(await db.BookingCancellations.AnyAsync(x => x.BookingId == id));
            Assert.False(await db.PaymentRefunds.AnyAsync(x => x.BookingId == id));
        }
    }
}
