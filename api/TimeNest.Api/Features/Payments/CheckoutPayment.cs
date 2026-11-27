namespace TimeNest.Api.Features.Payments;

public enum CheckoutStatus { Creating, Open, Paid, Expired, RecoveryRequired }

public sealed class CheckoutPayment
{
    public Guid BookingId { get; set; }
    public required string PublicOrigin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastCheckedAt { get; set; }
    public string? SessionId { get; set; }
    public string? PaymentIntentId { get; set; }
    public CheckoutStatus Status { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}
