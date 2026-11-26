namespace TimeNest.Api.Features.Vacancies;

public sealed record VacancyResponse(
    Guid Id,
    string Title,
    string Description,
    string ServiceTypeId,
    string ServiceType,
    string BusinessName,
    string Address,
    string City,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    int PriceCents,
    string Currency,
    double DistanceKm,
    bool IsDemo);

public sealed record VacancySearchResponse(IReadOnlyList<VacancyResponse> Items, int Total, int Page, int PageSize);
