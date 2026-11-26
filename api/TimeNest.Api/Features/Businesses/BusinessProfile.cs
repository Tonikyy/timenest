using System.ComponentModel.DataAnnotations;

namespace TimeNest.Api.Features.Businesses;

public sealed record BusinessProfileRequest(
    [Required, StringLength(160)] string Name,
    [Required, StringLength(240)] string Address,
    [Required, StringLength(100)] string City,
    [Required, Range(-90, 90)] double? Latitude,
    [Required, Range(-180, 180)] double? Longitude,
    [MinLength(8), MaxLength(8)] byte[]? Version = null);

public sealed record BusinessProfileResponse(
    Guid Id, string Name, string Address, string City,
    double Latitude, double Longitude, bool IsDemo, byte[] Version)
{
    public static BusinessProfileResponse From(Business business) =>
        new(business.Id, business.Name, business.Address, business.City,
            business.Location.Y, business.Location.X, business.IsDemo, business.Version);
}
