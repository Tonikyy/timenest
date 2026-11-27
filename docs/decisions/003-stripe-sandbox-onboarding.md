# 003: Stripe sandbox business onboarding

Status: accepted for the demo.

## Decision

Use Stripe Connect with Express accounts in Finland, EUR, and Stripe-hosted onboarding through Accounts v2 creation and the official Stripe.net SDK. Request merchant card_payments and recipient stripe_balance.stripe_transfers capabilities in preparation for the proposed destination-charge Checkout flow. This does not finalize fee/refund policies or implement payments.

Only owned demo businesses can start onboarding. The backend accepts only sk_test_ keys. A missing key leaves the feature unavailable; invalid/live keys fail startup. Use a separate database for each Stripe sandbox and keep its platform/key association stable. Key rotation within the same sandbox is supported; switching sandboxes requires a separate demo database.

Return/refresh URLs come from a configured trusted origin. Both land on the Angular payments page. Returning is not evidence of completion: GET retrieves current details/capabilities from Stripe, with no cached ready flag. A fresh link is created only by an authenticated, antiforgery-protected POST. The refresh page lets the owner explicitly continue, also after signing in again. Link URLs are short-lived credentials: never persist or log them.

## Recovery and concurrency

SQL stores one connection per business before the first provider request. Account creation uses a stable idempotency key derived from the business ID and a fixed payload, including the verified owner email snapshotted in SQL before the first request. Concurrent calls and lost responses reuse that key. Once the account ID is stored, subsequent attempts create links for that account.

An unresolved v2 creation older than 29 days requires operator reconciliation, before Stripe's 30-day v2 replay window ends. Pending v1 attempts also require reconciliation before any v2 request; a new API namespace does not inherit a v1 idempotency result. Existing associated accounts continue to work. Keep the pending row and inspect the original sandbox's request logs/accounts using the timenest_business_id metadata. See the local setup guide for the recovery procedure. Never erase pending history to blindly retry.

No webhook or persisted account-status projection is introduced: this slice reads authoritative state on every status request. Before Checkout is implemented, recheck payment eligibility server-side; never use a browser status result to authorize charges. Account updates, Checkout webhooks and payment reconciliation belong to that later slice.

## Accounts v2 compatibility fix (2026-11-27)

The first real test against a new Connect sandbox rejected POST /v1/accounts with HTTP 400. New account creation now uses POST /v2/core/accounts with contact_email, FI identity, Express dashboard, EUR defaults and application fee/loss responsibilities. The migration marks existing attempts as v1; new attempts explicitly store v2. It preserves existing account IDs and never silently recreates an uncertain legacy account. Account reads and hosted links continue to use Stripe's supported v1 interoperability, preserving the frontend readiness contract.

## Verification limits

Automated tests use real SQL Server and the real Stripe SDK with simulated HTTP responses. They cover ownership, CSRF, concurrent creation, fixed provider payloads, lost responses, link failure, status changes and expiry recovery. On 2026-11-27, a real configured Stripe sandbox created an account through v2, opened hosted onboarding, accepted fictional identity data and Stripe's test bank account, returned to TimeNest, and reported submitted details plus enabled charges/payouts and active transfers through the server-side status read. All 70 backend tests passed. Early-return/resume and capability revocation remain covered by simulated tests, not by this real hosted run. Checkout, real payments and live onboarding are outside this verification.

## Sources

- [Stripe-hosted onboarding](https://docs.stripe.com/connect/hosted-onboarding)
- [Create a v2 account](https://docs.stripe.com/api/v2/core/accounts/create)
- [Account links](https://docs.stripe.com/api/account_links/create)
- [API v2 idempotency](https://docs.stripe.com/api-v2-overview#idempotency)
- [v1/v2 interoperability](https://docs.stripe.com/connect/accounts-v2/migrate-integration)
- [Testing Connect](https://docs.stripe.com/connect/testing)
