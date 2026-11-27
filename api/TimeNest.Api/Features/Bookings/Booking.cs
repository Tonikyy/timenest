namespace TimeNest.Api.Features.Bookings;

public enum BookingStatus { Pending, Confirmed, Expired }

public sealed class Booking
{
    public Guid Id { get; set; }
    public Guid VacancyId { get; set; }
    public Guid BusinessId { get; set; }
    public required string CustomerId { get; set; }
    public required string Title { get; set; }
    public required string BusinessName { get; set; }
    public required string Address { get; set; }
    public required string City { get; set; }
    public required string ServiceTypeId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public int PriceCents { get; set; }
    public int FeeCents { get; set; }
    public string Currency { get; set; } = "eur";
    public required string RecipientAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset HoldExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }
    public BookingStatus Status { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] Version { get; set; } = [];
}
