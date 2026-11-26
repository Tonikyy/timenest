using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using TimeNest.Api.Features.Accounts;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class AccountTests(ApiFixture fixture)
{
    [Fact]
    public async Task Registration_requires_confirmation_then_cookie_login_and_logout_work()
    {
        using var account = new AccountClient(fixture);
        await account.Register(confirm: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await account.Post("/api/account/login", new { account.Email, AccountClient.Password })).StatusCode);
        var query = QueryHelpers.ParseQuery(new Uri(fixture.Mail.Links[account.Email]).Query);
        (await account.Post("/api/account/confirm-email", new { userId = query["userId"].ToString(), code = query["code"].ToString() })).EnsureSuccessStatusCode();
        var login = await account.Post("/api/account/login", new { account.Email, AccountClient.Password });
        login.EnsureSuccessStatusCode();
        var cookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("TimeNest.Session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(account.Email, (await account.Http.GetFromJsonAsync<AccountResponse>("/api/account/me"))!.Email);
        (await account.Post("/api/account/logout", new { })).EnsureSuccessStatusCode();
        Assert.Equal("null", await account.Http.GetStringAsync("/api/account/me"));
    }

    [Theory]
    [InlineData("/api/account/register")]
    [InlineData("/api/account/login")]
    [InlineData("/api/account/forgot-password")]
    public async Task Anonymous_mutations_require_antiforgery(string path)
    {
        using var account = new AccountClient(fixture);
        Assert.Equal(HttpStatusCode.BadRequest, (await account.Http.PostAsJsonAsync(path, new { account.Email, AccountClient.Password })).StatusCode);
    }

    [Fact]
    public async Task Authentication_changes_invalidate_old_csrf_tokens()
    {
        using var account = new AccountClient(fixture);
        await account.Register();
        var anonymousToken = await account.Csrf();
        await account.Login();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/logout") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-XSRF-TOKEN", anonymousToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await account.Http.SendAsync(request)).StatusCode);
        (await account.Post("/api/account/logout", new { })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Password_reset_is_single_use_and_revokes_existing_sessions()
    {
        using var account = new AccountClient(fixture);
        await account.Register();
        await account.Login();
        (await account.Post("/api/account/forgot-password", new { account.Email })).EnsureSuccessStatusCode();
        var query = QueryHelpers.ParseQuery(new Uri(fixture.Mail.Links[account.Email]).Query);
        const string changed = "Another long passphrase!";
        var request = new { account.Email, code = query["code"].ToString(), password = changed };
        (await account.Post("/api/account/reset-password", request)).EnsureSuccessStatusCode();
        Assert.Equal("null", await account.Http.GetStringAsync("/api/account/me"));
        Assert.Equal(HttpStatusCode.BadRequest, (await account.Post("/api/account/reset-password", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await account.Post("/api/account/login", new { account.Email, AccountClient.Password })).StatusCode);
        (await account.Post("/api/account/login", new { account.Email, password = changed })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Duplicate_registration_and_unknown_recovery_do_not_disclose_accounts()
    {
        using var account = new AccountClient(fixture);
        await account.Register();
        var duplicate = await account.Post("/api/account/register", new { email = account.Email.ToUpperInvariant(), AccountClient.Password });
        Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
        Assert.Equal("", await duplicate.Content.ReadAsStringAsync());
        var unknown = $"unknown-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.Accepted, (await account.Post("/api/account/forgot-password", new { email = unknown })).StatusCode);
        Assert.False(fixture.Mail.Links.ContainsKey(unknown));
    }

    [Fact]
    public async Task Bad_confirmation_tokens_and_weak_passwords_are_rejected()
    {
        using var account = new AccountClient(fixture);
        Assert.Equal(HttpStatusCode.BadRequest, (await account.Post("/api/account/register", new { account.Email, password = "short" })).StatusCode);
        await account.Register(confirm: false);
        var query = QueryHelpers.ParseQuery(new Uri(fixture.Mail.Links[account.Email]).Query);
        Assert.Equal(HttpStatusCode.BadRequest, (await account.Post("/api/account/confirm-email", new { userId = query["userId"].ToString(), code = "invalid!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await account.Post("/api/account/login", new { account.Email, AccountClient.Password })).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        using var account = new AccountClient(fixture);
        await account.Register();
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await account.Post("/api/account/login", new { account.Email, password = "Incorrect passphrase!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await account.Post("/api/account/login", new { account.Email, AccountClient.Password })).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByEmailAsync(account.Email))!));
    }
}
