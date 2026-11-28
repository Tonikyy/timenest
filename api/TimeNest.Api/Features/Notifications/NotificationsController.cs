using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Notifications;

public sealed record NotificationView(Guid Id, string Subject, string Body, string Link,
    DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

[ApiController, Authorize, Route("api/notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(TimeNestDbContext database, TimeProvider clock) : ControllerBase
{
    private string RecipientId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 10000) return BadRequest();
        var query = database.Notifications.AsNoTracking().Where(x => x.RecipientId == RecipientId);
        var total = await query.CountAsync(ct);
        var unread = await query.CountAsync(x => x.ReadAt == null, ct);
        var items = await query.OrderByDescending(x => x.Sequence).Skip((page - 1) * 20).Take(20)
            .Select(x => new NotificationView(x.Id, x.Subject, x.Body, x.Link, x.CreatedAt, x.ReadAt)).ToArrayAsync(ct);
        return Ok(new { items, total, unread, page, pageSize = 20 });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        var owned = database.Notifications.Where(x => x.Id == id && x.RecipientId == RecipientId);
        await owned.Where(x => x.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, clock.GetUtcNow()), ct);
        var item = await owned.Select(x => new { x.Id, x.ReadAt }).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(item);
    }
}
