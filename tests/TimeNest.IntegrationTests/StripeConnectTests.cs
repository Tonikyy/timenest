using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Businesses;
using TimeNest.Api.Features.Payments;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class StripeConnectTests(ApiFixture fixture)
{
    private const string Path = "/api/business/payments";
    private const string LinkPath = Path + "/onboarding-link";

    [Fact]
    public async Task Start_requires_login_csrf_and_a_demo_business()
    {
        using var owner = new AccountClient(fixture);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Http.GetAsync(Path)).StatusCode);
        await owner.Register(); await owner.Login();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Http.PostAsJsonAsync(LinkPath, new { })).StatusCode);
        Assert.Equal("noBusiness", (await Status(owner)).State);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Post(LinkPath, new { })).StatusCode);
        var business = await Profile(owner);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        await db.Businesses.Where(x => x.Id == business.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDemo, false));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Post(LinkPath, new { })).StatusCode);
        Assert.False(await db.StripeConnections.AnyAsync(x => x.BusinessId == business.Id));
    }

    [Fact]
    public async Task Onboarding_uses_owned_account_trusted_urls_and_current_provider_state()
    {
        using var owner = await Owner();
        using var other = await Owner();
        Assert.Equal("notStarted", (await Status(owner)).State);
        var response = await owner.Post(LinkPath, new { accountId = "acct_attacker", returnUrl = "https://attacker.test" });
        response.EnsureSuccessStatusCode();
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(fixture.Stripe.LinkUrl, (await response.Content.ReadFromJsonAsync<OnboardingLink>())!.Url);
        var id = await StoredAccount(owner);
        Assert.Equal("incomplete", (await Status(owner)).State);
        // Visiting/forging the return query cannot mark the account ready.
        Assert.Equal("incomplete", (await owner.Http.GetFromJsonAsync<OnboardingStatus>(Path + "?stripe=return"))!.State);
        Assert.Equal("notStarted", (await Status(other)).State);
        var linkRequest = fixture.Stripe.Requests.Last(x => x.Path == "/v1/account_links");
        var fields = QueryHelpers.ParseQuery(linkRequest.Body);
        Assert.Equal(id, fields["account"]);
        Assert.Equal("http://localhost:4200/business/payments?stripe=return", fields["return_url"]);
        Assert.Equal("http://localhost:4200/business/payments?stripe=refresh", fields["refresh_url"]);
        Assert.Equal("account_onboarding", fields["type"]);
        fixture.Stripe.Accounts[id] = new ConnectedAccount(id, true, true, true, true);
        Assert.Equal("ready", (await Status(owner)).State);
        fixture.Stripe.Accounts[id] = new ConnectedAccount(id, true, true, false, true);
        Assert.Equal("incomplete", (await Status(owner)).State);
        fixture.Stripe.FailReads = true;
        try
        {
            var failed = await owner.Http.GetAsync(Path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.DoesNotContain("sensitive-provider-detail", await failed.Content.ReadAsStringAsync());
        }
        finally { fixture.Stripe.FailReads = false; }
    }

    [Fact]
    public async Task Concurrent_starts_and_fresh_links_keep_one_account()
    {
        using var first = await Owner();
        using var second = new AccountClient(fixture);
        (await second.Post("/api/account/login", new { first.Email, AccountClient.Password })).EnsureSuccessStatusCode();
        var responses = await Task.WhenAll(first.Post(LinkPath, new { }), second.Post(LinkPath, new { }));
        Assert.All(responses, response => response.EnsureSuccessStatusCode());
        var business = (await first.Http.GetFromJsonAsync<BusinessProfileResponse>("/api/business"))!;
        var requests = fixture.Stripe.Requests.Where(x => x.Path == "/v1/accounts" && x.Key == $"timenest-connect-v1-{business.Id:N}").ToArray();
        Assert.NotEmpty(requests);
        Assert.Single(requests.Select(x => x.Body).Distinct());
        var fields = QueryHelpers.ParseQuery(requests[0].Body);
        Assert.Equal("FI", fields["country"]);
        Assert.Equal("eur", fields["default_currency"]);
        Assert.Equal("express", fields["type"]);
        Assert.Equal("true", fields["capabilities[transfers][requested]"]);
        Assert.Equal("true", fields["capabilities[card_payments][requested]"]);
        Assert.Equal(business.Id.ToString("N"), fields["metadata[timenest_business_id]"]);
        var id = await StoredAccount(first);
        (await first.Post(LinkPath, new { })).EnsureSuccessStatusCode();
        Assert.Equal(id, await StoredAccount(first));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<TimeNestDbContext>().StripeConnections
            .Where(x => x.BusinessId == business.Id).ToListAsync());
    }

    [Fact]
    public async Task Lost_creation_response_is_recovered_using_the_same_durable_key()
    {
        using var owner = await Owner();
        fixture.Stripe.FailAfterCreation = true;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.Post(LinkPath, new { })).StatusCode);
            Assert.Equal("pendingCreation", (await Status(owner)).State);
        }
        finally { fixture.Stripe.FailAfterCreation = false; }
        var business = (await owner.Http.GetFromJsonAsync<BusinessProfileResponse>("/api/business"))!;
        var providerId = fixture.Stripe.AccountsByKey[$"timenest-connect-v1-{business.Id:N}"];
        (await owner.Post(LinkPath, new { })).EnsureSuccessStatusCode();
        Assert.Equal(providerId, await StoredAccount(owner));
    }

    [Fact]
    public async Task Link_failure_retains_account_and_retry_never_recreates_it()
    {
        using var owner = await Owner();
        fixture.Stripe.FailLinks = true;
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.Post(LinkPath, new { })).StatusCode); }
        finally { fixture.Stripe.FailLinks = false; }
        var id = await StoredAccount(owner);
        var creationCount = fixture.Stripe.Requests.Count(x => x.Path == "/v1/accounts");
        (await owner.Post(LinkPath, new { })).EnsureSuccessStatusCode();
        Assert.Equal(id, await StoredAccount(owner));
        Assert.Equal(creationCount, fixture.Stripe.Requests.Count(x => x.Path == "/v1/accounts"));
    }

    [Fact]
    public async Task Uncertain_creation_outside_idempotency_window_requires_reconciliation()
    {
        using var owner = await Owner();
        var business = (await owner.Http.GetFromJsonAsync<BusinessProfileResponse>("/api/business"))!;
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        db.StripeConnections.Add(new StripeConnection { BusinessId = business.Id, CreationStartedAt = ApiFixture.Now.AddHours(-23) });
        await db.SaveChangesAsync();
        var requestCount = fixture.Stripe.Requests.Count;
        Assert.Equal("recoveryRequired", (await Status(owner)).State);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Post(LinkPath, new { })).StatusCode);
        Assert.Equal(requestCount, fixture.Stripe.Requests.Count);
    }

    [Theory]
    [InlineData("https://attacker.test/setup")]
    [InlineData("http://connect.stripe.com/setup")]
    [InlineData("https://connect.stripe.com.attacker.test/setup")]
    public async Task Unexpected_provider_link_is_never_sent_to_browser(string url)
    {
        using var owner = await Owner();
        var original = fixture.Stripe.LinkUrl;
        fixture.Stripe.LinkUrl = url;
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.Post(LinkPath, new { })).StatusCode); }
        finally { fixture.Stripe.LinkUrl = original; }
    }

    [Theory]
    [InlineData("sk_live_dummy", "https://example.test", false)]
    [InlineData("rk_live_dummy", "https://example.test", false)]
    [InlineData("sk_test_dummy", "https://example.test/redirect", false)]
    [InlineData("sk_test_dummy", "https://user@example.test", false)]
    [InlineData("sk_test_dummy", "http://example.test", false)]
    [InlineData("sk_test_dummy", "http://localhost:4200", true)]
    [InlineData("sk_test_dummy", "https://example.test", true)]
    public void Configuration_rejects_live_keys_and_untrusted_origins(string key, string origin, bool valid) =>
        Assert.Equal(valid, new StripeConnectOptions { SecretKey = key, PublicOrigin = origin }.IsValid(allowLocalHttp: true));

    [Fact]
    public void Disabled_integration_does_not_require_a_production_origin() =>
        Assert.True(new StripeConnectOptions().IsValid(allowLocalHttp: false));

    [Fact]
    public async Task Missing_key_is_unavailable_without_creating_a_connection_or_calling_Stripe()
    {
        using var owner = await Owner();
        await using var disabled = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.PostConfigure<StripeConnectOptions>(settings => settings.SecretKey = "")));
        using var client = new AccountClient(fixture, disabled.CreateClient());
        (await client.Post("/api/account/login", new { owner.Email, AccountClient.Password })).EnsureSuccessStatusCode();
        var count = fixture.Stripe.Requests.Count;
        Assert.Equal("unavailable", (await Status(client)).State);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.Post(LinkPath, new { })).StatusCode);
        Assert.Equal(count, fixture.Stripe.Requests.Count);
        Assert.Equal("notStarted", (await Status(owner)).State);
    }
    private static async Task<OnboardingStatus> Status(AccountClient owner) =>
        (await owner.Http.GetFromJsonAsync<OnboardingStatus>(Path))!;
    private static async Task<BusinessProfileResponse> Profile(AccountClient owner) =>
        (await (await owner.Post("/api/business", BusinessTests.Profile())).Content.ReadFromJsonAsync<BusinessProfileResponse>())!;
    private async Task<AccountClient> Owner()
    {
        var owner = new AccountClient(fixture);
        await owner.Register(); await owner.Login(); await Profile(owner);
        return owner;
    }
    private async Task<string> StoredAccount(AccountClient owner)
    {
        var business = (await owner.Http.GetFromJsonAsync<BusinessProfileResponse>("/api/business"))!;
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<TimeNestDbContext>().StripeConnections
            .SingleAsync(x => x.BusinessId == business.Id)).AccountId!;
    }
}
