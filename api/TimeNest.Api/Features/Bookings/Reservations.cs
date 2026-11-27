using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Vacancies;

namespace TimeNest.Api.Features.Bookings;

public sealed class BookingConflictException(string code) : Exception
{
    public string Code { get; } = code;
}

public sealed class Reservations(TimeNestDbContext database, TimeProvider clock)
{
    public async Task<Booking> Reserve(Guid vacancyId, string customerId, int feeCents, CancellationToken ct)
    {
        var existing = await database.Bookings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.VacancyId == vacancyId && x.IsActive, ct);
        if (existing is not null)
        {
            if (existing.CustomerId == customerId) return existing;
            throw new BookingConflictException("unavailable");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var slot = await database.Vacancies.Include(x => x.Business).SingleOrDefaultAsync(x => x.Id == vacancyId, ct);
        var now = clock.GetUtcNow();
        if (slot is null || slot.Status != VacancyStatus.Available || slot.StartsAt <= now.AddMinutes(40))
        {
            // A concurrent request may have committed after the initial booking read.
            var winner = await database.Bookings.AsNoTracking()
                .SingleOrDefaultAsync(x => x.VacancyId == vacancyId && x.IsActive && x.CustomerId == customerId, ct);
            if (winner is not null) return winner;
            throw new BookingConflictException("unavailable");
        }
        if (!slot.Business.IsDemo || slot.Business.OwnerId == customerId)
            throw new BookingConflictException("notBookable");
        if (feeCents < 0 || slot.PriceCents <= feeCents || slot.PriceCents < 50)
            throw new BookingConflictException("priceTooLow");
        var recipient = await database.StripeConnections.Where(x => x.BusinessId == slot.BusinessId)
            .Select(x => x.AccountId).SingleOrDefaultAsync(ct);
        if (recipient is null) throw new BookingConflictException("paymentsUnavailable");

        var booking = new Booking
        {
            Id = Guid.NewGuid(), VacancyId = slot.Id, BusinessId = slot.BusinessId, CustomerId = customerId,
            Title = slot.Title, BusinessName = slot.Business.Name, Address = slot.Business.Address, City = slot.Business.City,
            ServiceTypeId = slot.ServiceTypeId, StartsAt = slot.StartsAt, DurationMinutes = slot.DurationMinutes,
            PriceCents = slot.PriceCents, FeeCents = feeCents, RecipientAccountId = recipient,
            CreatedAt = now, HoldExpiresAt = now.AddMinutes(35)
        };
        slot.Status = VacancyStatus.Held;
        database.Bookings.Add(booking);
        try
        {
            // Rowversion protects against publication edits; the filtered index also protects one active booking.
            await database.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return booking;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException ||
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await transaction.RollbackAsync(ct);
            database.ChangeTracker.Clear();
            var winner = await database.Bookings.AsNoTracking()
                .SingleOrDefaultAsync(x => x.VacancyId == vacancyId && x.IsActive, ct);
            if (winner?.CustomerId == customerId) return winner;
            throw new BookingConflictException("unavailable");
        }
    }
}
