# 001: SQL Server locally, Azure SQL in Azure

Status: accepted.

## Context

TimeNest needs relational ownership, transactional reservation/booking rules, and location searches. The owner explicitly prefers SQL Server / Azure SQL and wants a stack they can understand and maintain.

## Decision

Use EF Core's SQL Server provider. Run SQL Server in Docker for development and integration tests. Deploy to Azure SQL in the Azure milestone. Use NetTopologySuite with SQL Server geography for distance calculations.

## Consequences

Use migrations, SQL constraints and transactions to enforce invariants. Test database behavior against SQL Server rather than an in-memory substitute. Docker is required for local integration tests; the .NET SDK and Node.js are also needed.

Choose Azure SQL sizing and cost limits during deployment planning; do not assume the service is free or automatically scales to zero.
