using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TimeNest.Api.Data;
using TimeNest.Api.Features.Bookings;

namespace TimeNest.Api.Features.Notifications;

public static class BookingNotifications
{
    private static readonly CultureInfo Finnish = CultureInfo.GetCultureInfo("fi-FI");
    private static readonly TimeZoneInfo Helsinki = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");

    public static async Task Record(TimeNestDbContext database, Booking booking, NotificationKind kind,
        DateTimeOffset now, CancellationToken ct, int refundAmount = 0, CancellationActor? actor = null)
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Notifications must share the booking or refund transaction.");
        var ownerId = await database.Businesses.Where(x => x.Id == booking.BusinessId).Select(x => x.OwnerId).SingleAsync(ct);
        var users = await database.Users.AsNoTracking().Where(x => x.Id == booking.CustomerId || x.Id == ownerId).ToListAsync(ct);
        foreach (var user in users)
        {
            // Callers serialize transitions; the unique index is the final duplicate guard.
            if (await database.Notifications.AnyAsync(x => x.BookingId == booking.Id && x.Kind == kind && x.RecipientId == user.Id, ct))
                continue;
            var audience = user.Id == booking.CustomerId ? NotificationAudience.Customer : NotificationAudience.Business;
            var (subject, text) = Message(booking, kind, audience, refundAmount, actor);
            var email = user.EmailConfirmed ? user.Email : null;
            database.Notifications.Add(new Notification
            {
                BookingId = booking.Id, RecipientId = user.Id, RecipientEmail = email, Kind = kind, Audience = audience,
                Subject = subject, Body = $"Esittelytila – vain kuvitteellisia palveluja ja testimaksuja.\n\n{text}\n\n" +
                    $"{booking.Title}\n{booking.BusinessName}\n{Day(booking.StartsAt)} (Suomen aikaa) · {booking.DurationMinutes} min\n" +
                    $"{booking.Address}, {booking.City}\nHinta: {Money(booking.PriceCents)} (sisältää palvelumaksun {Money(booking.FeeCents)}).\n\n" +
                    $"Tämä ilmoitus kuvaa tilannetta {Day(now)}. Tarkista ajantasainen tila varauksen tiedoista.",
                Link = (audience == NotificationAudience.Business ? "/business/bookings/" : "/bookings/") + booking.Id,
                CreatedAt = now, NextAttemptAt = now,
                EmailStatus = string.IsNullOrWhiteSpace(email) ? NotificationEmailStatus.Unavailable : NotificationEmailStatus.Pending
            });
        }
    }

    private static (string Subject, string Text) Message(Booking booking, NotificationKind kind,
        NotificationAudience audience, int refundAmount, CancellationActor? actor) => kind switch
    {
        NotificationKind.BookingConfirmed => (audience == NotificationAudience.Customer
            ? "Varauksesi on vahvistettu – TimeNest" : "Uusi maksettu varaus – TimeNest",
            "Maksu on vahvistettu ja aika on varattu. " +
            (booking.CancellationPolicy == CancellationPolicy.LastMinuteNoRefund
                ? "Asiakkaan peruutuksesta ei tehdä automaattista maksun palautusta."
                : "Tähän varaukseen sovelletaan aiempaa ehtoa: asiakkaan peruutus ennen ajan alkua palautetaan kokonaan.") +
            " Jos yritys peruu ajan, koko maksu palvelumaksuineen palautetaan."),
        NotificationKind.BookingCancelled => ("Varaus on peruttu – TimeNest",
            (actor == CancellationActor.Business ? "Yritys perui ajan." : "Asiakas perui varauksen.") +
            (refundAmount > 0 ? $" Maksun palautus {Money(refundAmount)} palvelumaksuineen on jonossa. Palautusta ei ole vielä vahvistettu."
                : " Maksettua hintaa ei palauteta automaattisesti tämän varauksen peruutusehdon mukaan.")),
        NotificationKind.RefundSucceeded => ("Maksun palautus vahvistettu – TimeNest",
            $"Maksun palautus {Money(refundAmount)} on vahvistettu. Näkyminen tilillä voi kestää useita pankkipäiviä. Varaus on peruttu."),
        NotificationKind.RefundFailed => ("Maksun palautus epäonnistui – TimeNest",
            $"Maksun palautus {Money(refundAmount)} epäonnistui. Ota yhteyttä TimeNestin ylläpitoon. Varaus on silti peruttu."),
        _ => ("Maksun palautus vaatii selvityksen – TimeNest",
            $"Maksun palautuksen {Money(refundAmount)} tilanne vaatii selvityksen. Ota yhteyttä TimeNestin ylläpitoon. Varaus on silti peruttu.")
    };
    private static string Money(int cents) => (cents / 100m).ToString("C", Finnish);
    private static string Day(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Helsinki).ToString("d.M.yyyy 'klo' HH.mm", Finnish);
}
