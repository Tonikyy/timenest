using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Vacancies;

[ApiController]
[Route("api/service-types")]
public sealed class ServiceTypesController(TimeNestDbContext database) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceType>>> List(CancellationToken cancellationToken)
    {
        return Ok(await database.ServiceTypes.AsNoTracking().OrderBy(type => type.Name).ToListAsync(cancellationToken));
    }
}
