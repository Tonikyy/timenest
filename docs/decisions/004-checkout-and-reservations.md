# 004: Sandbox reservations and customer payments

Status: accepted for the demo implementation.

Use authenticated, verified customer accounts, Stripe-hosted Checkout, card payments only, EUR destination charges and a configurable EUR 5 application fee deducted from the advertised price. Snapshot the service, business/location, price, fee, recipient and time when reserving. Prices must exceed the fee. Real money, refunds and cancellation of confirmed bookings require later work.

## Reservation lifecycle

A reservation lasts 35 minutes; only slots starting more than 40 minutes away can be booked. This leaves room for Stripe's minimum 30-minute Checkout expiry. Checkout creation begins within four minutes of reservation; its absolute expiry and request payload remain stable across retries.

SQL Server enforces one active reservation/booking per slot using a filtered unique index. Changing the vacancy to Held and inserting the booking share a transaction. Rowversion rejects concurrent publication edits. Same-customer retries return their existing active reservation; another customer gets a conflict. Confirmed bookings remain active.

A timer never proves payment is impossible. Once a Checkout attempt is recorded, keep the hold until Stripe confirms the session expired and unpaid, or confirms payment. Unknown outcomes remain held for reconciliation. Never recreate a session after its idempotency window; preserve all booking and payment history.

The reservation foundation provides persistence and an internal service only. The dependent payment PR adds public endpoints, provider verification and reconciliation; the customer UI follows separately.

## Sources

- [Checkout expiry](https://docs.stripe.com/payments/checkout/managing-limited-inventory)
- [Destination charges](https://docs.stripe.com/connect/destination-charges)
- [Server-side fulfillment](https://docs.stripe.com/checkout/fulfillment)
