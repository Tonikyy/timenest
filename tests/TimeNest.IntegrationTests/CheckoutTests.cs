using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Bookings;
using TimeNest.Api.Features.Payments;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class CheckoutTests(ApiFixture fixture)
{
    private sealed record Identifier(Guid Id);
    private async Task<(AccountClient Customer, Guid Slot, Guid Booking)> Reserve()
    {
        var customer = new AccountClient(fixture);
        await customer.Register(); await customer.Login();
        var seed = await ReservationTests.Seed(fixture);
        var response = await customer.Post("/api/bookings", new { vacancyId = seed.SlotId, priceCents = 1, feeCents = 0, recipient = "attacker" });
        response.EnsureSuccessStatusCode();
        return (customer, seed.SlotId, (await response.Content.ReadFromJsonAsync<Identifier>())!.Id);
    }
    private async Task<TestCheckout> Open(AccountClient customer, Guid booking)
    {
        (await customer.Post($"/api/bookings/{booking}/checkout", new { })).EnsureSuccessStatusCode();
        return fixture.Stripe.CheckoutsByKey[$"timenest-checkout-v1-{booking:N}"];
    }
    private Task<BookingView?> View(AccountClient customer, Guid id) => customer.Http.GetFromJsonAsync<BookingView>($"/api/bookings/{id}");
    private async Task Reconcile(Guid id, int minutes = 0)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        var clock = new LaterClock(minutes);
        await new CheckoutService(db, new Reservations(db, clock),
            scope.ServiceProvider.GetRequiredService<IStripeConnectGateway>(),
            scope.ServiceProvider.GetRequiredService<IStripeCheckoutGateway>(),
            scope.ServiceProvider.GetRequiredService<IOptions<StripeConnectOptions>>(), clock).Reconcile(id, default);
    }
    private sealed class LaterClock(int minutes) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ApiFixture.Now.AddMinutes(minutes);
    }
    private async Task<HttpResponseMessage> Webhook(TestCheckout session, string type = "checkout.session.completed", bool valid = true, long? timestamp = null)
    {
        var body = JsonSerializer.Serialize(new { id = "evt_test_duplicate", @object = "event", type,
            livemode = false, data = new { @object = new { id = session.Id, @object = "checkout.session" } } });
        var time = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_local_stub_only"),
            Encoding.UTF8.GetBytes($"{time}.{body}")));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/stripe/webhook")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", $"t={time},v1={(valid ? signature : "bad")}");
        return await fixture.Client.SendAsync(request);
    }

    [Fact]
    public async Task Checkout_uses_snapshots_and_duplicate_requests_create_one_session()
    {
        var (customer, slot, id) = await Reserve();
        using (customer)
        {
            var results = await Task.WhenAll(customer.Post($"/api/bookings/{id}/checkout", new { }),
                customer.Post($"/api/bookings/{id}/checkout", new { }));
            Assert.All(results, x => x.EnsureSuccessStatusCode());
            var requests = fixture.Stripe.Requests.Where(x => x.Key == $"timenest-checkout-v1-{id:N}").ToArray();
            Assert.Single(requests.Select(x => x.Body).Distinct());
            var fields = QueryHelpers.ParseQuery(requests[0].Body);
            Assert.Equal("4500", fields["line_items[0][price_data][unit_amount]"]);
            Assert.Equal("500", fields["payment_intent_data[application_fee_amount]"]);
            Assert.Equal("eur", fields["line_items[0][price_data][currency]"]);
            Assert.Equal("card", fields["allowed_payment_method_types[0]"]);
            Assert.Equal("false", fields["adaptive_pricing[enabled]"]);
            Assert.Equal(ApiFixture.Now.AddMinutes(35).ToUnixTimeSeconds().ToString(), fields["expires_at"]);
            Assert.Equal("Pending", (await View(customer, id))!.Status);
            var again = await customer.Post("/api/bookings", new { vacancyId = slot });
            Assert.Equal(id, (await again.Content.ReadFromJsonAsync<Identifier>())!.Id);
        }
    }

    [Fact]
    public async Task Lost_creation_response_keeps_hold_and_recovers_original_session()
    {
        var (customer, _, id) = await Reserve();
        using (customer)
        {
            fixture.Stripe.LoseCheckoutResponse = true;
            try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await customer.Post($"/api/bookings/{id}/checkout", new { })).StatusCode); }
            finally { fixture.Stripe.LoseCheckoutResponse = false; }
            var original = fixture.Stripe.CheckoutsByKey[$"timenest-checkout-v1-{id:N}"].Id;
            Assert.Equal(original, (await Open(customer, id)).Id);
            Assert.Single(fixture.Stripe.Requests.Where(x => x.Key == $"timenest-checkout-v1-{id:N}").Select(x => x.Body).Distinct());
            Assert.Equal("Pending", (await View(customer, id))!.Status);
        }
    }

    [Fact]
    public async Task Signed_duplicate_and_out_of_order_events_confirm_once_from_current_provider_state()
    {
        var (customer, slot, id) = await Reserve();
        using (customer)
        {
            var session = await Open(customer, id); session.Pay();
            var results = await Task.WhenAll(Webhook(session), Webhook(session), ReconcileAndRespond(id));
            Assert.All(results, x => x.EnsureSuccessStatusCode());
            var confirmed = (await View(customer, id))!;
            Assert.Equal("Confirmed", confirmed.Status);
            (await Webhook(session, "checkout.session.expired")).EnsureSuccessStatusCode();
            Assert.Equal(confirmed.ConfirmedAt, (await View(customer, id))!.ConfirmedAt);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            Assert.Equal(VacancyStatus.Booked, (await db.Vacancies.FindAsync(slot))!.Status);
            var payment = (await db.CheckoutPayments.FindAsync(id))!;
            Assert.Equal(CheckoutStatus.Paid, payment.Status);
            Assert.NotNull(payment.PaymentIntentId);
        }
    }
    private async Task<HttpResponseMessage> ReconcileAndRespond(Guid id)
    { await Reconcile(id); return new HttpResponseMessage(HttpStatusCode.OK); }

    [Fact]
    public async Task Redirect_and_event_name_cannot_claim_payment_success()
    {
        var (customer, _, id) = await Reserve();
        using (customer)
        {
            var session = await Open(customer, id);
            (await Webhook(session)).EnsureSuccessStatusCode();
            var result = await customer.Http.GetFromJsonAsync<BookingView>($"/api/bookings/{id}?checkout=success&paid=true");
            Assert.Equal("Pending", result!.Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await Webhook(session, valid: false)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Webhook(session, timestamp: DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds())).StatusCode);
        }
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("recipient")]
    [InlineData("fee")]
    [InlineData("currency")]
    [InlineData("live")]
    public async Task Mismatched_payment_cannot_confirm_or_release_the_booking(string mismatch)
    {
        var (customer, _, id) = await Reserve();
        using (customer)
        {
            var session = await Open(customer, id); session.Pay();
            switch (mismatch)
            {
                case "amount": session.Amount = 1; break;
                case "recipient": session.Recipient = "acct_wrong"; break;
                case "fee": session.Fee = 1; break;
                case "currency": session.Currency = "usd"; break;
                case "live": session.Live = true; break;
            }
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Webhook(session)).StatusCode);
            Assert.Equal("Pending", (await View(customer, id))!.Status);
        }
    }

    [Fact]
    public async Task Local_expiry_does_not_release_an_open_or_unreachable_checkout()
    {
        var (customer, _, id) = await Reserve();
        using (customer)
        {
            await Open(customer, id);
            await Reconcile(id, 36);
            Assert.Equal("Pending", (await View(customer, id))!.Status);
            fixture.Stripe.FailCheckoutReads = true;
            try { await Assert.ThrowsAsync<StripeConnectUnavailableException>(() => Reconcile(id, 36)); }
            finally { fixture.Stripe.FailCheckoutReads = false; }
            Assert.Equal("Pending", (await View(customer, id))!.Status);
        }
    }

    [Fact]
    public async Task Verified_expired_checkout_releases_slot_but_preserves_history()
    {
        var (customer, slot, id) = await Reserve();
        using (customer)
        {
            var session = await Open(customer, id); session.Status = "expired";
            (await Webhook(session, "checkout.session.expired")).EnsureSuccessStatusCode();
            (await Webhook(session, "checkout.session.completed")).EnsureSuccessStatusCode();
            Assert.Equal("Expired", (await View(customer, id))!.Status);
            var response = await customer.Post("/api/bookings", new { vacancyId = slot });
            response.EnsureSuccessStatusCode();
            Assert.NotEqual(id, (await response.Content.ReadFromJsonAsync<Identifier>())!.Id);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            Assert.Equal(2, await db.Bookings.CountAsync(x => x.VacancyId == slot));
            Assert.Equal(1, await db.Bookings.CountAsync(x => x.VacancyId == slot && x.IsActive));
        }
    }

    [Fact]
    public async Task Reservation_without_provider_attempt_can_expire_safely()
    {
        var (customer, _, id) = await Reserve();
        using (customer)
        {
            await Reconcile(id, 36);
            Assert.Equal("Expired", (await View(customer, id))!.Status);
            Assert.False(fixture.Stripe.CheckoutsByKey.ContainsKey($"timenest-checkout-v1-{id:N}"));
        }
    }

    [Fact]
    public async Task Uncertain_creation_outside_idempotency_window_requires_recovery()
    {
        var (customer, _, id) = await Reserve();
        using (customer)
        {
            fixture.Stripe.LoseCheckoutResponse = true;
            try { await customer.Post($"/api/bookings/{id}/checkout", new { }); }
            finally { fixture.Stripe.LoseCheckoutResponse = false; }
            var count = fixture.Stripe.Requests.Count;
            await Reconcile(id, 23 * 60);
            Assert.Equal(count, fixture.Stripe.Requests.Count);
            Assert.True((await View(customer, id))!.RecoveryRequired);
            Assert.Equal("Pending", (await View(customer, id))!.Status);
        }
    }

    [Fact]
    public async Task Bookings_require_ownership_authentication_and_csrf()
    {
        var (customer, slot, id) = await Reserve();
        using (customer)
        using (var other = new AccountClient(fixture))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await other.Http.GetAsync($"/api/bookings/{id}")).StatusCode);
            await other.Register(); await other.Login();
            Assert.Equal(HttpStatusCode.NotFound, (await other.Http.GetAsync($"/api/bookings/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/bookings/{id}/checkout", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/bookings/{id}/refresh", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.Http.PostAsJsonAsync($"/api/bookings/{id}/checkout", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await other.Post("/api/bookings", new { vacancyId = slot })).StatusCode);
            var list = await other.Http.GetStringAsync("/api/bookings");
            Assert.DoesNotContain(id.ToString(), list);
        }
    }
}
