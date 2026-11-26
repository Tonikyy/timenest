# TimeNest

TimeNest helps appointment-based businesses fill cancellations and helps customers find and pay for nearby openings.

This repository restarts the [original web prototype](https://github.com/Tonikyy/tnest-website) with **ASP.NET Core, Angular, and Azure**. The original logo and brand are retained.

## Direction

Build a working demo with sandbox payments first, then prepare a real pilot. Customers pay through TimeNest; the platform fee is part of the MVP.

- One ASP.NET Core application with feature folders.
- Angular with standalone components, reactive forms, and signals.
- EF Core with SQL Server locally and Azure SQL when deployed.
- Proposed hosting: Angular and the API on one Azure App Service.
- Proposed identity: ASP.NET Core Identity with cookies and antiforgery protection.
- Proposed payments: Stripe Connect and hosted Checkout in a sandbox.

See [MVP scope and milestones](docs/mvp.md), [architecture](docs/architecture.md), and [decisions](docs/decisions/README.md).

## Brand

The original SVG, purple palette, and Poppins / Playfair Display / Great Vibes typography will be carried forward. See [brand reference](docs/brand.md).

## Development

Implementation is delivered in separate, reviewable pull requests. Setup instructions are added with the API and Angular foundations. Nothing in this repository provisions Azure resources or enables live payments automatically.

## API foundation

The read-only discovery API, SQL Server migration, and fictional demo data are implemented. See [local setup and API reference](docs/local-development.md). Accounts, publishing, booking and payments remain later work.
