namespace TimeNest.Api.Features.Payments;

public enum RefundStatus { Queued, Pending, Succeeded, Failed, RecoveryRequired }

public sealed class PaymentRefund
{
    public Guid BookingId { get; set; }
    public required string PaymentIntentId { get; set; }
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "eur";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AttemptedAt { get; set; }
    public DateTimeOffset? SucceededAt { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? StripeRefundId { get; set; }
    public RefundStatus Status { get; set; }
}
