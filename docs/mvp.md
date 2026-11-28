# MVP plan

## Confirmed decisions

- Build a working demo first, then a real pilot.
- ASP.NET Core, Angular, and Azure.
- SQL Server locally; Azure SQL when deployed.
- Collect customer payment and a platform fee through TimeNest from the first MVP.
- Preserve the original TimeNest logo and visual brand.
- Separate pull requests for separate concerns; dependent work may be stacked.

## First demo

A business creates its profile, completes sandbox payment onboarding, and publishes a cancellation opening. A customer discovers an opening, temporarily reserves it, pays through hosted checkout, and sees a confirmed booking. Both sides can view and cancel bookings. Refunds have their own visible status.

Start with one owner and one location per business, Finnish UI, EUR, and appointment times in Europe/Helsinki. Demo businesses and appointments must be clearly fictional.

Include account registration, verification, recovery, nearby search, service/date filtering, booking ownership, cancellation, payment/refund recovery, and responsive customer/business screens.

## Sandbox payment policies

- Stripe Connect sandbox Express onboarding and hosted card Checkout are implemented. A real sandbox customer payment reached a server-verified confirmed booking on 2026-11-27.
- Fixed EUR 5 application fee deducted from the advertised price; make the fee configurable and snapshot it on each booking.
- With destination charges, payment-provider costs come out of the platform's proceeds.
- New bookings: customer cancellation before the start releases the slot without an automatic refund after payment. Business cancellation creates a full refund including the platform fee. Existing bookings retain their original full-refund-before-start policy; see decisions/005-cancellations-and-refunds.md.
- Reservations last 35 minutes; start Checkout within four minutes, for appointments more than 40 minutes away. Never release a hold based solely on a local timer while payment remains possible.
- Only card payments are enabled. Cancellation lifecycle and durable refund obligations are implemented; the dependent provider and UI slices add processing and screens.

These are demo policies, not finalized terms for a real-money pilot.

## Invariants

- Only future available slots can be reserved; one active reservation or booking per slot.
- Compute price, currency, fee, and payment recipient on the server.
- Confirm only a verified successful payment, regardless of browser redirects.
- Repeated requests/events cannot create duplicate charges, confirmations, or refunds.
- Reconcile provider state before releasing an expired reservation.
- Never silently change a booked service, price, or time.
- Preserve cancellation, payment, and refund history separately.
- An unavailable database must produce an honest failure, never substitute fictional API results.

## Milestones

1. **Runnable foundation:** Angular calls ASP.NET Core and SQL Server; migration, clearly marked demo openings, CI, original branding.
2. **Business publishing:** accounts, business ownership/profile, publish/manage demo slots. Stripe sandbox onboarding is implemented separately; a real hosted round trip with fictional test data reached ready on 2026-11-27.
3. **Discovery and paid booking:** Stripe sandbox onboarding, location/service/date search, reservation, Checkout, verified payment confirmation, double-booking protection.
4. **Cancellation and recovery:** refunds, abandoned-checkout cleanup, retry-safe webhook processing, dashboards, notification delivery.
5. **Azure demo:** Bicep, OIDC deployment, controlled migrations, monitoring, costs, end-to-end smoke tests.
6. **Real pilot:** live onboarding, agreed commercial/cancellation policies, operational recovery and real email delivery.

The demo is complete when publish → discover → pay → confirm → cancel → refund works and concurrent customers cannot purchase the same slot.

## Deferred

Saved-search alerts, calendar integrations, staff scheduling, multiple locations, reviews, native apps, and additional currencies.

## Existing backlog reconciliation

The original GitHub milestones are background context; the sequence above supersedes API-first / Azure-first / frontend-last ordering.

- #1–3, #8, #10, #16: foundation; #2 now records the SQL Server/Azure SQL decision.
- #4–5, #9, #17, #19: model, identity and business publishing.
- #6–7, #18: discovery, reservations and paid booking.
- Expand #4, #7, #8, #18 and #19 for payment state and recovery.
- Add dedicated work items for Connect onboarding, Checkout/webhooks, refunds, and reconciliation before implementing those features.
- #11–15, #20: complete Azure demo.
- #21–23: deferred saved-search notification pipeline. Use retry-safe publishing and duplicate-safe processing rather than promising exactly-once delivery.

This plan does not imply that the existing GitHub issues have been rewritten or closed.
