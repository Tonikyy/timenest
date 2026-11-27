namespace TimeNest.Api.Features.Payments;

public sealed class StripeConnectOptions
{
    public int ApplicationFeeCents { get; set; } = 500;
    public string WebhookSecret { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string PublicOrigin { get; set; } = "http://localhost:4200";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);

    public bool IsValid(bool allowLocalHttp) =>
        ApplicationFeeCents is >= 0 and <= 10000 &&
        (WebhookSecret.Length == 0 || WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal)) &&
        (!IsConfigured || (SecretKey.StartsWith("sk_test_", StringComparison.Ordinal) &&
        Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin) &&
        string.IsNullOrEmpty(origin.UserInfo) && string.IsNullOrEmpty(origin.Query) &&
        string.IsNullOrEmpty(origin.Fragment) && origin.AbsolutePath == "/" &&
        (origin.Scheme == "https" || (allowLocalHttp && origin.Scheme == "http" && origin.IsLoopback))));
}
