using TimeNest.Api.Features.Businesses;

namespace TimeNest.Api.Features.Vacancies;

public enum VacancyStatus { Available, Held, Booked, Cancelled }

public sealed class Vacancy
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public required string ServiceTypeId { get; set; }
    public ServiceType ServiceType { get; set; } = null!;
    public required string Title { get; set; }
    public required string Description { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public int PriceCents { get; set; }
    public VacancyStatus Status { get; set; } = VacancyStatus.Available;
}
