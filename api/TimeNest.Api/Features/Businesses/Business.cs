using NetTopologySuite.Geometries;

namespace TimeNest.Api.Features.Businesses;

public sealed class Business
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public required string City { get; set; }
    public required Point Location { get; set; }
    public bool IsDemo { get; set; }
}
