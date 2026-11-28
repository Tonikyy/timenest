# 005: Last-minute cancellation and refund policy

Status: accepted for the sandbox demo on 2026-11-28.

TimeNest fills last-minute openings. New bookings use a snapshotted LastMinuteNoRefund policy: customers may cancel before the appointment starts, but receive no automatic refund after paying. Show the policy before payment and the precise financial consequence before cancellation. This does not implement exception handling or finalize real-money terms.

Business cancellation creates a full refund obligation, including TimeNest's fee. Business cancellation is also available after the scheduled start when the service cannot be provided. Existing bookings retain LegacyFullRefundBeforeStart: customer cancellation before the start creates a full refund obligation. A migration assigns that legacy policy to existing rows; new reservations explicitly select the new policy.

Customer cancellation makes the slot available again; the existing more-than-40-minutes booking limit still applies. Business cancellation closes the slot. No-show handling, goodwill refunds, partial refunds and conditional refunds after resale are separate future work.

Booking cancellation, original payment and refund history are separate records. Under one SQL transaction, lock the booking, authorize its owner, record cancellation, release/close the slot and enqueue any refund. Repeated or competing cancellation requests cannot create multiple refunds; the first committed cancellation determines the outcome. A customer sees Cancelled independently of refund progress.

Unpaid reservations can be abandoned. First persist the request and prevent new Checkout attempts. Release only when no provider attempt exists, or Stripe confirms the existing session expired and unpaid. If payment wins the race, keep the confirmed booking and require a separate, explicit paid-cancellation action.

Stripe refunds reverse the destination transfer and refund the platform fee. Persist the attempt before calling Stripe and reuse one stable idempotency key. Lost responses, pending refunds and out-of-order callbacks require provider reconciliation. Failed or ambiguous refunds must remain visible, never be reported as completed or silently replaced.

Separate PRs deliver lifecycle/persistence, provider refund processing/recovery, and Finnish customer/business screens. The lifecycle PR queues refunds; the dependent provider PR executes them.

Sources:
- [Destination charge refunds](https://docs.stripe.com/connect/destination-charges#issue-refunds)
- [Stripe refund states and events](https://docs.stripe.com/refunds)
- [Expiring an unpaid Checkout session](https://docs.stripe.com/api/checkout/sessions/expire)
