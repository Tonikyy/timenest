using System.ComponentModel.DataAnnotations;

namespace TimeNest.Api.Features.Vacancies;

public sealed class VacancySearch : IValidatableObject
{
    [Range(-90, 90)]
    public double Latitude { get; init; } = 60.1699;

    [Range(-180, 180)]
    public double Longitude { get; init; } = 24.9384;

    [Range(1, 100)]
    public int RadiusKm { get; init; } = 10;

    [StringLength(40)]
    public string? ServiceTypeId { get; init; }

    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    [Range(1, 10000)]
    public int Page { get; init; } = 1;

    [Range(1, 50)]
    public int PageSize { get; init; } = 12;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!double.IsFinite(Latitude) || !double.IsFinite(Longitude))
        {
            yield return new ValidationResult("Coordinates must be finite.", [nameof(Latitude), nameof(Longitude)]);
        }

        if (From.HasValue && To.HasValue && To <= From)
        {
            yield return new ValidationResult("To must be later than From.", [nameof(To)]);
        }
    }
}
