namespace TimeNest.Api.Features.Bookings;

public enum CancellationPolicy { LegacyFullRefundBeforeStart, LastMinuteNoRefund }
public enum CancellationActor { Customer, Business }

public sealed class BookingCancellation
{
    public Guid BookingId { get; set; }
    public CancellationActor Actor { get; set; }
    public DateTimeOffset CancelledAt { get; set; }
    public int RefundAmountCents { get; set; }
}
