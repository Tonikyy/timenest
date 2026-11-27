using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;

namespace TimeNest.IntegrationTests;

public sealed partial class StripeStub
{
    public ConcurrentDictionary<string, TestRefund> RefundsByKey { get; } = new();
    public ConcurrentDictionary<string, TestRefund> Refunds { get; } = new();
    public bool LoseRefundResponse { get; set; }
    public bool FailRefundReads { get; set; }
    public string NewRefundStatus { get; set; } = "succeeded";

    private HttpResponseMessage? Refund(string path, HttpMethod method, string body, string? key)
    {
        if (path == "/v1/refunds" && method == HttpMethod.Post)
        {
            var form = QueryHelpers.ParseQuery(body);
            var item = RefundsByKey.GetOrAdd(key!, _ => new TestRefund
            {
                Id = "re_" + Guid.NewGuid().ToString("N"), Booking = form["metadata[timenest_booking_id]"].ToString(),
                PaymentIntent = form["payment_intent"].ToString(), Amount = long.Parse(form["amount"].ToString()),
                Status = NewRefundStatus
            });
            Refunds[item.Id] = item;
            if (LoseRefundResponse) throw new HttpRequestException("Simulated lost refund response.");
            return Json(item.Payload());
        }
        if (path.StartsWith("/v1/refunds/") && method == HttpMethod.Get)
            return FailRefundReads ? Failure() : Json(Refunds[path.Split('/').Last()].Payload());
        return null;
    }
}

public sealed class TestRefund
{
    public required string Id { get; init; }
    public required string Booking { get; init; }
    public required string PaymentIntent { get; set; }
    public long Amount { get; set; }
    public string Currency { get; set; } = "eur";
    public string Status { get; set; } = "succeeded";
    public string? Reversal { get; set; } = "trr_test";
    public object Payload() => new
    {
        id = Id, @object = "refund", payment_intent = PaymentIntent, amount = Amount, currency = Currency,
        status = Status, metadata = new { timenest_booking_id = Booking }, transfer_reversal = Reversal
    };
}
