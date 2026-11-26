using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using TimeNest.Api.Features.Accounts;

namespace TimeNest.IntegrationTests;

public sealed class TestAccountEmail : IAccountEmail
{
    public bool IsAvailable => true;
    public ConcurrentDictionary<string, string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Task SendAsync(string recipient, string subject, string link, CancellationToken cancellationToken)
    {
        Links[recipient] = link;
        return Task.CompletedTask;
    }
}

public sealed class AccountClient(ApiFixture fixture) : IDisposable
{
    public HttpClient Http { get; } = fixture.Factory.CreateClient();
    public string Email { get; } = $"{Guid.NewGuid():N}@example.test";
    public const string Password = "A long demo passphrase!";
    public void Dispose() => Http.Dispose();

    public async Task<string> Csrf()
    {
        var response = await Http.GetAsync("/api/account/csrf");
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("XSRF-TOKEN="));
        return Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
    }

    public async Task<HttpResponseMessage> Post(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", await Csrf());
        return await Http.SendAsync(request);
    }

    public async Task Register(bool confirm = true)
    {
        (await Post("/api/account/register", new { Email, Password })).EnsureSuccessStatusCode();
        if (confirm)
        {
            var query = QueryHelpers.ParseQuery(new Uri(fixture.Mail.Links[Email]).Query);
            (await Post("/api/account/confirm-email", new { userId = query["userId"].ToString(), code = query["code"].ToString() })).EnsureSuccessStatusCode();
        }
    }

    public async Task Login() =>
        (await Post("/api/account/login", new { Email, Password })).EnsureSuccessStatusCode();
}

[CollectionDefinition("API")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
