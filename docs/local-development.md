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

Publishing is limited to demo businesses. All profiles created by this batch are marked demo by the server. Stripe sandbox onboarding is described below; no booking, checkout, charge, refund or real-business publishing is enabled here.

## Stripe sandbox onboarding

The backend provides GET /api/business/payments and POST /api/business/payments/onboarding-link. Both require a signed-in owner; POST also requires the standard antiforgery token. Open /business/payments from either business screen. The Finnish page supports start/resume, incomplete/ready status, signed-out recovery and explicit retries. Existing demo publication remains available independently of onboarding.

Configure a Connect-enabled Stripe sandbox in the Stripe Dashboard. Use only fictional test business information. Store its secret locally with .NET user-secrets under `Stripe:SecretKey` (sk_test_ prefix); do not put it in appsettings, source, screenshots, chat or command output. To avoid storing the key in PowerShell command history:

```powershell
$stripeSecret = Read-Host 'Stripe sandbox secret key' -MaskInput
@{ 'Stripe:SecretKey' = $stripeSecret } | ConvertTo-Json -Compress |
  dotnet user-secrets set --project api/TimeNest.Api
Remove-Variable stripeSecret
dotnet user-secrets set 'Stripe:PublicOrigin' 'http://localhost:4200' --project api/TimeNest.Api
dotnet run --project api/TimeNest.Api -- --migrate
dotnet run --project api/TimeNest.Api
```

The configured origin must be HTTPS, with no path, credentials, query or fragment. Local loopback HTTP is accepted only in Development/Testing. Keep the same sandbox paired with its database; use a separate database when switching Stripe sandboxes. Do not use live keys. No webhook secret or publishable key is needed for this hosted onboarding slice.

GET returns noBusiness, unavailable (no key configured), notStarted, pendingCreation, recoveryRequired, incomplete or ready. Ready requires submitted details, charges enabled, payouts enabled and active transfers from the current Stripe account. Provider failures return 503, never a fabricated ready status. Onboarding status does not enable bookings or payments.

A link is generated just in time for the authenticated owner. It returns to /business/payments?stripe=return; expired/used links return to ?stripe=refresh. Neither query changes account status. The UI offers an explicit continue button to request a fresh link. Never share or store these URLs.

### Recover an uncertain account creation

For v2 attempts younger than 29 days, retry the onboarding POST: it uses the original idempotency key and payload. If Stripe created the account but the response or SQL association failed, the retry recovers that same account. Link failures leave the existing association intact.

After 29 days, automated v2 creation stops. Unresolved attempts created by the older v1 implementation are held for reconciliation immediately; they are never automatically reissued through v2. Pause onboarding requests for the affected business. In the original Stripe sandbox, find the creation request with idempotency key `timenest-connect-v2-{BusinessId in N format}` (`timenest-connect-v1-...` for legacy attempts) and the account with metadata `timenest_business_id` equal to that same 32-character ID. Verify the sandbox, FI/Express account and metadata before linking it. With a backup and a database transaction, update only the matching pending StripeConnections row where AccountId IS NULL to the verified account ID; require exactly one affected row. The unique account-ID index prevents associating one account with two businesses. Resume and confirm GET retrieves the account successfully.

If creation provably never reached Stripe, an operator can reset CreationStartedAt after checking the sandbox request logs and accounts. For a legacy v1 attempt proven to have been rejected before creating an account, also set CreationApiVersion to 2 and snapshot the verified owner email into ContactEmail. Do not do this if a legacy account might exist; associate that verified account instead. If its outcome remains uncertain, leave it pending and investigate; do not delete the row, rotate its key, change sandboxes or create another account. There is deliberately no public endpoint for attaching arbitrary account IDs.

### Validation

The test suite uses a simulated HTTP transport behind the actual Stripe SDK plus a disposable SQL Server. No Stripe credentials are required and no external accounts are created by tests. A real sandbox run on 2026-11-27 verified v2 account creation, hosted onboarding with fictional identity data and Stripe's test bank account, return to TimeNest, and server-verified ready status. After configuring a different sandbox, repeat that check. Also check an early return and fresh-link resume, and verify revoked capabilities no longer show ready; those paths are covered by simulated tests but were not exercised against Stripe in this run. See [ADR 003](decisions/003-stripe-sandbox-onboarding.md).

## Customer Checkout

Apply the migrations, run the API and Angular, and use a verified customer account different from the business owner. Only fictional businesses with ready Stripe sandbox accounts can be booked. The API rechecks the recipient's current capabilities before reservation and before the first Checkout attempt.

- POST /api/bookings with vacancyId reserves an available slot; retries by its customer reuse the active booking.
- GET /api/bookings?page=1 and GET /api/bookings/{id} expose only the current customer's bookings.
- POST /api/bookings/{id}/checkout starts/resumes the same Stripe session.
- POST /api/bookings/{id}/refresh reconciles provider state, then returns the booking.
- POST /api/payments/stripe/webhook accepts Stripe-signed platform Checkout events. It is the only payment mutation exempt from cookie CSRF.

