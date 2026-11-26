using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TimeNest.Api.Features.Businesses;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.Api.Data;

public static class DemoData
{
    public static async Task SeedAsync(TimeNestDbContext database, TimeProvider clock)
    {
        // Seed only an empty database. Never move appointments that someone may have interacted with.
        if (await database.Businesses.AnyAsync())
        {
            return;
        }

        database.ServiceTypes.AddRange(
            new ServiceType { Id = "hair", Name = "Hiukset" },
            new ServiceType { Id = "massage", Name = "Hieronta" },
            new ServiceType { Id = "beauty", Name = "Kauneus" },
            new ServiceType { Id = "wellbeing", Name = "Hyvinvointi" });

        var businesses = new[]
        {
            Business("10000000-0000-0000-0000-000000000001", "Studio Hohde", "Annankatu 12", "Helsinki", 24.9392, 60.1641),
            Business("10000000-0000-0000-0000-000000000002", "Hetki Hieronta", "Fredrikinkatu 35", "Helsinki", 24.9336, 60.1668),
            Business("10000000-0000-0000-0000-000000000003", "Kauneushuone Lumi", "Korkeavuorenkatu 21", "Helsinki", 24.9470, 60.1625),
            Business("10000000-0000-0000-0000-000000000004", "Oma Hetki", "Tapiolan keskustorni", "Espoo", 24.8052, 60.1769)
        };
        database.Businesses.AddRange(businesses);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
        var tomorrow = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).Date.AddDays(1);
        var openings = new[]
        {
            (0, "hair", "Leikkaus & viimeistely", "Uusi ilme tai tuttu leikkaus. Aikaan sisältyy pesu, leikkaus ja viimeistely.", 45, 4500, 0, 10),
            (1, "massage", "Klassinen hieronta", "Rauhallinen hetki kehonhuollolle. Hieronta mukautetaan toiveisiisi.", 60, 6000, 0, 12),
            (2, "beauty", "Kosteuttava kasvohoito", "Hellävarainen puhdistus, kosteuttava naamio ja kasvohieronta.", 60, 7500, 0, 14),
            (0, "hair", "Hiusten leikkaus", "Huoliteltu leikkaus arkeen. Kerro paikan päällä toiveesi.", 30, 3500, 1, 11),
            (1, "massage", "Niska-hartiahieronta", "Täsmähoitoa niskan ja hartioiden kireyteen.", 30, 3800, 1, 15),
            (3, "wellbeing", "Rentouttava jalkahoito", "Palauttava hoito jaloille rauhallisessa ympäristössä.", 45, 5500, 2, 10),
            (2, "beauty", "Kulmien muotoilu", "Kulmien muotoilu ja värjäys toiveidesi mukaan.", 30, 3000, 2, 13),
            (3, "massage", "Palauttava hieronta", "Varaa aikaa palautumiselle ja kehon hyvinvoinnille.", 60, 6500, 3, 14)
        };

        foreach (var (business, service, title, description, duration, price, day, hour) in openings)
        {
            var localTime = DateTime.SpecifyKind(tomorrow.AddDays(day).AddHours(hour), DateTimeKind.Unspecified);
            database.Vacancies.Add(new Vacancy
            {
                Id = Guid.NewGuid(),
                BusinessId = businesses[business].Id,
                ServiceTypeId = service,
                Title = title,
                Description = description,
                StartsAt = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localTime, zone)),
                DurationMinutes = duration,
                PriceCents = price
            });
        }

        await database.SaveChangesAsync();
    }

    private static Business Business(string id, string name, string address, string city, double longitude, double latitude) =>
        new()
        {
            Id = Guid.Parse(id),
            Name = name,
            Address = address,
            City = city,
            Location = new Point(longitude, latitude) { SRID = 4326 },
            IsDemo = true
        };
}
