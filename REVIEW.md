# Code review guidance

Use this file as additional simplicity guidance during code review. Rely on the reviewer's normal instructions for general correctness, validation, prioritization, and reporting. [AGENTS.md](AGENTS.md), the [MVP plan](docs/mvp.md), and [architecture](docs/architecture.md) are the source of truth for repository constraints and scope.

Reduce code volume and conceptual complexity without compromising correctness, maintainability, or necessary boundaries. Check whether a change is overengineered or broader than the requested work. Fewer lines are not an improvement when they hide behavior.

## Simplicity cues

Treat these as investigation cues, not automatic findings. Raise a finding only when you can explain a concrete unnecessary cost and a simpler alternative that preserves required behavior.

- Require a current need for each new mechanism. Prefer existing repository patterns and standard ASP.NET Core, Angular, EF Core, SQL Server, or Azure features when they cover the need directly.
- Question interfaces with one implementation, factories for one product, configuration for values that do not vary, and abstractions created for hypothetical future uses. Keep them when they serve a real boundary, such as a payment provider.
- Keep one backend application with feature folders. Question generic repositories around `DbSet`, mediator pipelines, extra projects, and independently deployed services unless the change demonstrates a concrete need.
- Question parallel authorization, validation, retry, persistence, or state mechanisms when an established path already covers the responsibility.
- Question helpers, wrappers, aliases, constants, and named types used once when they merely rename an obvious expression or forward one operation. Keep them when they materially improve clarity, express domain meaning, or own a necessary lifecycle.
- Prefer a clear feature flow over special cases scattered through unrelated shared code. Avoid speculative compatibility paths, fallback data, and configuration switches without a current requirement.
- Check repeated null, empty, permission, and error guards for distinct responsibilities. Client feedback and server validation at a trust boundary are different responsibilities.
- In Angular, prefer standalone components, signals for local state, and reactive forms where appropriate. Add shared state or reusable components when actual consumers justify them.
- Question comments that only restate code. Preserve explanations of business rules, tradeoffs, and non-obvious constraints.
- Cover meaningful changed behavior and important branches without disproportionate scaffolding or tests that only mirror implementation details. Verify database-dependent behavior against SQL Server.

## Boundaries to preserve

Apply these checks when the change touches the relevant behavior. Do not require a foundation PR to implement later milestones.

- Enforce authentication and resource ownership in the backend for customer and business mutations. Angular visibility checks and route guards improve the experience; they do not secure the API. Preserve antiforgery protection for cookie-authenticated browser mutations and appropriate verification for provider callbacks.
- Keep input validation at trust boundaries, safe error handling, accessibility, and compatibility with stored customer data. A shorter implementation must not discard these responsibilities.
- Preserve one active reservation or booking per slot under concurrency. A UI check or a separate availability read does not replace database-enforced concurrency protection.
- Compute prices, currency, fees, and recipients on the server. Use integer cents and EUR, and preserve booked service, price, fee, and time snapshots. Store instants in UTC and display appointment times in Europe/Helsinki, including daylight-saving transitions.
- Verify payment success on the server. Preserve duplicate-safe request and webhook processing, reconciliation before releasing reservations, and separate booking, payment, cancellation, and refund history.
- Keep recovery needed for retries, out-of-order events, restarts, and partial failure between SQL Server and the payment provider. Do not remove it just because the happy path becomes shorter.
- Keep demo data explicit and development-only. Do not replace failed API or database calls with fictional successful results. Keep secrets out of source, logs, and test artifacts.
- Preserve the original logo and [brand](docs/brand.md), Finnish customer-facing text, and clear loading, empty, and error states. Keep infrastructure details out of product screens.
- Respect the demo-first scope. Sandbox payments and proposed policies are not live payment readiness; Azure provisioning and live payments require their own explicitly authorized work.

## Test confidence

Every review must explicitly answer: **Is this feature tested well enough that we can be confident it works?**

- Assess evidence for the intended behavior, meaningful failure paths, and regressions. Passing tests or a coverage percentage alone are not sufficient if the important behavior is untested.
- Match verification to the risk: focused business-rule tests, SQL Server integration tests for persistence and concurrency, and UI or end-to-end checks for affected user journeys. For booking and payment changes, consider duplicate requests, races, retries, and partial failures.
- State whether the evidence is sufficient and why. Distinguish checks actually run from tests only inspected, and name anything unverified. If confidence is insufficient, identify the concrete scenario and the smallest useful additional check; do not invent passing results.
- Keep the assessment proportionate. Documentation-only changes need relevant document checks, and simple changes do not need elaborate test scaffolding. Apply the same confidence question to every review.

## Review discipline

Ground findings in the changed code and a concrete scenario. Explain the consequence and point to the smallest useful location. Do not report personal style preferences, speculative future requirements, or unrelated pre-existing problems as defects in the change.

Do not flag an infrastructure outage by itself. Flag changed behavior that makes failures more likely, conceals them, loses data, or prevents required recovery. Booking and payment recovery remain product requirements even when an external dependency is unavailable.

Run or inspect checks relevant to the change and state any validation limits. Keep separate concerns in separate PRs, account for the declared base of stacked PRs, and do not demand already-provided foundation code again. Do not merge PRs automatically.
