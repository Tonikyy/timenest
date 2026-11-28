using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using TimeNest.Api.Features.Payments;

namespace TimeNest.IntegrationTests;

// Exercises the real Stripe SDK serialization through HTTP, without credentials or external calls.
public sealed partial class StripeStub : HttpMessageHandler
{
    public ConcurrentDictionary<string, string> AccountsByKey { get; } = new();
    public ConcurrentDictionary<string, ConnectedAccount> Accounts { get; } = new();
    public ConcurrentQueue<(string Path, string Body, string? Key)> Requests { get; } = new();
    public bool FailAfterCreation { get; set; }
    public bool FailLinks { get; set; }
    public bool FailReads { get; set; }
    public string LinkUrl { get; set; } = "https://connect.stripe.com/setup/test-only";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null;
        Requests.Enqueue((path, body, key));
        if (Refund(path, request.Method, body, key) is { } refund) return refund;
        if (Checkout(path, request.Method, body, key) is { } checkout) return checkout;
        if (path == "/v2/core/accounts" && request.Method == HttpMethod.Post)
        {
            var id = AccountsByKey.GetOrAdd(key!, _ => "acct_" + Guid.NewGuid().ToString("N"));
            Accounts.TryAdd(id, new ConnectedAccount(id, false, false, false, false));
            if (FailAfterCreation) throw new HttpRequestException("Simulated lost response after provider success.");
            return Json(new { id, @object = "v2.core.account", livemode = false });
        }
        if (path.StartsWith("/v1/accounts/") && request.Method == HttpMethod.Get)
        {
            if (FailReads) return Failure();
            var account = Accounts[path.Split('/').Last()];
            return Json(new
            {
                id = account.Id, @object = "account", details_submitted = account.DetailsSubmitted,
                charges_enabled = account.ChargesEnabled, payouts_enabled = account.PayoutsEnabled,
                capabilities = new { transfers = account.TransfersActive ? "active" : "inactive" }
            });
        }
        if (path == "/v1/account_links")
        {
            Assert.True(Accounts.ContainsKey(QueryHelpers.ParseQuery(body)["account"].ToString()));
            if (FailLinks) return Failure();
            return Json(new { @object = "account_link", url = LinkUrl });
        }
        throw new InvalidOperationException("Unexpected Stripe request.");
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Failure()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Content = new StringContent("""{"error":{"type":"api_error","message":"sensitive-provider-detail"}}""") };
        response.Headers.Add("Stripe-Should-Retry", "false");
        return response;
    }
}
