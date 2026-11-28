namespace TimeNest.Api.Features.Notifications;

public enum NotificationKind { BookingConfirmed, BookingCancelled, RefundSucceeded, RefundFailed, RefundNeedsAttention }
public enum NotificationAudience { Customer, Business }
public enum NotificationEmailStatus { Pending, Delivered, Failed, Unavailable }

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Sequence { get; set; }
    public Guid BookingId { get; set; }
    public required string RecipientId { get; set; }
    public string? RecipientEmail { get; set; }
    public NotificationAudience Audience { get; set; }
    public NotificationKind Kind { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public required string Link { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public NotificationEmailStatus EmailStatus { get; set; }
    public int EmailAttempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? EmailDeliveredAt { get; set; }
}
