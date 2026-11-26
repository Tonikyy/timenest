using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class PublishingTests(ApiFixture fixture)
{
    private static object Request(string title = "Demo appointment", byte[]? version = null,
        string date = "2027-01-18", string? time = "12:00", int priceCents = 4500, string serviceTypeId = "hair") =>
        new { title, description = "Fictional service", serviceTypeId, date, time, durationMinutes = 45, priceCents, version };

    private async Task<AccountClient> Owner()
    {
        var account = new AccountClient(fixture);
        await account.Register(); await account.Login();
        (await account.Post("/api/business", BusinessTests.Profile())).EnsureSuccessStatusCode();
        return account;
    }

    private static async Task<ManagedVacancy> Publish(AccountClient owner)
    {
        var response = await owner.Post("/api/business/vacancies", Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ManagedVacancy>())!;
    }

    private static async Task<HttpResponseMessage> Edit(AccountClient owner, Guid id, object request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"/api/business/vacancies/{id}") { Content = JsonContent.Create(request) };
        message.Headers.Add("X-XSRF-TOKEN", await owner.Csrf());
        return await owner.Http.SendAsync(message);
    }

    [Fact]
    public async Task Publish_edit_discover_and_cancel_preserve_data_and_history()
    {
        using var owner = await Owner();
        var slot = await Publish(owner);
        Assert.Equal(new DateTimeOffset(2027, 1, 18, 10, 0, 0, TimeSpan.Zero), slot.StartsAt);
        var edited = await Edit(owner, slot.Id, Request("Edited appointment", slot.Version));
        edited.EnsureSuccessStatusCode();
        slot = (await edited.Content.ReadFromJsonAsync<ManagedVacancy>())!;
        var discovery = await owner.Http.GetFromJsonAsync<VacancySearchResponse>("/api/vacancies?latitude=0&longitude=0&pageSize=50");
        Assert.Contains(discovery!.Items, item => item.Id == slot.Id && item.Title == "Edited appointment" && item.IsDemo);
        var cancelled = await owner.Post($"/api/business/vacancies/{slot.Id}/cancel", new { slot.Version });
        cancelled.EnsureSuccessStatusCode();
        var result = (await cancelled.Content.ReadFromJsonAsync<ManagedVacancy>())!;
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(ApiFixture.Now, result.CancelledAt);
        (await owner.Post($"/api/business/vacancies/{slot.Id}/cancel", new { slot.Version })).EnsureSuccessStatusCode();
        discovery = await owner.Http.GetFromJsonAsync<VacancySearchResponse>("/api/vacancies?latitude=0&longitude=0&pageSize=50");
        Assert.DoesNotContain(discovery!.Items, item => item.Id == slot.Id);
        var managed = await owner.Http.GetFromJsonAsync<ManagedVacancyPage>("/api/business/vacancies");
        Assert.Contains(managed!.Items, item => item.Id == slot.Id && item.Status == "Cancelled");
    }

    [Fact]
    public async Task Other_owners_cannot_list_edit_or_cancel_another_business_slots()
    {
        using var owner = await Owner();
        using var other = await Owner();
        var slot = await Publish(owner);
        Assert.Empty((await other.Http.GetFromJsonAsync<ManagedVacancyPage>("/api/business/vacancies"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await Edit(other, slot.Id, Request("Stolen", slot.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/business/vacancies/{slot.Id}/cancel", new { slot.Version })).StatusCode);
    }

    [Fact]
    public async Task Publishing_requires_a_verified_session_profile_and_csrf()
    {
        using var account = new AccountClient(fixture);
        Assert.Equal(HttpStatusCode.Unauthorized, (await account.Http.GetAsync("/api/business/vacancies")).StatusCode);
        await account.Register(); await account.Login();
        Assert.Equal(HttpStatusCode.Conflict, (await account.Post("/api/business/vacancies", Request())).StatusCode);
        (await account.Post("/api/business", BusinessTests.Profile())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await account.Http.PostAsJsonAsync("/api/business/vacancies", Request())).StatusCode);
    }

    [Theory]
    [InlineData("2027-01-18", "12:00:30", 4500, "hair")]
    [InlineData("2027-01-18", null, 4500, "hair")]
    [InlineData("2027-01-14", "12:00", 4500, "hair")]
    [InlineData("2027-03-28", "03:30", 4500, "hair")]
    [InlineData("2027-10-31", "03:30", 4500, "hair")]
    [InlineData("2027-01-18", "12:00", 0, "hair")]
    [InlineData("2027-01-18", "12:00", 4500, "unknown")]
    public async Task Invalid_slots_and_ambiguous_or_missing_Helsinki_times_are_rejected(string date, string? time, int price, string service)
    {
        using var owner = await Owner();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Post("/api/business/vacancies", Request(date: date, time: time, priceCents: price, serviceTypeId: service))).StatusCode);
    }

    [Theory]
    [InlineData(VacancyStatus.Held)]
    [InlineData(VacancyStatus.Booked)]
    [InlineData(VacancyStatus.Cancelled)]
    public async Task Non_available_slots_cannot_be_edited(VacancyStatus status)
    {
        using var owner = await Owner();
        var slot = await Publish(owner);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        var stored = await database.Vacancies.SingleAsync(item => item.Id == slot.Id);
        stored.Status = status;
        await database.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Edit(owner, slot.Id, Request("Invalid edit", stored.Version))).StatusCode);
        if (status != VacancyStatus.Cancelled)
            Assert.Equal(HttpStatusCode.Conflict, (await owner.Post($"/api/business/vacancies/{slot.Id}/cancel", new { stored.Version })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_edits_have_one_winner_and_stale_cancellation_does_not_hide_it()
    {
        using var owner = await Owner();
        using var second = new AccountClient(fixture);
        (await second.Post("/api/account/login", new { owner.Email, AccountClient.Password })).EnsureSuccessStatusCode();
        var slot = await Publish(owner);
        var results = await Task.WhenAll(Edit(owner, slot.Id, Request("First edit", slot.Version)),
            Edit(second, slot.Id, Request("Second edit", slot.Version)));
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Post($"/api/business/vacancies/{slot.Id}/cancel", new { slot.Version })).StatusCode);
        Assert.Equal("Available", (await owner.Http.GetFromJsonAsync<ManagedVacancyPage>("/api/business/vacancies"))!.Items.Single().Status);
    }

    [Fact]
    public async Task Owner_list_is_paginated_without_duplicates()
    {
        using var owner = await Owner();
        await Publish(owner); await Publish(owner);
        var first = await owner.Http.GetFromJsonAsync<ManagedVacancyPage>("/api/business/vacancies?pageSize=1");
        var second = await owner.Http.GetFromJsonAsync<ManagedVacancyPage>("/api/business/vacancies?pageSize=1&page=2");
        Assert.Equal(2, first!.Total);
        Assert.Single(first.Items); Assert.Single(second!.Items);
        Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Http.GetAsync("/api/business/vacancies?page=0")).StatusCode);
    }

    [Fact]
    public async Task Real_businesses_cannot_publish_through_the_demo_flow()
    {
        using var owner = await Owner();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        var profile = (await owner.Http.GetFromJsonAsync<TimeNest.Api.Features.Businesses.BusinessProfileResponse>("/api/business"))!;
        var business = await database.Businesses.SingleAsync(item => item.Id == profile.Id);
        business.IsDemo = false;
        await database.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Post("/api/business/vacancies", Request())).StatusCode);
    }

    [Fact]
    public async Task Past_available_slots_cannot_be_edited_or_cancelled()
    {
        using var owner = await Owner();
        var slot = await Publish(owner);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        var stored = await database.Vacancies.SingleAsync(item => item.Id == slot.Id);
        stored.StartsAt = ApiFixture.Now.AddMinutes(-1);
        await database.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Edit(owner, slot.Id, Request(version: stored.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Post($"/api/business/vacancies/{slot.Id}/cancel", new { stored.Version })).StatusCode);
    }
}
