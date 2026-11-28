using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Bookings;

[ApiController, Authorize, Route("api/business/bookings"), EnableRateLimiting("checkout")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BusinessBookingsController(TimeNestDbContext database, Cancellations cancellations, BookingViews views) : ControllerBase
{
    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private IQueryable<Booking> Owned() => database.Bookings.AsNoTracking().Where(b =>
        database.Businesses.Any(x => x.Id == b.BusinessId && x.OwnerId == OwnerId && x.IsDemo) &&
        (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Cancelled));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 10000) return BadRequest();
        var query = Owned();
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.StartsAt).ThenBy(x => x.Id)
            .Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return Ok(new { items = await views.Read(rows, ct), total, page, pageSize = 20 });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var booking = await Owned().SingleOrDefaultAsync(x => x.Id == id, ct);
        return booking is null ? NotFound() : Ok((await views.Read([booking], ct))[0]);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            if (!await cancellations.Cancel(id, OwnerId, true, ct)) return NotFound();
            return await Get(id, ct);
        }
        catch (BookingConflictException e) { return Conflict(new { code = e.Code }); }
    }
}
