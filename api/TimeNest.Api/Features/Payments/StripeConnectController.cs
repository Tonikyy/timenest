using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TimeNest.Api.Data;

namespace TimeNest.Api.Features.Payments;

public sealed record OnboardingStatus(string State, bool DetailsSubmitted = false,
    bool ChargesEnabled = false, bool PayoutsEnabled = false, bool TransfersActive = false);
public sealed record OnboardingLink(string Url);

[ApiController]
[Authorize]
[Route("api/business/payments")]
[EnableRateLimiting("stripe-connect")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StripeConnectController(TimeNestDbContext database, IStripeConnectGateway stripe,
    IOptions<StripeConnectOptions> options, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<OnboardingStatus>> Get(CancellationToken cancellationToken)
    {
        var business = await database.Businesses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == User.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);
        if (business is null) return Ok(new OnboardingStatus("noBusiness"));
        if (!business.IsDemo) return Conflict(new { code = "demoOnly" });
        if (!options.Value.IsConfigured) return Ok(new OnboardingStatus("unavailable"));
        var connection = await database.StripeConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == business.Id, cancellationToken);
        if (connection is null) return Ok(new OnboardingStatus("notStarted"));
        if (connection.AccountId is null)
            return Ok(new OnboardingStatus(RequiresRecovery(connection) ? "recoveryRequired" : "pendingCreation"));
        try
        {
            var account = await stripe.GetAccount(connection.AccountId, cancellationToken);
            return Ok(new OnboardingStatus(account.Ready ? "ready" : "incomplete",
                account.DetailsSubmitted, account.ChargesEnabled, account.PayoutsEnabled, account.TransfersActive));
        }
        catch (StripeConnectUnavailableException) { return Unavailable(); }
    }

    [HttpPost("onboarding-link")]
    public async Task<ActionResult<OnboardingLink>> Start(CancellationToken cancellationToken)
    {
        var business = await database.Businesses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == User.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);
        if (business is null) return Conflict(new { code = "noBusiness" });
        if (!business.IsDemo) return Conflict(new { code = "demoOnly" });
        if (!options.Value.IsConfigured) return Unavailable();

        var connection = await database.StripeConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == business.Id, cancellationToken);
        if (connection is null)
        {
            var email = await database.Users.Where(x => x.Id == business.OwnerId)
                .Select(x => x.Email).SingleAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(email)) return Unavailable();
            connection = new StripeConnection
            { BusinessId = business.Id, CreationStartedAt = clock.GetUtcNow(), ContactEmail = email };
            database.StripeConnections.Add(connection);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                database.Entry(connection).State = EntityState.Detached;
                connection = await database.StripeConnections.AsNoTracking()
                    .SingleAsync(x => x.BusinessId == business.Id, cancellationToken);
            }
        }

        try
        {
            if (connection.AccountId is null)
            {
                // Never replay an uncertain v1 attempt through v2 or exceed the v2 replay window.
                if (RequiresRecovery(connection)) return Conflict(new { code = "recoveryRequired" });
                var accountId = await stripe.CreateAccount(business.Id, connection.ContactEmail!, cancellationToken);
                await database.StripeConnections.Where(x => x.BusinessId == business.Id && x.AccountId == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.AccountId, accountId), cancellationToken);
                connection = await database.StripeConnections.AsNoTracking()
                    .SingleAsync(x => x.BusinessId == business.Id, cancellationToken);
            }
            return Ok(new OnboardingLink(await stripe.CreateOnboardingLink(connection.AccountId!, cancellationToken)));
        }
        catch (StripeConnectUnavailableException) { return Unavailable(); }
    }

    private bool RequiresRecovery(StripeConnection connection) =>
        connection.CreationApiVersion != 2 || string.IsNullOrWhiteSpace(connection.ContactEmail) ||
        clock.GetUtcNow() - connection.CreationStartedAt >= TimeSpan.FromDays(29);
    private ObjectResult Unavailable() => Problem(statusCode: 503, title: "Maksujen käyttöönotto ei ole nyt käytettävissä.");
}
