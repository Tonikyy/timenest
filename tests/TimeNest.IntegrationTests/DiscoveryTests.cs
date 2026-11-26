using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.IntegrationTests;

public sealed class DiscoveryTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Search_excludes_past_held_booked_cancelled_and_distant_slots()
    {
        var result = await Search();
        Assert.Equal(3, result.Total);
        Assert.All(result.Items, slot =>
        {
            Assert.True(slot.StartsAt > ApiFixture.Now);
            Assert.InRange(slot.DistanceKm, 0, 10);
            Assert.True(slot.IsDemo);
            Assert.Equal("EUR", slot.Currency);
            Assert.Equal(4500, slot.PriceCents);
        });
        Assert.Equal(["Demo Center", "Demo Center", "Demo Nearby"], result.Items.Select(slot => slot.BusinessName));
        Assert.InRange(result.Items[2].DistanceKm, 1, 2);
    }

    [Fact]
    public async Task Search_filters_service_and_exclusive_date_range()
    {
        var from = Uri.EscapeDataString(ApiFixture.Now.AddHours(1).ToString("O"));
        var to = Uri.EscapeDataString(ApiFixture.Now.AddHours(24).ToString("O"));
        var result = await Search($"?serviceTypeId=hair&from={from}&to={to}");
        var slot = Assert.Single(result.Items);
        Assert.Equal("Hiukset", slot.ServiceType);
        Assert.Equal("Demo Center", slot.BusinessName);
    }

    [Fact]
    public async Task Radius_is_computed_in_meters_by_sql_server()
    {
        var result = await Search("?radiusKm=1");
        Assert.Equal(2, result.Total);
        Assert.All(result.Items, slot => Assert.Equal("Demo Center", slot.BusinessName));
    }

    [Fact]
    public async Task Pagination_has_stable_order_and_no_duplicates()
    {
        var first = await Search("?pageSize=2");
        var second = await Search("?pageSize=2&page=2");
        Assert.Equal(3, first.Total);
        Assert.Equal(3, second.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(3, first.Items.Concat(second.Items).Select(slot => slot.Id).Distinct().Count());
    }

    [Fact]
    public async Task Unknown_service_returns_an_empty_result()
    {
        var result = await Search("?serviceTypeId=unknown");
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData("?radiusKm=0")]
    [InlineData("?radiusKm=101")]
    [InlineData("?latitude=91")]
    [InlineData("?longitude=181")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=51")]
    [InlineData("?from=2027-01-17&to=2027-01-16")]
    [InlineData("?from=not-a-date")]
    public async Task Invalid_search_returns_validation_problem(string query)
    {
        var response = await fixture.Client.GetAsync("/api/vacancies" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task Catalog_and_health_use_the_migrated_database()
    {
        var catalog = await fixture.Client.GetFromJsonAsync<ServiceType[]>("/api/service-types");
        Assert.NotNull(catalog);
        Assert.Equal(2, catalog.Length);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Unavailable_database_fails_readiness_but_not_liveness()
    {
        await using var unavailable = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:TimeNest"] = "Server=127.0.0.1,1;Database=Unavailable;User Id=sa;Password=NotARealPassword!;Connect Timeout=1;Encrypt=True;TrustServerCertificate=True"
                })));
        using var client = unavailable.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        var response = await client.GetAsync("/api/vacancies");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Demo_seed_leaves_an_existing_database_unchanged()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        var before = await database.Vacancies.Select(slot => slot.StartsAt).ToListAsync();
        await DemoData.SeedAsync(database, TimeProvider.System);
        var after = await database.Vacancies.Select(slot => slot.StartsAt).ToListAsync();
        Assert.Equal(before, after);
    }

    private async Task<VacancySearchResponse> Search(string query = "")
    {
        var result = await fixture.Client.GetFromJsonAsync<VacancySearchResponse>("/api/vacancies" + query);
        Assert.NotNull(result);
        return result;
    }
}
