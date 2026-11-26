using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Businesses;

[ApiController]
[Authorize]
[Route("api/business")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BusinessController(TimeNestDbContext database) : ControllerBase
{
    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var business = await database.Businesses.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == OwnerId, cancellationToken);
        return new JsonResult(business is null ? null : BusinessProfileResponse.From(business));
    }

    [HttpPost]
    public async Task<IActionResult> Create(BusinessProfileRequest request, CancellationToken cancellationToken)
    {
        var business = new Business
        {
            Id = Guid.NewGuid(), OwnerId = OwnerId, IsDemo = true,
            Name = request.Name.Trim(), Address = request.Address.Trim(), City = request.City.Trim(),
            Location = new Point(request.Longitude!.Value, request.Latitude!.Value) { SRID = 4326 }
        };
        database.Businesses.Add(business);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Problem(statusCode: 409, title: "Tililläsi on jo yritysprofiili. Lataa profiili uudelleen.");
        }
        return Created("/api/business", BusinessProfileResponse.From(business));
    }

    [HttpPut]
    public async Task<IActionResult> Update(BusinessProfileRequest request, CancellationToken cancellationToken)
    {
        if (request.Version is null) return Problem(statusCode: 400, title: "Profiilin versio puuttuu.");
        var business = await database.Businesses
            .SingleOrDefaultAsync(item => item.OwnerId == OwnerId, cancellationToken);
        if (business is null) return NotFound();
        database.Entry(business).Property(item => item.Version).OriginalValue = request.Version;
        business.Name = request.Name.Trim();
        business.Address = request.Address.Trim();
        business.City = request.City.Trim();
        business.Location = new Point(request.Longitude!.Value, request.Latitude!.Value) { SRID = 4326 };
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(statusCode: 409, title: "Profiili on muuttunut. Lataa uusin versio ennen tallentamista.");
        }
        return Ok(BusinessProfileResponse.From(business));
    }
}