Customer mutations require the normal antiforgery token. Prices, fee, recipient and redirects never come from customer input. Stripe:ApplicationFeeCents defaults to 500; the advertised price must exceed the fee. Checkout is card-only with adaptive pricing disabled so the EUR snapshot remains authoritative. Start Checkout within four minutes of reservation; sessions expire 35 minutes after reservation. Open slots must start more than 40 minutes away.

For local webhook forwarding, install the official Stripe CLI, select the same sandbox, and run:

```powershell
stripe listen --forward-to http://localhost:5080/api/payments/stripe/webhook --events checkout.session.completed,checkout.session.expired,checkout.session.async_payment_succeeded,checkout.session.async_payment_failed
```

Store the listener's signing secret locally as Stripe:WebhookSecret using a masked prompt and the JSON user-secrets technique above. Do not paste it into chat or commit it. Restart the API after configuration. The webhook validates the original request bytes, signature and timestamp, rejects live/connected-account events, and retrieves the current session and expanded PaymentIntent. Browser query parameters and event names cannot confirm a booking.

A background worker scans durable pending rows every 30 seconds outside Testing. It shares the webhook/refresh transition logic and recovers payments even when the browser never returns or webhook delivery fails. Payment status and vacancy transition commit together. Duplicate/concurrent/out-of-order requests cannot confirm twice or revert a terminal booking. Recovery reads must verify the session identity, sandbox, amount, currency, fee and recipient.

### Uncertain Checkout creation

A missing session ID after a recorded attempt is an uncertain provider outcome, not proof that no payment is possible. Retries reuse the stored request payload, origin, absolute expiry and idempotency key timenest-checkout-v1-{bookingId in N format}. After 23 hours without an association, automatic creation stops and the payment requires recovery. A failed/expired creation request may also require reconciliation if its outcome cannot be recovered within the provider's replay window. Keep the reservation held.

Pause work for that booking. In the same sandbox, inspect its idempotency key and timenest_booking_id metadata, verify the session and all snapshotted payment details, and associate only that verified session with the existing payment row in a transaction. Require exactly one updated row and preserve the unique session/payment-intent indexes. Set status back to Creating so reconciliation retrieves the association. Never delete payment history or create a replacement session for an uncertain attempt. A delayed signed webhook can also recover an unassociated session using its verified provider metadata and snapshot checks.

A reservation with no recorded provider attempt can be released after expiry. Once an attempt exists, only verified expired/unpaid sessions release slots; open, processing, unreachable or mismatched sessions retain the hold. Pressing Back or returning from Checkout does not cancel a payment or free a slot. Confirmed-booking cancellation and refunds are not implemented in this slice.

### Customer payment verification (2026-11-27)

A real local browser run used a verified fictional customer to discover and reserve a payable demo opening, continue to Stripe-hosted sandbox Checkout, pay EUR 45 with Stripe's test Visa, and return to a server-verified Confirmed booking. The server checked the EUR 5 application fee and connected-account recipient. No live payment was made. Webhook signature/duplicate/order/failure behavior was verified in SQL Server integration tests; real Stripe webhook forwarding was not configured for this run. The customer return refresh performed the real reconciliation. Cancellation/refunds, real-provider declines/3DS and a real timed Checkout expiry remain unverified here.
## Cancellation and refund recovery

New reservations snapshot the last-minute policy: customer cancellation before the start does not automatically refund payment. Business cancellation refunds the complete amount, including the fee. The cancellation migration assigns the original policy to existing bookings. Customer and business mutations require ownership and CSRF.

POST /api/bookings/{id}/cancel cancels a confirmed booking. POST /api/bookings/{id}/abandon requests cancellation of an unpaid hold; it does not cancel a payment that won the race. GET /api/business/bookings and /{id} show owned bookings; POST /{id}/cancel closes the appointment and queues its refund.

Refunds are processed every 30 seconds when the sandbox key is configured. Forward refund.created, refund.updated and refund.failed as well as the existing checkout.session events to /api/payments/stripe/webhook, using the signing secret for that endpoint. The webhook retrieves current provider state and checks booking metadata, PaymentIntent, amount, currency and destination-transfer reversal before accepting success. The polling worker provides recovery when a callback is missed, including checks for 35 days after success is first observed because bank failures can arrive later. A long queue or pending period does not shorten that window, and duplicate observations do not extend it. The monitoring migration gives existing successful refunds a full window from migration time.

Queued means the obligation is recorded; Pending means Stripe has not completed it. Succeeded means Stripe reports successful processing, not a promise that the customer bank has already posted the credit. Failed and RecoveryRequired need operator attention. Cancellation remains recorded regardless of refund progress. No provider error body, payment URL or secret is logged.

The create request uses one persisted attempt and stable key. An unknown attempt older than 23 hours is never blindly recreated. Inspect the sandbox payment and its refunds in Stripe: compare the exact PaymentIntent, booking metadata, EUR amount and transfer/fee reversal. If the existing refund is found, resend its signed refund.created or refund.updated event to recover the association. Never clear the attempt or invent a new key to bypass ambiguity. Failed refunds and manual replacements require an explicit recovery decision preserving the old history; this demo does not provide an operator retry UI or automated replacement refund. Keep unresolved obligations visible.

Stripe can hold card refunds pending when the available balance is insufficient; cancellation must not label these completed. For local failure and race coverage, run dotnet test against the SQL Server test container. No live payments are enabled.
