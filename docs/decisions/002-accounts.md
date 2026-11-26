# 002: ASP.NET Core Identity and same-origin cookies

Status: accepted for the demo.

Use ASP.NET Core Identity in the existing SQL Server database, with email verification before sign-in, non-persistent HttpOnly cookies, and ASP.NET Core antiforgery protection on all controller mutations. Angular uses its built-in XSRF cookie/header support. Retrieve a fresh token before mutations so authentication changes and other tabs do not retain a token for the wrong identity.

Identity owns password hashing, lockout and signed confirmation/reset tokens. Passwords are 12–128 characters; passphrases are supported. Lock out after five failures for 15 minutes, expire email tokens after two hours and sessions after eight hours. Validate the security stamp on each authenticated request so password resets revoke sessions immediately; accept the database read for this demo.

No customer/business role selector is needed: any verified account may create its one business profile. Ownership is enforced server-side. This supersedes MSAL/external-identity assumptions in issues #9 and #17.

Development email is written under api/TimeNest.Api/.local/mail, outside the web root and ignored by Git. No confirmation/reset tokens are returned by the API or logged. The developer opens the link from the local JSON message. The delivery boundary is replaced by an in-memory sender in tests. Outside Development, registration and email requests return 503 until real delivery is integrated; existing cookie sign-in is HTTPS-only. Real email delivery, persistent Data Protection keys, HTTPS hosting and deployment-specific rate limiting remain Azure/pilot work.

Email links use Accounts:PublicOrigin (default http://localhost:4200 for local development), never the incoming Host header. Configure a trusted HTTPS origin with real delivery. Registration, resend and recovery return generic responses to avoid disclosing registered email addresses. The in-process per-IP account limiter is a demo baseline, not a distributed abuse-control service.

References: [Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0), [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), [Angular XSRF](https://angular.dev/guide/http/setup).
