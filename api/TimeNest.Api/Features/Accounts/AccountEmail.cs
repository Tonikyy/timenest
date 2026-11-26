using System.Text.Json;

namespace TimeNest.Api.Features.Accounts;

public interface IAccountEmail
{
    bool IsAvailable { get; }
    Task SendAsync(string recipient, string subject, string link, CancellationToken cancellationToken);
}

// Local development mailbox; replace this boundary with real delivery for the Azure demo.
public sealed class DevelopmentAccountEmail(IWebHostEnvironment environment) : IAccountEmail
{
    public bool IsAvailable => environment.IsDevelopment();

    public async Task SendAsync(string recipient, string subject, string link, CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new InvalidOperationException("Account email delivery is not configured.");
        var directory = Path.Combine(environment.ContentRootPath, ".local", "mail");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, $"{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(new { recipient, subject, link }), cancellationToken);
    }
}
