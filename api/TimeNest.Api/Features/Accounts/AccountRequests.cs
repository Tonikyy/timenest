using System.ComponentModel.DataAnnotations;

namespace TimeNest.Api.Features.Accounts;

public sealed record Credentials(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password);

public sealed record EmailRequest([Required, EmailAddress, StringLength(254)] string Email);
public sealed record ConfirmEmailRequest(
    [Required, StringLength(450)] string UserId,
    [Required, StringLength(4096)] string Code);
public sealed record ResetPasswordRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(4096)] string Code,
    [Required, StringLength(128, MinimumLength = 12)] string Password);
public sealed record AccountResponse(string Id, string Email);
