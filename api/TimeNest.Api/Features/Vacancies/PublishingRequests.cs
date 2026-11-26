using System.ComponentModel.DataAnnotations;

namespace TimeNest.Api.Features.Vacancies;

public sealed record PublishVacancyRequest(
    [Required, StringLength(160)] string Title,
    [Required, StringLength(1000)] string Description,
    [Required, StringLength(40)] string ServiceTypeId,
    [Required] DateOnly? Date,
    [Required] TimeOnly? Time,
    [Range(5, 480)] int DurationMinutes,
    [Range(1, int.MaxValue)] int PriceCents,
    [MinLength(8), MaxLength(8)] byte[]? Version = null);

public sealed record CancelVacancyRequest([Required, MinLength(8), MaxLength(8)] byte[] Version);
public sealed class BusinessVacancyQuery
{
    [Range(1, 10000)] public int Page { get; init; } = 1;
    [Range(1, 50)] public int PageSize { get; init; } = 12;
}
public sealed record ManagedVacancy(
    Guid Id, string Title, string Description, string ServiceTypeId, DateTimeOffset StartsAt,
    int DurationMinutes, int PriceCents, string Status, DateTimeOffset? CancelledAt, byte[] Version)
{
    public static ManagedVacancy From(Vacancy slot) =>
        new(slot.Id, slot.Title, slot.Description, slot.ServiceTypeId, slot.StartsAt, slot.DurationMinutes,
            slot.PriceCents, slot.Status.ToString(), slot.CancelledAt, slot.Version);
}
public sealed record ManagedVacancyPage(IReadOnlyList<ManagedVacancy> Items, int Total, int Page, int PageSize);
