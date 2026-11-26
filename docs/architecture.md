# Architecture

One repository, one ASP.NET Core application, one Angular application, one SQL database.

```text
api/TimeNest.Api/
  Features/          HTTP endpoints, request/response types and feature logic
  Data/              EF Core context, mappings, migrations
  Integrations/      Concrete external provider integrations when implemented
  BackgroundJobs/    Persistent payment/retry work when implemented
  Notifications/     Delivery integration when implemented
web/src/app/
  core/              Shared application services
  shared/            Reusable UI
  features/          Customer, business and checkout experiences
tests/               Backend unit/integration tests and later browser flows
infra/               Azure Bicep added in the deployment milestone
docs/decisions/      Short decisions with consequences
```

Only create folders that contain implemented functionality. The tree describes intended responsibility, not a requirement for empty scaffolding.

## Boundaries

Controllers handle HTTP and validation. EF Core owns persistence. Extract business operations into feature services when they become nontrivial. Do not introduce repositories wrapping every DbSet, mediator pipelines, or separate deployable services without a concrete need.

Angular talks to relative /api URLs. A development proxy reaches the API locally; the planned Azure host serves the compiled frontend and API on the same origin. Browser cookies and antiforgery are the proposed identity model.

SQL Server is the source of truth for bookings and financial state. Stripe is the payment processor. Webhook processing and reconciliation must tolerate retries, out-of-order events, restarts, and partial failure between SQL and Stripe.

Demo data is an explicit opt-in development operation. Never seed automatically in production. Azure deployment, identity, and payment integrations are later milestones, not features supplied by the initial foundation.
