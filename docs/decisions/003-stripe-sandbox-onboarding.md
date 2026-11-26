# 003: Stripe sandbox business onboarding

Status: accepted for the demo.

## Decision

Use Stripe Connect with Express accounts in Finland, EUR, and Stripe-hosted onboarding through the Accounts v1 API and official Stripe.net SDK. Request card_payments and transfers capabilities in preparation for the proposed destination-charge Checkout flow. This does not finalize fee/refund policies or implement payments.

Only owned demo businesses can start onboarding. The backend accepts only sk_test_ keys. A missing key leaves the feature unavailable; invalid/live keys fail startup. Use a separate database for each Stripe sandbox and keep its platform/key association stable. Key rotation within the same sandbox is supported; switching sandboxes requires a separate demo database.

Return/refresh URLs come from a configured trusted origin. Both land on the Angular payments page. Returning is not evidence of completion: GET retrieves current details/capabilities from Stripe, with no cached ready flag. A fresh link is created only by an authenticated, antiforgery-protected POST. The refresh page lets the owner explicitly continue, also after signing in again. Link URLs are short-lived credentials: never persist or log them.

## Recovery and concurrency

SQL stores one connection per business before the first provider request. Account creation uses a stable idempotency key derived from the business ID and a fixed payload (no mutable profile details). Concurrent calls and lost responses reuse that key. Once the account ID is stored, subsequent attempts create links for that account.

An unresolved creation older than 23 hours requires operator reconciliation. Stripe may prune v1 idempotency keys after 24 hours, so automatically creating again could duplicate an account. Keep the pending row and inspect the original sandbox's request logs/accounts using the timenest_business_id metadata. See the local setup guide for the recovery procedure. Never erase pending history to blindly retry.

No webhook or persisted account-status projection is introduced: this slice reads authoritative state on every status request. Before Checkout is implemented, recheck payment eligibility server-side; never use a browser status result to authorize charges. Account updates, Checkout webhooks and payment reconciliation belong to that later slice.

## Verification limits

Automated tests use real SQL Server and the real Stripe SDK with simulated HTTP responses. They cover ownership, CSRF, concurrent creation, fixed provider payloads, lost responses, link failure, status changes and expiry recovery. A Stripe-hosted browser round trip requires locally configured sandbox credentials; simulated tests cannot establish that the chosen Stripe platform is correctly configured.

## Sources

- [Stripe-hosted onboarding](https://docs.stripe.com/connect/hosted-onboarding)
- [Create an account](https://docs.stripe.com/api/accounts/create)
- [Account links](https://docs.stripe.com/api/account_links/create)
- [Idempotent requests](https://docs.stripe.com/api/idempotent_requests)
- [Testing Connect](https://docs.stripe.com/connect/testing)
