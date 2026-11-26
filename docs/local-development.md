# Local development

## Prerequisites

- .NET 10 SDK (global.json permits newer .NET 10 feature bands).
- PowerShell 7 for the setup helper.
- Docker Desktop running Linux containers, with enough memory for SQL Server (at least 2 GB for SQL Server itself).
- Node.js and npm for the Angular frontend when its PR is included.

## Start SQL Server and seed fictional data

From the repository root:

```powershell
./scripts/setup-local.ps1
dotnet run --project api/TimeNest.Api
```

The helper generates a random local SQL password in ignored .env, stores the connection in .NET user-secrets, starts SQL Server on localhost:14333, applies migrations, and seeds an empty database.

API: http://localhost:5080. Development OpenAPI: http://localhost:5080/openapi/v1.json.

No credentials are committed. The local developer login is not a deployment credential. Keep .env paired with the Docker database volume; changing the password in .env does not change an existing SQL Server login.

Demo appointments start tomorrow in Europe/Helsinki and span four days. Re-running the seed does not move existing appointments. If the sample dates have passed, search will correctly return no future openings. Recreate only a disposable local demo database if fresh sample dates are needed.

## API endpoints

- GET /api/service-types — categories from SQL Server.
- GET /api/vacancies — available future slots, ordered by distance, start time, and ID.
- GET /health/live — process health.
- GET /health/ready — database connection and migrated catalog table.

Search parameters: latitude, longitude, radiusKm (1–100), serviceTypeId, from (inclusive), to (exclusive), page (1–10000), pageSize (1–50). Defaults: Helsinki center, 10 km, first page, 12 results. Send ISO timestamps with an explicit offset. Date input displayed in Finland must be converted using Europe/Helsinki rather than the visitor's machine timezone.

Search is read-only. Account endpoints are described below; business publishing is described below; booking and payment endpoints are not implemented. The API never returns fallback demo data if SQL Server is unavailable.

## Build and test

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

Integration tests start their own disposable SQL Server using Testcontainers and apply the real migrations. Docker and the .NET SDK are required; tests do not use the development database. The first run downloads the SQL Server image.

## Migrations

```powershell
dotnet tool restore
dotnet ef migrations add DescriptiveName --project api/TimeNest.Api --output-dir Data/Migrations
dotnet run --project api/TimeNest.Api -- --migrate
```

The migrate/seed switches are restricted to Development. Production migrations will be a controlled deployment step in the Azure milestone.

## Stop local SQL Server

```powershell
docker compose stop
```

This preserves the named data volume. Do not delete it to troubleshoot an unrelated application problem.

## Accounts

Run the setup script again after pulling the accounts PR to apply the Identity migration. Open /account/register, use a fictional email and a 12–128 character password, then open the confirmation link in the newest JSON message under api/TimeNest.Api/.local/mail. This mailbox is local, ignored by Git and never served over HTTP. Treat its links as credentials and do not share or commit it. Forgotten-password and resend-confirmation flows use the same mailbox.

The API provides GET /api/account/me and /api/account/csrf, plus POST register, login, logout, confirm-email, resend-confirmation, forgot-password and reset-password under /api/account. Mutations require the antiforgery cookie and X-XSRF-TOKEN header, including anonymous registration/login. Angular obtains a fresh token before each mutation and uses HttpClient's normal XSRF handling. Tokens are refreshed against the current identity after login/logout.

Local email links default to http://localhost:4200; Accounts:PublicOrigin may override this trusted origin. Outside Development the local sender is disabled and email-dependent account endpoints return 503 until a real delivery implementation is configured. No email is sent to external recipients by this demo setup. See [the account decision](decisions/002-accounts.md) for deployment requirements.

## Business profiles

A verified account can create one fictional business at /business. Name, address, city and explicit map coordinates are required; coordinates drive nearby search. GET /api/business returns only the caller's profile (or JSON null). POST creates a profile and PUT edits it, with antiforgery protection. The server assigns ownership and demo status; callers cannot set either.

A filtered unique SQL index enforces one business per owner even for concurrent requests. Existing seeded businesses retain null owners and cannot be claimed through this API. PUT requires the base64 version returned by GET; stale edits return 409. Reload the profile explicitly before retrying so another edit is not silently overwritten. Business deletion and owner transfer are outside this MVP slice.
## Demo vacancy publishing

Open /business/vacancies after creating a profile. Publish, edit and cancel fictional openings; the customer search reads the same SQL records. Only future Available slots can be edited or cancelled. Held/booked slots are rejected rather than changing a reservation's details. Cancellation retains the row, status and UTC cancellation timestamp; repeated cancellation is harmless.

GET /api/business/vacancies lists only the caller's slots (page 1–10000, pageSize 1–50; default 12), with future dates first. POST publishes, PUT /{id} edits and POST /{id}/cancel cancels. Mutations require authentication and CSRF. Edits/cancellations carry the base64 rowversion returned by the list; conflicts return 409, and another owner's IDs return 404.

Publication fields: title, description, serviceTypeId, date (YYYY-MM-DD), time (HH:mm:ss, whole minutes), durationMinutes (5–480), priceCents (positive integer EUR cents). The server converts Europe/Helsinki calendar input to UTC and rejects past, nonexistent spring-transition, and ambiguous autumn-transition times. Choose an unambiguous time rather than silently choosing a DST offset.

Publishing is limited to demo businesses. All profiles created by this batch are marked demo by the server. Stripe sandbox onboarding is the next payment step; no booking, checkout, charge, refund or real-business publishing is enabled here.
