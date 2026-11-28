using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Bookings;
using TimeNest.Api.Features.Payments;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class RefundTests(ApiFixture fixture)
{
    private async Task<Guid> Cancel()
    {
        var (customer, owner, id, _) = await CancellationTests.Create(fixture);
        using (customer) using (owner)
            (await owner.Post($"/api/business/bookings/{id}/cancel", new { })).EnsureSuccessStatusCode();
        return id;
    }
    private async Task Reconcile(Guid id, int hours = 0)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        await new RefundService(scope.ServiceProvider.GetRequiredService<TimeNestDbContext>(),
            scope.ServiceProvider.GetRequiredService<IStripeRefundGateway>(), new LaterClock(hours)).Reconcile(id, default);
    }
    private sealed class LaterClock(int hours) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ApiFixture.Now.AddHours(hours);
    }
    private async Task<PaymentRefund> Read(Guid id)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TimeNestDbContext>().PaymentRefunds.AsNoTracking().SingleAsync(x => x.BookingId == id);
    }
    private async Task<HttpResponseMessage> Webhook(TestRefund refund, string type = "refund.updated", bool valid = true, bool live = false)
    {
        var body = JsonSerializer.Serialize(new { id = "evt_refund", @object = "event", type, livemode = live,
            data = new { @object = new { id = refund.Id, @object = "refund" } } });
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_local_stub_only"),
            Encoding.UTF8.GetBytes($"{time}.{body}")));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/stripe/webhook")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", $"t={time},v1={(valid ? signature : "bad")}");
        return await fixture.Client.SendAsync(request);
    }

    [Fact]
    public async Task Concurrent_retries_create_one_full_refund_reverse_transfer_and_return_fee()
    {
        var id = await Cancel();
        await Task.WhenAll(Reconcile(id), Reconcile(id));
        var request = fixture.Stripe.Requests.Where(x => x.Key == $"timenest-refund-v1-{id:N}").ToArray();
        Assert.Single(request.Select(x => x.Body).Distinct());
        var fields = QueryHelpers.ParseQuery(request[0].Body);
        Assert.Equal("4500", fields["amount"]);
        Assert.Equal("true", fields["reverse_transfer"]);
        Assert.Equal("true", fields["refund_application_fee"]);
        Assert.Equal(RefundStatus.Succeeded, (await Read(id)).Status);
        var remote = fixture.Stripe.RefundsByKey[$"timenest-refund-v1-{id:N}"];
        var callbacks = await Task.WhenAll(Webhook(remote), Webhook(remote, "refund.created"));
        Assert.All(callbacks, x => x.EnsureSuccessStatusCode());
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        Assert.Equal(BookingStatus.Cancelled, (await db.Bookings.FindAsync(id))!.Status);
        Assert.Equal(CheckoutStatus.Paid, (await db.CheckoutPayments.FindAsync(id))!.Status);
        Assert.Equal(1, await db.PaymentRefunds.CountAsync(x => x.BookingId == id));
    }

    [Fact]
    public async Task Lost_creation_response_reuses_original_refund_and_payload_after_restart()
    {
        var id = await Cancel();
        fixture.Stripe.LoseRefundResponse = true;
        try { await Assert.ThrowsAsync<StripeConnectUnavailableException>(() => Reconcile(id)); }
        finally { fixture.Stripe.LoseRefundResponse = false; }
        var unknown = await Read(id);
        Assert.NotNull(unknown.AttemptedAt); Assert.Null(unknown.StripeRefundId);
        Assert.Equal(RefundStatus.Queued, unknown.Status);
        await Reconcile(id);
        Assert.Equal(fixture.Stripe.RefundsByKey[$"timenest-refund-v1-{id:N}"].Id, (await Read(id)).StripeRefundId);
        Assert.Single(fixture.Stripe.Requests.Where(x => x.Key == $"timenest-refund-v1-{id:N}").Select(x => x.Body).Distinct());
    }

    [Fact]
    public async Task Pending_refund_and_unavailable_provider_are_not_reported_completed()
    {
        var id = await Cancel();
        fixture.Stripe.NewRefundStatus = "pending";
        try { await Reconcile(id); }
        finally { fixture.Stripe.NewRefundStatus = "succeeded"; }
        Assert.Equal(RefundStatus.Pending, (await Read(id)).Status);
        fixture.Stripe.FailRefundReads = true;
        try { await Assert.ThrowsAsync<StripeConnectUnavailableException>(() => Reconcile(id)); }
        finally { fixture.Stripe.FailRefundReads = false; }
        Assert.Equal(RefundStatus.Pending, (await Read(id)).Status);
        var remote = fixture.Stripe.RefundsByKey[$"timenest-refund-v1-{id:N}"];
        remote.Status = "succeeded";
        (await Webhook(remote, "refund.failed")).EnsureSuccessStatusCode();
        Assert.Equal(RefundStatus.Succeeded, (await Read(id)).Status);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("payment")]
    [InlineData("reversal")]
    public async Task Mismatched_provider_details_cannot_confirm_refund(string mismatch)
    {
        var id = await Cancel();
        fixture.Stripe.NewRefundStatus = "pending";
        try { await Reconcile(id); }
        finally { fixture.Stripe.NewRefundStatus = "succeeded"; }
        var remote = fixture.Stripe.RefundsByKey[$"timenest-refund-v1-{id:N}"];
        remote.Status = "succeeded";
        if (mismatch == "amount") remote.Amount++;
        if (mismatch == "currency") remote.Currency = "usd";
        if (mismatch == "payment") remote.PaymentIntent = "pi_other";
        if (mismatch == "reversal") remote.Reversal = null;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Webhook(remote)).StatusCode);
        Assert.Equal(RefundStatus.Pending, (await Read(id)).Status);
    }

    [Fact]
    public async Task Invalid_signatures_and_live_callbacks_are_rejected()
    {
        var id = await Cancel(); await Reconcile(id);
        var remote = fixture.Stripe.RefundsByKey[$"timenest-refund-v1-{id:N}"];
        Assert.Equal(HttpStatusCode.BadRequest, (await Webhook(remote, valid: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Webhook(remote, live: true)).StatusCode);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("succeeded")]
    public async Task Failed_refund_is_visible_and_never_silently_replaced(string initial)
    {
        var id = await Cancel();
        fixture.Stripe.NewRefundStatus = initial;
        try { await Reconcile(id); }
        finally { fixture.Stripe.NewRefundStatus = "succeeded"; }
        var remote = fixture.Stripe.RefundsByKey[$"timenest-refund-v1-{id:N}"];
        remote.Status = "failed";
        (await Webhook(remote)).EnsureSuccessStatusCode();
        Assert.Equal(RefundStatus.Failed, (await Read(id)).Status);
        var requests = fixture.Stripe.Requests.Count;
        await Reconcile(id);
        Assert.Equal(requests, fixture.Stripe.Requests.Count);
        remote.Status = "pending"; // A delayed provider read must not regress the terminal failure.
        (await Webhook(remote)).EnsureSuccessStatusCode();
        Assert.Equal(RefundStatus.Failed, (await Read(id)).Status);
    }

    [Fact]
    public async Task Unknown_attempt_is_not_recreated_after_key_window_but_signed_callback_can_recover_it()
    {
        var id = await Cancel();
        fixture.Stripe.LoseRefundResponse = true;
        try { await Assert.ThrowsAsync<StripeConnectUnavailableException>(() => Reconcile(id)); }
        finally { fixture.Stripe.LoseRefundResponse = false; }
        var requests = fixture.Stripe.Requests.Count;
        await Reconcile(id, 23);
        Assert.Equal(requests, fixture.Stripe.Requests.Count);
        Assert.Equal(RefundStatus.RecoveryRequired, (await Read(id)).Status);
        (await Webhook(fixture.Stripe.RefundsByKey[$"timenest-refund-v1-{id:N}"], "refund.created")).EnsureSuccessStatusCode();
        Assert.Equal(RefundStatus.Succeeded, (await Read(id)).Status);
    }

    [Fact]
    public async Task Worker_rechecks_new_success_after_long_pending_period_without_webhook()
    {
        var id = await Cancel();
        fixture.Stripe.NewRefundStatus = "pending";
        try { await Reconcile(id); }
        finally { fixture.Stripe.NewRefundStatus = "succeeded"; }
        var remote = fixture.Stripe.RefundsByKey[$"timenest-refund-v1-{id:N}"];
        remote.Status = "succeeded";
        await Reconcile(id, 40 * 24);
        var succeeded = await Read(id);
        Assert.Equal(RefundStatus.Succeeded, succeeded.Status);
        Assert.Equal(ApiFixture.Now.AddDays(40), succeeded.SucceededAt);
        await Reconcile(id, 41 * 24);
        (await Webhook(remote)).EnsureSuccessStatusCode();
        Assert.Equal(succeeded.SucceededAt, (await Read(id)).SucceededAt);
        remote.Status = "failed";

        using var worker = new RefundReconciler(fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new StripeConnectOptions { SecretKey = "sk_test_local_stub_only" }),
            new LaterClock(41 * 24), NullLogger<RefundReconciler>.Instance);
        await worker.StartAsync(default);
        try
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while ((await Read(id)).Status != RefundStatus.Failed && timeout.Elapsed < TimeSpan.FromSeconds(60))
                await Task.Delay(500);
            Assert.Equal(RefundStatus.Failed, (await Read(id)).Status);
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task Monitoring_migration_gives_existing_successes_a_full_window()
    {
        var succeededId = await Cancel();
        await Reconcile(succeededId);
        var queuedId = await Cancel();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        var migrator = db.Database.GetService<IMigrator>();
        var before = await db.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]").SingleAsync();
        try
        {
            // Simulate the existing schema/data rather than merely inspecting generated SQL.
            await migrator.MigrateAsync("20261127221450_BookingCancellations");
            await migrator.MigrateAsync();
            var succeeded = await Read(succeededId);
            Assert.NotNull(succeeded.SucceededAt);
            var after = await db.Database.SqlQueryRaw<DateTimeOffset>(
                "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]").SingleAsync();
            Assert.InRange(succeeded.SucceededAt.Value, before, after);
            Assert.Null((await Read(queuedId)).SucceededAt);
        }
        finally { await migrator.MigrateAsync(); }
    }
}
