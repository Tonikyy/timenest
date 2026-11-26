# TimeNest development

## Product and scope

Read docs/mvp.md and docs/architecture.md before changing behavior. Preserve the original logo and brand described in docs/brand.md. The first release is a demo with sandbox payments; live payments and Azure provisioning require an explicit deployment task.

## Code

- Use ASP.NET Core, Angular, EF Core, and SQL Server / Azure SQL.
- Keep one backend application with feature folders. Controllers handle HTTP; keep business rules testable.
- Use framework features directly. Add interfaces or extra layers only for a concrete boundary.
- Use standalone Angular components, reactive forms where appropriate, and signals for component state.
- Keep customer-visible text in Finnish initially. Do not show infrastructure details in product screens.
- Public reads may be anonymous; all future mutations need authentication, ownership checks, and antiforgery protection.
- Prices are integer cents and currency is EUR. Store instants as UTC; render appointment times in Europe/Helsinki.
- Preserve booking, payment, and refund history. Payment success must be verified server-side.
- Keep secrets out of source, command output, and test artifacts.

## Verification and delivery

- During code review, apply the additional simplicity guidance in [REVIEW.md](REVIEW.md).
- Run the builds and relevant tests for the changed area. Database behavior is tested against SQL Server.
- Use separate pull requests for separate concerns. Stack dependent PRs and explain the base dependency.
- Do not merge PRs automatically.
- Keep setup instructions and the milestone status accurate. Do not claim planned features are implemented.
