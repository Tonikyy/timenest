# TimeNest

TimeNest helps appointment-based businesses fill cancellations and helps customers find and pay for nearby openings.

This repository restarts the [original web prototype](https://github.com/Tonikyy/tnest-website) with **ASP.NET Core, Angular, and Azure**, preserving its original logo, purple palette, and typography.

## Current implementation

The first runnable slice includes:

- ASP.NET Core API with EF Core migrations and SQL Server.
- Database-side nearby search with service/date/radius filters and pagination.
- Responsive Angular discovery page and appointment details in Finnish.
- Explicitly fictional, opt-in demo data and owner-managed demo openings.
- Health checks, SQL Server integration tests, frontend tests, and GitHub Actions.
- Accounts with email verification, cookie login/logout and password recovery (local development email).

Verified accounts can manage their demo business profile and publish, edit and cancel fictional openings. Stripe sandbox onboarding, bookings, payments, refunds and Azure deployment remain next. Bookings and payments are not enabled.

## Run locally

Prerequisites: .NET 10 SDK, Node.js 24.15+ (24.x) or a compatible Angular 22 Node version, npm, PowerShell 7, and Docker Desktop with Linux containers.

From the repository root:

```powershell
./scripts/setup-local.ps1
dotnet run --project api/TimeNest.Api
```

In another terminal:

```powershell
npm --prefix web ci
npm --prefix web start
```

Open [TimeNest locally](http://localhost:4200). The API runs at http://localhost:5080. [Full setup and API reference](docs/local-development.md).

The setup helper creates a random local database password and stores the API connection in .NET user-secrets. No production credentials are needed.

## Verify

```powershell
dotnet build --configuration Release
dotnet test --configuration Release
npm --prefix web run build
npm --prefix web test
npm --prefix web run format:check
```

Integration tests start their own SQL Server container and do not modify the local demo database.

## Product and architecture

Build a working demo with sandbox payments first, then a real pilot. Customer payments and the platform fee are part of the MVP.

One backend application with feature folders, one Angular application, and one SQL database. Proposed deployment: serve the Angular build and API from the same Azure App Service, backed by Azure SQL. Identity uses ASP.NET Core Identity and same-origin cookies. Stripe integration remains a separate payment step.

- [MVP scope and milestones](docs/mvp.md)
- [Architecture](docs/architecture.md)
- [Decisions](docs/decisions/README.md)
- [Original brand reference](docs/brand.md)
- [Frontend development](web/README.md)

Separate concerns are delivered in separate PRs; dependent PRs are stacked. Nothing provisions Azure resources or enables live payments automatically.
