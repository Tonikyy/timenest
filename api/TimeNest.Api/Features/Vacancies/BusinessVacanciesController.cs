using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Vacancies;

[ApiController]
[Authorize]
[Route("api/business/vacancies")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BusinessVacanciesController(TimeNestDbContext database, TimeProvider clock) : ControllerBase
{
    private static readonly TimeZoneInfo Helsinki = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<ActionResult<ManagedVacancyPage>> List([FromQuery] BusinessVacancyQuery search, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var query = database.Vacancies.AsNoTracking().Where(slot => slot.Business.OwnerId == OwnerId);
        var total = await query.CountAsync(cancellationToken);
        var slots = await query.OrderBy(slot => slot.StartsAt < now).ThenBy(slot => slot.StartsAt).ThenBy(slot => slot.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToListAsync(cancellationToken);
        return Ok(new ManagedVacancyPage(slots.Select(ManagedVacancy.From).ToList(), total, search.Page, search.PageSize));
    }

    [HttpPost]
    public async Task<IActionResult> Publish(PublishVacancyRequest request, CancellationToken cancellationToken)
    {
        var business = await database.Businesses.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerId == OwnerId, cancellationToken);
        if (business is null) return Problem(statusCode: 409, title: "Luo ensin yritysprofiili.");
        if (!business.IsDemo) return Problem(statusCode: 409, title: "Vain esittelyaikojen julkaisu on käytössä.");
        var startsAt = await Validate(request, cancellationToken);
        if (startsAt is null) return ValidationProblem();
        var slot = new Vacancy
        {
            Id = Guid.NewGuid(), BusinessId = business.Id,
            Title = request.Title.Trim(), Description = request.Description.Trim(),
            ServiceTypeId = request.ServiceTypeId, StartsAt = startsAt.Value,
            DurationMinutes = request.DurationMinutes, PriceCents = request.PriceCents
        };
        database.Vacancies.Add(slot);
        await database.SaveChangesAsync(cancellationToken);
        return StatusCode(201, ManagedVacancy.From(slot));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, PublishVacancyRequest request, CancellationToken cancellationToken)
    {
        if (request.Version is null) return Problem(statusCode: 400, title: "Ajan versio puuttuu.");
        var slot = await FindOwned(id, cancellationToken);
        if (slot is null) return NotFound();
        if (slot.Status != VacancyStatus.Available || slot.StartsAt <= clock.GetUtcNow())
            return Problem(statusCode: 409, title: "Vain tulevaa vapaata aikaa voi muokata.");
        var startsAt = await Validate(request, cancellationToken);
        if (startsAt is null) return ValidationProblem();
        database.Entry(slot).Property(item => item.Version).OriginalValue = request.Version;
        slot.Title = request.Title.Trim(); slot.Description = request.Description.Trim();
        slot.ServiceTypeId = request.ServiceTypeId; slot.StartsAt = startsAt.Value;
        slot.DurationMinutes = request.DurationMinutes; slot.PriceCents = request.PriceCents;
        return await Save(slot, cancellationToken);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelVacancyRequest request, CancellationToken cancellationToken)
    {
        var slot = await FindOwned(id, cancellationToken);
        if (slot is null) return NotFound();
        if (slot.Status == VacancyStatus.Cancelled) return Ok(ManagedVacancy.From(slot));
        if (slot.Status != VacancyStatus.Available || slot.StartsAt <= clock.GetUtcNow())
            return Problem(statusCode: 409, title: "Tätä aikaa ei voi perua tässä näkymässä.");
        database.Entry(slot).Property(item => item.Version).OriginalValue = request.Version;
        slot.Status = VacancyStatus.Cancelled; slot.CancelledAt = clock.GetUtcNow();
        return await Save(slot, cancellationToken);
    }

    private Task<Vacancy?> FindOwned(Guid id, CancellationToken cancellationToken) =>
        database.Vacancies.SingleOrDefaultAsync(slot => slot.Id == id && slot.Business.OwnerId == OwnerId && slot.Business.IsDemo, cancellationToken);

    private async Task<IActionResult> Save(Vacancy slot, CancellationToken cancellationToken)
    {
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(statusCode: 409, title: "Aika on muuttunut. Lataa lista uudelleen ennen muutoksia.");
        }
        return Ok(ManagedVacancy.From(slot));
    }

    private async Task<DateTimeOffset?> Validate(PublishVacancyRequest request, CancellationToken cancellationToken)
    {
        var local = request.Date!.Value.ToDateTime(request.Time!.Value, DateTimeKind.Unspecified);
        if (local <= TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Helsinki).DateTime)
            ModelState.AddModelError(nameof(request.Date), "Ajan tulee olla tulevaisuudessa.");
        if (request.Time!.Value.Ticks % TimeSpan.TicksPerMinute != 0)
            ModelState.AddModelError(nameof(request.Time), "Anna kellonaika kokonaisina minuutteina.");
        if (Helsinki.IsInvalidTime(local) || Helsinki.IsAmbiguousTime(local))
            ModelState.AddModelError(nameof(request.Time), "Kellonaika ei ole yksiselitteinen kesäajan siirtymän vuoksi. Valitse toinen kellonaika.");
        if (!await database.ServiceTypes.AnyAsync(type => type.Id == request.ServiceTypeId, cancellationToken))
            ModelState.AddModelError(nameof(request.ServiceTypeId), "Valitse olemassa oleva palveluluokka.");
        if (!ModelState.IsValid) return null;
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Helsinki));
    }
}
