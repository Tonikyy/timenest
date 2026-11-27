using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Payments;

namespace TimeNest.Api.Features.Bookings;

public sealed record ReserveRequest(Guid VacancyId);
public sealed record BookingView(Guid Id, string Title, string BusinessName, string Address, string City,
    DateTimeOffset StartsAt, int DurationMinutes, int PriceCents, int FeeCents, string Currency,
    string Status, DateTimeOffset HoldExpiresAt, DateTimeOffset CheckoutStartBefore, DateTimeOffset? ConfirmedAt, bool RecoveryRequired)
{
    public static BookingView From(Booking b, bool recovery = false) => new(b.Id, b.Title, b.BusinessName, b.Address,
        b.City, b.StartsAt, b.DurationMinutes, b.PriceCents, b.FeeCents, b.Currency, b.Status.ToString(),
        b.HoldExpiresAt, b.CreatedAt.AddMinutes(4), b.ConfirmedAt, recovery);
}

[ApiController, Authorize, Route("api/bookings")]
[EnableRateLimiting("checkout")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BookingsController(TimeNestDbContext database, CheckoutService checkout) : ControllerBase
{
    private string CustomerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost]
    public async Task<IActionResult> Reserve(ReserveRequest request, CancellationToken ct)
    {
        try { return Ok(new { id = await checkout.Start(request.VacancyId, CustomerId, ct) }); }
        catch (BookingConflictException e) { return Conflict(new { code = e.Code }); }
        catch (StripeConnectUnavailableException) { return Unavailable(); }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 10000) return BadRequest();
        var query = database.Bookings.AsNoTracking().Where(x => x.CustomerId == CustomerId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return Ok(new { items = rows.Select(x => BookingView.From(x)), total, page, pageSize = 20 });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var booking = await Owned(id, ct);
        if (booking is null) return NotFound();
        var recovery = await database.CheckoutPayments.AnyAsync(x => x.BookingId == id && x.Status == CheckoutStatus.RecoveryRequired, ct);
        return Ok(BookingView.From(booking, recovery));
    }

    [HttpPost("{id:guid}/checkout")]
    public async Task<IActionResult> Continue(Guid id, CancellationToken ct)
    {
        if (await Owned(id, ct) is null) return NotFound();
        try { return Ok(new { url = await checkout.Continue(id, ct) }); }
        catch (BookingConflictException e) { return Conflict(new { code = e.Code }); }
        catch (StripeConnectUnavailableException) { return Unavailable(); }
    }

    [HttpPost("{id:guid}/refresh")]
    public async Task<IActionResult> Refresh(Guid id, CancellationToken ct)
    {
        if (await Owned(id, ct) is null) return NotFound();
        try { await checkout.Reconcile(id, ct); return await Get(id, ct); }
        catch (StripeConnectUnavailableException) { return Unavailable(); }
    }

    private Task<Booking?> Owned(Guid id, CancellationToken ct) => database.Bookings.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == CustomerId, ct);
    private ObjectResult Unavailable() => Problem(statusCode: 503, title: "Maksun tilannetta ei voitu varmistaa. Yritä uudelleen.");
}
