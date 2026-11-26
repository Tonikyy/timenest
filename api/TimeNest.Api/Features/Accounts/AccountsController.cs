using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace TimeNest.Api.Features.Accounts;

[ApiController]
[Route("api/account")]
[EnableRateLimiting("accounts")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountsController(
    UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signIn,
    IAntiforgery antiforgery,
    IAccountEmail email,
    IConfiguration configuration,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("csrf")]
    [DisableRateLimiting]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, SameSite = SameSiteMode.Strict, Path = "/",
            Secure = !environment.IsDevelopment() && !environment.IsEnvironment("Testing")
        });
        return NoContent();
    }

    [HttpGet("me")]
    [DisableRateLimiting]
    public async Task<ActionResult<AccountResponse?>> Me()
    {
        var user = await users.GetUserAsync(User);
        return new JsonResult(user is null ? null : new AccountResponse(user.Id, user.Email!));
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(Credentials request, CancellationToken cancellationToken)
    {
        if (!email.IsAvailable) return EmailUnavailable();
        var address = request.Email.Trim();
        if (await users.FindByEmailAsync(address) is not null) return Accepted();
        var user = new IdentityUser { UserName = address, Email = address };
        IdentityResult result;
        try { result = await users.CreateAsync(user, request.Password); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Accepted();
        }
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName")) return Accepted();
            return Problem(statusCode: 400, title: "Tarkista sähköpostiosoite ja salasana.");
        }
        await SendConfirmation(user, cancellationToken);
        return Accepted();
    }

    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> Resend(EmailRequest request, CancellationToken cancellationToken)
    {
        if (!email.IsAvailable) return EmailUnavailable();
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null && !user.EmailConfirmed) await SendConfirmation(user, cancellationToken);
        return Accepted();
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> Confirm(ConfirmEmailRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId);
        var code = Decode(request.Code);
        if (user is null || code is null || !(await users.ConfirmEmailAsync(user, code)).Succeeded)
            return Problem(statusCode: 400, title: "Vahvistuslinkki on virheellinen tai vanhentunut.");
        return NoContent();
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(Credentials request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !(await signIn.PasswordSignInAsync(user, request.Password, false, true)).Succeeded)
            return Problem(statusCode: 401, title: "Kirjautuminen ei onnistunut. Tarkista tiedot ja vahvista sähköpostisi.");
        return Ok(new AccountResponse(user.Id, user.Email!));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> Forgot(EmailRequest request, CancellationToken cancellationToken)
    {
        if (!email.IsAvailable) return EmailUnavailable();
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null && user.EmailConfirmed)
        {
            var code = Encode(await users.GeneratePasswordResetTokenAsync(user));
            await email.SendAsync(user.Email!, "Vaihda TimeNest-salasanasi",
                Link("reset", new() { ["email"] = user.Email, ["code"] = code }), cancellationToken);
        }
        return Accepted();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> Reset(ResetPasswordRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        var code = Decode(request.Code);
        if (user is null || !user.EmailConfirmed || code is null ||
            !(await users.ResetPasswordAsync(user, code, request.Password)).Succeeded)
            return Problem(statusCode: 400, title: "Salasanaa ei voitu vaihtaa. Pyydä uusi linkki ja tarkista salasana.");
        return NoContent();
    }

    private async Task SendConfirmation(IdentityUser user, CancellationToken cancellationToken)
    {
        var code = Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        await email.SendAsync(user.Email!, "Vahvista TimeNest-sähköpostisi",
            Link("confirm", new() { ["userId"] = user.Id, ["code"] = code }), cancellationToken);
    }

    private string Link(string action, Dictionary<string, string?> query)
    {
        // Never build email links from the untrusted request Host header.
        var origin = configuration["Accounts:PublicOrigin"] ?? "http://localhost:4200";
        return QueryHelpers.AddQueryString($"{origin.TrimEnd('/')}/account/{action}", query);
    }

    private ObjectResult EmailUnavailable() =>
        Problem(statusCode: 503, title: "Sähköpostipalvelu ei ole vielä käytössä.");

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private static string? Decode(string code)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch (FormatException) { return null; }
    }
}
