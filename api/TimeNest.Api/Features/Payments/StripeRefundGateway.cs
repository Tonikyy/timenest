using Microsoft.Extensions.Options;
using Stripe;

namespace TimeNest.Api.Features.Payments;

public interface IStripeRefundGateway
{
    Task<Refund> Create(PaymentRefund payment, bool refundFee, CancellationToken ct);
    Task<Refund> Get(string id, CancellationToken ct);
}

public sealed class StripeRefundGateway(IOptions<StripeConnectOptions> options, IHttpClientFactory clients) : IStripeRefundGateway
{
    private StripeClient Client() => new(options.Value.SecretKey,
        httpClient: new SystemNetHttpClient(clients.CreateClient("stripe"), maxNetworkRetries: 2));

    public Task<Refund> Create(PaymentRefund payment, bool refundFee, CancellationToken ct) => Call(() =>
        Client().V1.Refunds.CreateAsync(new RefundCreateOptions
        {
            PaymentIntent = payment.PaymentIntentId, Amount = payment.AmountCents,
            ReverseTransfer = true, RefundApplicationFee = refundFee,
            Metadata = new() { ["timenest_booking_id"] = payment.BookingId.ToString("N") }
        }, new RequestOptions { IdempotencyKey = $"timenest-refund-v1-{payment.BookingId:N}" }, ct));

    public Task<Refund> Get(string id, CancellationToken ct) => Call(() =>
        Client().V1.Refunds.GetAsync(id, cancellationToken: ct));

    private static async Task<Refund> Call(Func<Task<Refund>> action)
    {
        try { return await action(); }
        catch (StripeException) { throw new StripeConnectUnavailableException(); }
        catch (HttpRequestException) { throw new StripeConnectUnavailableException(); }
        catch (TaskCanceledException) { throw new StripeConnectUnavailableException(); }
    }
}
