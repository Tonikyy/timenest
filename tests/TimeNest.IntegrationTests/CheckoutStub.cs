using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;

namespace TimeNest.IntegrationTests;

public sealed partial class StripeStub
{
    public ConcurrentDictionary<string, TestCheckout> CheckoutsByKey { get; } = new();
    public ConcurrentDictionary<string, TestCheckout> Checkouts { get; } = new();
    public bool LoseCheckoutResponse { get; set; }
    public bool FailCheckoutReads { get; set; }

    private HttpResponseMessage? Checkout(string path, HttpMethod method, string body, string? key)
    {
        if (path == "/v1/checkout/sessions" && method == HttpMethod.Post)
        {
            var form = QueryHelpers.ParseQuery(body);
            var item = CheckoutsByKey.GetOrAdd(key!, _ => new TestCheckout
            {
                Id = "cs_test_" + Guid.NewGuid().ToString("N"), Booking = form["client_reference_id"].ToString(),
                Amount = long.Parse(form["line_items[0][price_data][unit_amount]"].ToString()),
                Fee = long.Parse(form["payment_intent_data[application_fee_amount]"].ToString()),
                Recipient = form["payment_intent_data[transfer_data][destination]"].ToString(),
                Currency = form["line_items[0][price_data][currency]"].ToString(),
                Expires = long.Parse(form["expires_at"].ToString())
            });
            Checkouts[item.Id] = item;
            if (LoseCheckoutResponse) throw new HttpRequestException("Simulated lost checkout response.");
            return Json(item.Payload());
        }
        if (path.StartsWith("/v1/checkout/sessions/") && method == HttpMethod.Get)
            return FailCheckoutReads ? Failure() : Json(Checkouts[path.Split('/').Last()].Payload());
        return null;
    }
}

public sealed class TestCheckout
{
    public required string Id { get; init; }
    public required string Booking { get; init; }
    public required string Recipient { get; set; }
    public required string Currency { get; set; }
    public long Amount { get; set; }
    public long Fee { get; set; }
    public long Expires { get; init; }
    public string Status { get; set; } = "open";
    public string PaymentStatus { get; set; } = "unpaid";
    public bool Live { get; set; }
    public object Payload() => new
    {
        id = Id, @object = "checkout.session", mode = "payment", livemode = Live,
        client_reference_id = Booking, metadata = new { timenest_booking_id = Booking },
        amount_total = Amount, currency = Currency, status = Status, payment_status = PaymentStatus,
        expires_at = Expires, url = "https://checkout.stripe.com/c/pay/test-only",
        payment_intent = PaymentStatus != "paid" ? null : new
        {
            id = "pi_" + Id, @object = "payment_intent", livemode = Live, status = "succeeded",
            amount = Amount, amount_received = Amount, currency = Currency, application_fee_amount = Fee,
            transfer_data = new { destination = Recipient }, metadata = new { timenest_booking_id = Booking }
        }
    };
    public void Pay() { Status = "complete"; PaymentStatus = "paid"; }
}
