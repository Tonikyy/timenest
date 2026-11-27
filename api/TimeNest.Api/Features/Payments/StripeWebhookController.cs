using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe;

namespace TimeNest.Api.Features.Payments;

[ApiController, AllowAnonymous, Route("api/payments/stripe/webhook")]
[IgnoreAntiforgeryToken, RequestSizeLimit(131072)]
public sealed class StripeWebhookController(CheckoutService checkout, IOptions<StripeConnectOptions> options) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.WebhookSecret)) return StatusCode(503);
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        Event stripeEvent;
        try { stripeEvent = EventUtility.ConstructEvent(body, Request.Headers["Stripe-Signature"],
            options.Value.WebhookSecret, throwOnApiVersionMismatch: false); }
        catch (StripeException) { return BadRequest(); }
        catch (ArgumentException) { return BadRequest(); }

        // Destination-charge sessions belong to the platform, not connected-account event streams.
        if (stripeEvent.Livemode || stripeEvent.Account is not null) return BadRequest();
        if (stripeEvent.Type is not ("checkout.session.completed" or "checkout.session.expired" or
            "checkout.session.async_payment_succeeded" or "checkout.session.async_payment_failed")) return Ok();
        if (stripeEvent.Data.Object is not Stripe.Checkout.Session session) return BadRequest();
        try { await checkout.Receive(session.Id, ct); return Ok(); }
        catch (StripeConnectUnavailableException) { return StatusCode(503); }
    }
}
