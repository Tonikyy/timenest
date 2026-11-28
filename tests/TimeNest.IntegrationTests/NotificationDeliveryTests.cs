using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Notifications;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class NotificationDeliveryTests(ApiFixture fixture)
{
    private readonly Clock clock = new();
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = ApiFixture.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Mail : INotificationEmail
    {
        public bool IsAvailable { get; set; } = true;
        public bool Fail { get; set; }
        public Func<CancellationToken, Task>? Wait { get; set; }
        public ConcurrentQueue<Guid> Sent { get; } = new();
        public async Task Send(Notification notification, CancellationToken ct)
        {
            if (Wait is not null) await Wait(ct);
            if (Fail) throw new IOException("Simulated mailbox failure");
            Sent.Enqueue(notification.Id);
        }
    }
    private async Task<Notification[]> Seed(bool cancellation = false)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        // The shared API fixture is serial; isolate this worker's eligible queue from other tests.
        await db.Notifications.Where(x => x.EmailStatus == NotificationEmailStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EmailStatus, NotificationEmailStatus.Unavailable));
        var (customer, owner, id, _) = await CancellationTests.Create(fixture);
        using (customer) using (owner)
            if (cancellation) (await customer.Post($"/api/bookings/{id}/cancel", new { })).EnsureSuccessStatusCode();
        await db.Notifications.Where(x => x.BookingId == id && x.Audience == NotificationAudience.Business)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EmailStatus, NotificationEmailStatus.Unavailable));
        return await db.Notifications.AsNoTracking().Where(x => x.BookingId == id && x.Audience == NotificationAudience.Customer).OrderBy(x => x.Sequence).ToArrayAsync();
    }
    private async Task<bool> Deliver(INotificationEmail mail)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await new NotificationDelivery(scope.ServiceProvider.GetRequiredService<TimeNestDbContext>(), mail, clock,
            NullLogger<NotificationDelivery>.Instance).DeliverNext(default);
    }
    private async Task<Notification> Read(Guid id)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TimeNestDbContext>().Notifications.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    [Fact]
    public async Task Concurrent_workers_send_once_and_preserve_booking_order()
    {
        var rows = await Seed(true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mail = new Mail { Wait = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); } };
        var first = Deliver(mail);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { Assert.False(await Deliver(mail)); }
        finally { release.SetResult(); }
        Assert.True(await first);
        Assert.True(await Deliver(mail));
        Assert.Equal(rows.Select(x => x.Id), mail.Sent);
        Assert.All(rows, x => Assert.Null(x.ReadAt));
        Assert.Equal(NotificationEmailStatus.Delivered, (await Read(rows[0].Id)).EmailStatus);
    }

    [Fact]
    public async Task Failure_backs_off_then_exhausts_without_blocking_later_messages()
    {
        var rows = await Seed(true);
        var mail = new Mail { Fail = true };
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(await Deliver(mail));
            var row = await Read(rows[0].Id);
            Assert.Equal(attempt, row.EmailAttempts);
            if (attempt < 5)
            {
                Assert.Equal(clock.Now.AddSeconds(30 * Math.Pow(2, attempt - 1)), row.NextAttemptAt);
                Assert.False(await Deliver(mail)); clock.Now = row.NextAttemptAt;
            }
        }
        Assert.Equal(NotificationEmailStatus.Failed, (await Read(rows[0].Id)).EmailStatus);
        mail.Fail = false;
        Assert.True(await Deliver(mail));
        Assert.Equal(rows[1].Id, Assert.Single(mail.Sent));
    }

    [Fact]
    public async Task Expired_lease_recovers_and_stale_worker_cannot_acknowledge_new_attempt()
    {
        var row = Assert.Single(await Seed());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = new Mail { Fail = true, Wait = async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); } };
        var first = Deliver(old); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            clock.Now = clock.Now.AddMinutes(3);
            Assert.True(await Deliver(new Mail()));
        }
        finally { release.SetResult(); }
        await first;
        var delivered = await Read(row.Id);
        Assert.Equal(NotificationEmailStatus.Delivered, delivered.EmailStatus);
        Assert.Equal(2, delivered.EmailAttempts); Assert.Null(delivered.LeaseId);
    }

    [Fact]
    public async Task Crash_on_final_attempt_becomes_failed_and_disabled_sender_does_not_consume_attempts()
    {
        var row = Assert.Single(await Seed());
        Assert.False(await Deliver(new Mail { IsAvailable = false }));
        Assert.Equal(0, (await Read(row.Id)).EmailAttempts);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        await db.Notifications.Where(x => x.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.EmailAttempts, 5)
            .SetProperty(x => x.LeaseId, Guid.NewGuid()).SetProperty(x => x.LeaseExpiresAt, clock.Now.AddMinutes(-1)));
        var mail = new Mail(); Assert.True(await Deliver(mail)); Assert.Empty(mail.Sent);
        Assert.Equal(NotificationEmailStatus.Failed, (await Read(row.Id)).EmailStatus);
    }

    [Fact]
    public async Task Local_mailbox_is_atomic_duplicate_safe_and_recovers_lost_acknowledgement()
    {
        var row = Assert.Single(await Seed());
        var root = Path.Combine(Path.GetTempPath(), "timenest-notification-" + Guid.NewGuid().ToString("N"));
        var env = new EnvironmentStub { ContentRootPath = root };
        var config = new ConfigurationBuilder().Build();
        var mail = new DevelopmentNotificationEmail(env, config);
        try
        {
            await Task.WhenAll(mail.Send(row, default), mail.Send(row, default));
            Assert.True(await Deliver(mail)); // Simulates a file written before a crash/lost SQL ack.
            var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
            Assert.Single(files); Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            Assert.Contains("http://localhost:4200/bookings/", await File.ReadAllTextAsync(files[0]));
            Assert.Equal(NotificationEmailStatus.Delivered, (await Read(row.Id)).EmailStatus);
            env.EnvironmentName = "Production";
            Assert.False(mail.IsAvailable);
            await Assert.ThrowsAsync<InvalidOperationException>(() => mail.Send(row, default));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Hosted_worker_delivers_durable_queue()
    {
        var row = Assert.Single(await Seed());
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var mail = new Mail();
        var services = new ServiceCollection().AddLogging()
            .AddSingleton(scope.ServiceProvider.GetRequiredService<DbContextOptions<TimeNestDbContext>>())
            .AddScoped<TimeNestDbContext>().AddSingleton<INotificationEmail>(mail)
            .AddSingleton<TimeProvider>(clock).AddScoped<NotificationDelivery>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new NotificationWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<NotificationWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while ((await Read(row.Id)).EmailStatus != NotificationEmailStatus.Delivered && DateTime.UtcNow < deadline) await Task.Delay(100);
            Assert.Equal(NotificationEmailStatus.Delivered, (await Read(row.Id)).EmailStatus);
            Assert.Equal(row.Id, Assert.Single(mail.Sent));
        }
        finally { await worker.StopAsync(default); }
    }

    private sealed class EnvironmentStub : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "TimeNest";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
