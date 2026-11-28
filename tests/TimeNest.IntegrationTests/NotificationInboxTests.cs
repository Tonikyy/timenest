using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Bookings;
using TimeNest.Api.Features.Notifications;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class NotificationInboxTests(ApiFixture fixture)
{
    private sealed record Page(NotificationView[] Items, int Total, int Unread, int PageSize);
    [Fact]
    public async Task Inbox_enforces_recipient_ownership_csrf_and_idempotent_read_without_changing_delivery()
    {
        var (customer, owner, id, _) = await CancellationTests.Create(fixture);
        using (customer) using (owner)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync("/api/notifications")).StatusCode);
            var page = (await customer.Http.GetFromJsonAsync<Page>("/api/notifications"))!;
            var notice = Assert.Single(page.Items); Assert.Equal(1, page.Unread);
            var business = Assert.Single((await owner.Http.GetFromJsonAsync<Page>("/api/notifications"))!.Items);
            Assert.NotEqual(notice.Id, business.Id);
            Assert.Equal($"/business/bookings/{id}", business.Link);
            var response = await customer.Http.GetAsync("/api/notifications");
            Assert.True(response.Headers.CacheControl!.NoStore);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "body", "createdAt", "id", "link", "readAt", "subject" },
                json.RootElement.GetProperty("items")[0].EnumerateObject().Select(x => x.Name).Order().ToArray());
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.Http.PostAsJsonAsync($"/api/notifications/{notice.Id}/read", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.Post($"/api/notifications/{notice.Id}/read", new { })).StatusCode);
            var results = await Task.WhenAll(customer.Post($"/api/notifications/{notice.Id}/read", new { }), customer.Post($"/api/notifications/{notice.Id}/read", new { }));
            Assert.All(results, x => x.EnsureSuccessStatusCode());
            var read = (await customer.Http.GetFromJsonAsync<Page>("/api/notifications"))!;
            Assert.Equal(0, read.Unread); Assert.Equal(ApiFixture.Now, read.Items[0].ReadAt);
            Assert.Equal(1, (await owner.Http.GetFromJsonAsync<Page>("/api/notifications"))!.Unread);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            Assert.Equal(NotificationEmailStatus.Pending, (await scope.ServiceProvider.GetRequiredService<TimeNestDbContext>().Notifications.FindAsync(notice.Id))!.EmailStatus);
        }
    }

    [Fact]
    public async Task Inbox_paginates_equal_timestamps_in_sequence_order_and_validates_bounds()
    {
        var (customer, owner, id, _) = await CancellationTests.Create(fixture);
        using (customer) using (owner)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
            var original = await db.Bookings.SingleAsync(x => x.Id == id);
            for (var i = 0; i < 20; i++)
            {
                var history = (Booking)db.Entry(original).CurrentValues.ToObject();
                history.Id = Guid.NewGuid(); history.IsActive = false; history.Status = BookingStatus.Cancelled;
                db.Bookings.Add(history);
                db.Notifications.Add(new Notification { BookingId = history.Id, RecipientId = original.CustomerId,
                    Kind = NotificationKind.BookingCancelled, Subject = "History", Body = "Test history", Link = "/bookings/" + history.Id,
                    CreatedAt = ApiFixture.Now, NextAttemptAt = ApiFixture.Now, EmailStatus = NotificationEmailStatus.Unavailable });
            }
            await db.SaveChangesAsync();
            var first = (await customer.Http.GetFromJsonAsync<Page>("/api/notifications?page=1"))!;
            var second = (await customer.Http.GetFromJsonAsync<Page>("/api/notifications?page=2"))!;
            Assert.Equal(21, first.Total); Assert.Equal(21, first.Unread); Assert.Equal(20, first.Items.Length);
            Assert.Single(second.Items);
            var expected = await db.Notifications.Where(x => x.RecipientId == original.CustomerId).OrderByDescending(x => x.Sequence).Select(x => x.Id).ToArrayAsync();
            Assert.Equal(expected, first.Items.Concat(second.Items).Select(x => x.Id));
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.Http.GetAsync("/api/notifications?page=0")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.Http.GetAsync("/api/notifications?page=10001")).StatusCode);
        }
    }

    [Fact]
    public async Task New_account_has_empty_inbox_and_cannot_mark_unknown_items()
    {
        using var account = new AccountClient(fixture); await account.Register(); await account.Login();
        var page = (await account.Http.GetFromJsonAsync<Page>("/api/notifications"))!;
        Assert.Empty(page.Items); Assert.Equal(0, page.Total); Assert.Equal(0, page.Unread);
        Assert.Equal(HttpStatusCode.NotFound, (await account.Post($"/api/notifications/{Guid.NewGuid()}/read", new { })).StatusCode);
    }
}
