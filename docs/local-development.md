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

Search is read-only. Account endpoints are described below; publishing, booking and payment endpoints are not included in the accounts slice. The API never returns fallback demo data if SQL Server is unavailable.

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
