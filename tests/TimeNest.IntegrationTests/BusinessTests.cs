using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Accounts;
using TimeNest.Api.Features.Businesses;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class BusinessTests(ApiFixture fixture)
{
    public static object Profile(string name = "Fictional business", byte[]? version = null) =>
        new { name, address = "Demo street 1", city = "Demo city", latitude = 0.0, longitude = 0.0, version };

    [Fact]
    public async Task Profile_requires_login_and_mutations_require_csrf()
    {
        using var account = new AccountClient(fixture);
        Assert.Equal(HttpStatusCode.Unauthorized, (await account.Http.GetAsync("/api/business")).StatusCode);
        await account.Register();
        await account.Login();
        Assert.Equal(HttpStatusCode.BadRequest, (await account.Http.PostAsJsonAsync("/api/business", Profile())).StatusCode);
        Assert.Equal("null", await account.Http.GetStringAsync("/api/business"));
    }

    [Fact]
    public async Task Ownership_and_demo_status_are_set_by_the_server()
    {
        using var owner = new AccountClient(fixture);
        using var other = new AccountClient(fixture);
        await owner.Register(); await owner.Login();
        await other.Register(); await other.Login();
        var otherUser = await other.Http.GetFromJsonAsync<AccountResponse>("/api/account/me");
        var created = await owner.Post("/api/business", new
        {
            name = "Fictional owner", address = "Demo 1", city = "Demo", latitude = 0, longitude = 0,
            ownerId = otherUser!.Id, isDemo = false
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var profile = (await created.Content.ReadFromJsonAsync<BusinessProfileResponse>())!;
        Assert.True(profile.IsDemo);
        Assert.Equal(8, profile.Version.Length);
        Assert.Equal("null", await other.Http.GetStringAsync("/api/business"));
        Assert.Equal(HttpStatusCode.NotFound, (await Put(other, Profile("Stolen", profile.Version))).StatusCode);
        Assert.Equal("Fictional owner", (await owner.Http.GetFromJsonAsync<BusinessProfileResponse>("/api/business"))!.Name);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<TimeNestDbContext>().Businesses.SingleAsync(item => item.Id == profile.Id);
        var ownerUser = await owner.Http.GetFromJsonAsync<AccountResponse>("/api/account/me");
        Assert.Equal(ownerUser!.Id, stored.OwnerId);
    }

    [Fact]
    public async Task Concurrent_profile_creation_creates_only_one_business()
    {
        using var first = new AccountClient(fixture);
        using var second = new AccountClient(fixture);
        await first.Register(); await first.Login();
        (await second.Post("/api/account/login", new { first.Email, AccountClient.Password })).EnsureSuccessStatusCode();
        var results = await Task.WhenAll(first.Post("/api/business", Profile("First")), second.Post("/api/business", Profile("Second")));
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Stale_profile_edits_do_not_overwrite_saved_changes()
    {
        using var account = new AccountClient(fixture);
        await account.Register(); await account.Login();
        var created = await account.Post("/api/business", Profile());
        var original = (await created.Content.ReadFromJsonAsync<BusinessProfileResponse>())!;
        (await Put(account, Profile("Saved edit", original.Version))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Put(account, Profile("Stale edit", original.Version))).StatusCode);
        Assert.Equal("Saved edit", (await account.Http.GetFromJsonAsync<BusinessProfileResponse>("/api/business"))!.Name);
    }

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("Valid", 91, 0)]
    [InlineData("Valid", 0, -181)]
    public async Task Invalid_profile_details_are_rejected(string name, double latitude, double longitude)
    {
        using var account = new AccountClient(fixture);
        await account.Register(); await account.Login();
        var response = await account.Post("/api/business", new { name, address = "Demo", city = "Demo", latitude, longitude });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Put(AccountClient account, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/business") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", await account.Csrf());
        return await account.Http.SendAsync(request);
    }
}
