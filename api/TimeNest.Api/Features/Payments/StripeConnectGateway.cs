using Microsoft.Extensions.Options;
using Stripe;

namespace TimeNest.Api.Features.Payments;

public sealed record ConnectedAccount(string Id, bool DetailsSubmitted, bool ChargesEnabled,
    bool PayoutsEnabled, bool TransfersActive)
{
    public bool Ready => DetailsSubmitted && ChargesEnabled && PayoutsEnabled && TransfersActive;
}

public interface IStripeConnectGateway
{
    Task<string> CreateAccount(Guid businessId, CancellationToken cancellationToken);
    Task<ConnectedAccount> GetAccount(string accountId, CancellationToken cancellationToken);
    Task<string> CreateOnboardingLink(string accountId, CancellationToken cancellationToken);
}

public sealed class StripeConnectUnavailableException : Exception;

public sealed class StripeConnectGateway(IOptions<StripeConnectOptions> options, IHttpClientFactory httpClients)
    : IStripeConnectGateway
{
    private readonly StripeConnectOptions settings = options.Value;

    private StripeClient Client() => new(settings.SecretKey,
        httpClient: new SystemNetHttpClient(httpClients.CreateClient("stripe"), maxNetworkRetries: 2));

    public Task<string> CreateAccount(Guid businessId, CancellationToken cancellationToken) => Call(async () =>
    {
        // Keep this payload stable across retries, even if the local profile is edited.
        var account = await Client().V1.Accounts.CreateAsync(new AccountCreateOptions
        {
            Type = "express", Country = "FI", DefaultCurrency = "eur",
            Capabilities = new AccountCapabilitiesOptions
            {
                CardPayments = new AccountCapabilitiesCardPaymentsOptions { Requested = true },
                Transfers = new AccountCapabilitiesTransfersOptions { Requested = true }
            },
            Metadata = new Dictionary<string, string> { ["timenest_business_id"] = businessId.ToString("N") }
        }, new RequestOptions { IdempotencyKey = $"timenest-connect-v1-{businessId:N}" }, cancellationToken);
        return account.Id;
    }, cancellationToken);

    public Task<ConnectedAccount> GetAccount(string accountId, CancellationToken cancellationToken) => Call(async () =>
    {
        var account = await Client().V1.Accounts.GetAsync(accountId, cancellationToken: cancellationToken);
        if (account.Deleted == true || account.Id != accountId) throw new StripeConnectUnavailableException();
        return new ConnectedAccount(account.Id, account.DetailsSubmitted, account.ChargesEnabled,
            account.PayoutsEnabled, account.Capabilities?.Transfers == "active");
    }, cancellationToken);

    public Task<string> CreateOnboardingLink(string accountId, CancellationToken cancellationToken) => Call(async () =>
    {
        var origin = settings.PublicOrigin.TrimEnd('/');
        var link = await Client().V1.AccountLinks.CreateAsync(new AccountLinkCreateOptions
        {
            Account = accountId, Type = "account_onboarding",
            ReturnUrl = origin + "/business/payments?stripe=return",
            RefreshUrl = origin + "/business/payments?stripe=refresh"
        }, cancellationToken: cancellationToken);
        if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var url) ||
            url.Scheme != "https" || url.Host != "connect.stripe.com" ||
            !url.IsDefaultPort || url.UserInfo.Length != 0)
            throw new StripeConnectUnavailableException();
        return link.Url;
    }, cancellationToken);

    private static async Task<T> Call<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        try { return await operation(); }
        catch (StripeException) { throw new StripeConnectUnavailableException(); }
        catch (HttpRequestException) { throw new StripeConnectUnavailableException(); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new StripeConnectUnavailableException(); }
    }
}
