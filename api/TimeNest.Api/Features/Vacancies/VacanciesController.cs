using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Vacancies;

[ApiController]
[Route("api/vacancies")]
public sealed class VacanciesController(TimeNestDbContext database, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<VacancySearchResponse>> Search(
        [FromQuery] VacancySearch search, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var origin = new Point(search.Longitude, search.Latitude) { SRID = 4326 };
        var query = database.Vacancies.AsNoTracking()
            .Where(slot => slot.Status == VacancyStatus.Available && slot.StartsAt > now)
            .Where(slot => slot.Business.Location.Distance(origin) <= search.RadiusKm * 1000);

        if (!string.IsNullOrWhiteSpace(search.ServiceTypeId))
        {
            query = query.Where(slot => slot.ServiceTypeId == search.ServiceTypeId);
        }
        if (search.From.HasValue)
        {
            query = query.Where(slot => slot.StartsAt >= search.From.Value);
        }
        if (search.To.HasValue)
        {
            query = query.Where(slot => slot.StartsAt < search.To.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(slot => slot.Business.Location.Distance(origin))
            .ThenBy(slot => slot.StartsAt)
            .ThenBy(slot => slot.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(slot => new VacancyResponse(
                slot.Id, slot.Title, slot.Description, slot.ServiceTypeId, slot.ServiceType.Name,
                slot.Business.Name, slot.Business.Address, slot.Business.City, slot.StartsAt,
                slot.DurationMinutes, slot.PriceCents, "EUR",
                slot.Business.Location.Distance(origin) / 1000, slot.Business.IsDemo))
            .ToListAsync(cancellationToken);

        return Ok(new VacancySearchResponse(items, total, search.Page, search.PageSize));
    }
}
