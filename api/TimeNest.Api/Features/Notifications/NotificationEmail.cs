using System.Text.Json;
namespace TimeNest.Api.Features.Notifications;

public interface INotificationEmail
{
    bool IsAvailable { get; }
    Task Send(Notification notification, CancellationToken ct);
}

public sealed class DevelopmentNotificationEmail(IWebHostEnvironment environment, IConfiguration configuration) : INotificationEmail
{
    public bool IsAvailable => environment.IsDevelopment();

    public async Task Send(Notification notification, CancellationToken ct)
    {
        if (!IsAvailable) throw new InvalidOperationException("Local notification email requires Development.");
        var origin = configuration["Accounts:PublicOrigin"] ?? "http://localhost:4200";
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !(uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidOperationException("Configure a trusted HTTPS origin or local HTTP origin.");
        var directory = Path.Combine(environment.ContentRootPath, ".local", "mail", "notifications");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"{notification.Id:N}.json");
        var contents = JsonSerializer.Serialize(new
        {
            id = notification.Id, recipient = notification.RecipientEmail, subject = notification.Subject,
            body = notification.Body, link = origin.TrimEnd('/') + notification.Link, createdAt = notification.CreatedAt
        });
        // Immutable final files deduplicate delivery after a lost SQL acknowledgement.
        if (File.Exists(destination))
        {
            await VerifyExisting(); return;
        }
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, contents, ct);
            try { File.Move(temporary, destination, overwrite: false); }
            catch (IOException) when (File.Exists(destination)) { await VerifyExisting(); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

        async Task VerifyExisting()
        {
            if (await File.ReadAllTextAsync(destination, ct) != contents)
                throw new IOException("Existing notification artifact does not match its immutable message.");
        }
    }
}
