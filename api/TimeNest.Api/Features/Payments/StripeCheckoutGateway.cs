using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using TimeNest.Api.Features.Bookings;

namespace TimeNest.Api.Features.Payments;

public interface IStripeCheckoutGateway
{
    Task<Session> Create(Booking booking, CheckoutPayment payment, CancellationToken ct);
    Task<Session> Expire(string sessionId, CancellationToken ct);
    Task<Session> Get(string sessionId, CancellationToken ct);
}

public sealed class StripeCheckoutGateway(IOptions<StripeConnectOptions> options, IHttpClientFactory clients) : IStripeCheckoutGateway
{
    private StripeClient Client() => new(options.Value.SecretKey,
        httpClient: new SystemNetHttpClient(clients.CreateClient("stripe"), maxNetworkRetries: 2));

    public Task<Session> Create(Booking booking, CheckoutPayment payment, CancellationToken ct) => Call(() =>
        Client().V1.Checkout.Sessions.CreateAsync(new SessionCreateOptions
        {
            AdaptivePricing = new SessionAdaptivePricingOptions { Enabled = false },
            Mode = "payment", AllowedPaymentMethodTypes = ["card"], Locale = "fi",
            ClientReferenceId = booking.Id.ToString("N"),
            Metadata = new() { ["timenest_booking_id"] = booking.Id.ToString("N") },
            ExpiresAt = booking.HoldExpiresAt.UtcDateTime,
            SuccessUrl = payment.PublicOrigin + "/bookings/" + booking.Id + "?checkout=return",
            CancelUrl = payment.PublicOrigin + "/bookings/" + booking.Id + "?checkout=cancel",
            LineItems = [new SessionLineItemOptions
            {
                Quantity = 1, PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = booking.Currency, UnitAmount = booking.PriceCents,
                    ProductData = new SessionLineItemPriceDataProductDataOptions { Name = booking.Title }
                }
            }],
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                ApplicationFeeAmount = booking.FeeCents,
                TransferData = new SessionPaymentIntentDataTransferDataOptions { Destination = booking.RecipientAccountId },
                Metadata = new() { ["timenest_booking_id"] = booking.Id.ToString("N") }
            },
            Expand = ["payment_intent"]
        }, new RequestOptions { IdempotencyKey = $"timenest-checkout-v1-{booking.Id:N}" }, ct));

    public Task<Session> Get(string sessionId, CancellationToken ct) => Call(() =>
        Client().V1.Checkout.Sessions.GetAsync(sessionId, new SessionGetOptions { Expand = ["payment_intent"] }, cancellationToken: ct));

    public Task<Session> Expire(string sessionId, CancellationToken ct) => Call(() =>
        Client().V1.Checkout.Sessions.ExpireAsync(sessionId, cancellationToken: ct));

    private static async Task<Session> Call(Func<Task<Session>> action)
    {
        try { return await action(); }
        catch (StripeException) { throw new StripeConnectUnavailableException(); }
        catch (HttpRequestException) { throw new StripeConnectUnavailableException(); }
        catch (TaskCanceledException) { throw new StripeConnectUnavailableException(); }
    }
}
