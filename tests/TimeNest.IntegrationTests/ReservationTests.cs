using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Bookings;
using TimeNest.Api.Features.Businesses;
using TimeNest.Api.Features.Payments;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.IntegrationTests;

[Collection("API")]
public sealed class ReservationTests(ApiFixture fixture)
{
    internal static async Task<(Guid SlotId, string CustomerId)> Seed(ApiFixture fixture, int price = 4500, int minutes = 120)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        var user = new IdentityUser { Id = Guid.NewGuid().ToString(), UserName = Guid.NewGuid().ToString() };
        var business = new Business { Id = Guid.NewGuid(), Name = "Demo salon", Address = "Test 1", City = "Helsinki",
            Location = new Point(0, 0) { SRID = 4326 }, IsDemo = true };
        var slot = new Vacancy { Id = Guid.NewGuid(), Business = business, ServiceTypeId = "hair",
            Title = "Demo haircut", Description = "Fictional", StartsAt = ApiFixture.Now.AddMinutes(minutes),
            PriceCents = price, DurationMinutes = 45 };
        db.Users.Add(user); db.Vacancies.Add(slot);
        var accountId = "acct_" + Guid.NewGuid().ToString("N");
        db.StripeConnections.Add(new StripeConnection { BusinessId = business.Id, AccountId = accountId });
        fixture.Stripe.Accounts[accountId] = new ConnectedAccount(accountId, true, true, true, true);
        await db.SaveChangesAsync();
        return (slot.Id, user.Id);
    }

    private async Task<Booking> Reserve(Guid slotId, string customerId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await new Reservations(scope.ServiceProvider.GetRequiredService<TimeNestDbContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>()).Reserve(slotId, customerId, 500, default);
    }

    [Fact]
    public async Task Parallel_customers_cannot_hold_the_same_slot()
    {
        var first = await Seed(fixture);
        var second = await Seed(fixture);
        async Task<Booking?> Try(string customer)
        {
            try { return await Reserve(first.SlotId, customer); }
            catch (BookingConflictException) { return null; }
        }
        var results = await Task.WhenAll(Try(first.CustomerId), Try(second.CustomerId));
        Assert.Single(results, x => x is not null);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        Assert.Equal(1, await db.Bookings.CountAsync(x => x.VacancyId == first.SlotId && x.IsActive));
        Assert.Equal(VacancyStatus.Held, (await db.Vacancies.FindAsync(first.SlotId))!.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Booking_committed_before_vacancy_read_is_reused_only_by_its_customer(bool sameCustomer)
    {
        var seed = await Seed(fixture);
        var competingCustomer = sameCustomer ? seed.CustomerId : (await Seed(fixture)).CustomerId;
        Booking? winner = null;
        var interleaving = new CommitBeforeVacancyRead(async () => winner = await Reserve(seed.SlotId, competingCustomer));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var options = new DbContextOptionsBuilder<TimeNestDbContext>(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<TimeNestDbContext>>())
            .AddInterceptors(interleaving).Options;
        await using var db = new TimeNestDbContext(options);
        var reservations = new Reservations(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());

        if (sameCustomer)
        {
            var result = await reservations.Reserve(seed.SlotId, seed.CustomerId, 500, default);
            Assert.Equal(winner!.Id, result.Id);
        }
        else
            await Assert.ThrowsAsync<BookingConflictException>(() => reservations.Reserve(seed.SlotId, seed.CustomerId, 500, default));

        Assert.True(interleaving.Committed);
        Assert.Equal(1, await db.Bookings.CountAsync(x => x.VacancyId == seed.SlotId && x.IsActive));
        Assert.Equal(VacancyStatus.Held, (await db.Vacancies.AsNoTracking().SingleAsync(x => x.Id == seed.SlotId)).Status);
    }

    private sealed class CommitBeforeVacancyRead(Func<Task> commit) : DbCommandInterceptor
    {
        public bool Committed { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!Committed && command.CommandText.Contains("FROM [Vacancies]"))
            {
                Committed = true;
                await commit();
            }
            return result;
        }
    }

    [Fact]
    public async Task Retry_reuses_the_booking_and_snapshots_survive_business_edits()
    {
        var seed = await Seed(fixture);
        var booking = await Reserve(seed.SlotId, seed.CustomerId);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        await db.Businesses.Where(x => x.Id == booking.BusinessId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Changed").SetProperty(x => x.Address, "Changed"));
        var retry = await Reserve(seed.SlotId, seed.CustomerId);
        Assert.Equal(booking.Id, retry.Id);
        Assert.Equal("Demo salon", retry.BusinessName);
        Assert.Equal("Test 1", retry.Address);
        Assert.Equal(4500, retry.PriceCents);
        Assert.Equal(500, retry.FeeCents);
        Assert.Equal("eur", retry.Currency);
        Assert.Equal(ApiFixture.Now.AddMinutes(35), retry.HoldExpiresAt);
    }

    [Theory]
    [InlineData(500, 120)]
    [InlineData(4500, 40)]
    [InlineData(4500, -10)]
    public async Task Invalid_price_or_time_leaves_no_hold(int price, int minutes)
    {
        var seed = await Seed(fixture, price, minutes);
        await Assert.ThrowsAsync<BookingConflictException>(() => Reserve(seed.SlotId, seed.CustomerId));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        Assert.False(await db.Bookings.AnyAsync(x => x.VacancyId == seed.SlotId));
        Assert.Equal(VacancyStatus.Available, (await db.Vacancies.FindAsync(seed.SlotId))!.Status);
    }

    [Fact]
    public async Task Held_vacancy_rejects_an_edit_using_its_old_rowversion()
    {
        var seed = await Seed(fixture);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeNestDbContext>();
        var stale = (await db.Vacancies.FindAsync(seed.SlotId))!;
        await Reserve(seed.SlotId, seed.CustomerId);
        stale.PriceCents = 100;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }
}
