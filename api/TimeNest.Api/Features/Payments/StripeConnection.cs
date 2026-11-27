namespace TimeNest.Api.Features.Payments;

public sealed class StripeConnection
{
    public Guid BusinessId { get; set; }
    public DateTimeOffset CreationStartedAt { get; set; }
    public int CreationApiVersion { get; set; } = 2;
    public string? ContactEmail { get; set; }
    public string? AccountId { get; set; }
}
