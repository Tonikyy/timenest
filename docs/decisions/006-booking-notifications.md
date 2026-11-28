# 006: Booking and cancellation notifications

Status: accepted for the demo on 2026-11-28.

Use a signed-in inbox plus the existing Development-only local-mailbox approach. No external emails, SMS, saved-search alerts or Azure resources are added in this step.

Record booking confirmation and paid-booking cancellation for the customer and business owner. Notify both about successful, failed or attention-required refunds, including a bank failure after an earlier success. Do not notify for temporary unpaid holds, ordinary hold expiry or unpaid abandonment. Historical bookings are not backfilled with notifications.

Each immutable notification snapshots its recipient user ID, verified email (when available), audience, Finnish subject/body and owned booking link at the event. Preserve booking/service/price/time snapshots; render appointment and event times in Europe/Helsinki with the year. Messages explicitly describe the event time and link to the current booking status. Cancellation with a queued refund must not claim it completed.

The SQL notification table also serves as the email outbox. Insert notification rows inside the transaction that changes booking/refund state. A unique booking/event/recipient index prevents duplicate records; transition locks preserve event order. An identity sequence orders events even when their timestamps are equal. The stable GUID identifies inbox items and mailbox artifacts.

Email delivery is separate from the transaction. Lease one eligible row at a time, retry failures with bounded backoff, and preserve exhausted failures for inspection. Preserve order per booking/recipient while earlier messages are pending. One failed message must not stop other bookings. Expired leases allow restart recovery. Inbox read state is independent from delivery.

The Development sender writes immutable JSON messages outside the web root, using the notification GUID as a stable filename and an atomic move. A retry after file delivery but before SQL acknowledgement reuses that artifact. This is local deduplication, not an exactly-once claim for a future email provider. Real delivery will need a provider-specific idempotency/receipt strategy. Missing verified recipient addresses remain visible in the inbox and are explicitly marked unavailable for email.

The inbox API is authenticated and scoped to the recipient. Read markers require CSRF, are idempotent, and never disclose another user's messages. Do not expose recipient addresses, leases, retry details or email-delivery internals in product screens.

Deliver separately: transactional records/templates; local mailbox delivery/recovery; owned inbox API and UI. Do not retroactively use the saved-search pipeline issues #21–23 as completed work.
