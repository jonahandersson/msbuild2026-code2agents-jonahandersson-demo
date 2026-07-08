# shop-api

A small ASP.NET Core API used as the **demo target** for the agent-driven rollback
scenario. The MCP server (the Function app in the parent repo) queries this repo's
build pipeline; the agent opens rollback PRs against this repo.

The scenario is: a schema migration on `Orders` times out during deployment,
the agent diagnoses it, and the agent opens a rollback PR back to the last green
commit.

## What's in here

- A minimal Web API exposing `/orders` and `/customers`
- EF Core with an Orders + Customers schema
- A pipeline (`azure-pipelines.yml`) that builds, tests, and runs migrations
- A migration script (`scripts/run-migrations.ps1`) that fails on purpose in a
  controlled, reproducible way — so the demo behaves the same every time

## How the controlled failure works

The migration script reads a flag from `Data/Migrations/manifest.json`. When the
manifest sets `"simulateTimeout": true`, the script writes a realistic
"command timeout expired" log entry and exits with a non-zero code. The pipeline
fails. The build log contains the exact line the agent is going to diagnose.

This is a deliberate choice for demo reliability. The migration file itself is
real (`20260515_AddCustomerLoyalty.cs`) — if you'd rather run it against a real
SQLite/PostgreSQL instance in the pipeline, see "Switching to real migrations"
at the bottom of this README.

## Quick local run

```bash
cd src/ShopApi
dotnet run
# → https://localhost:7081/orders
```

## Switching to real migrations

If you want the pipeline to run real EF migrations against a real database:

1. Add a SQL Server or PostgreSQL service container to `azure-pipelines.yml`.
2. Replace the `pwsh scripts/run-migrations.ps1` step with
   `dotnet ef database update --project src/ShopApi`.
3. Make the migration intentionally slow (a `Thread.Sleep` in the migration's
   `Up` is the lazy way; a real data seed with millions of rows is the
   non-lazy way).
4. Set the pipeline step's `timeoutInMinutes: 1`.

For a 20-minute conference talk where reliability matters more than authenticity,
the simulated approach is the right call.
