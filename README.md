# BookFlow

Multi-tenant bookstore management SaaS — billing (GST), inventory, purchasing, customers/suppliers, payments, returns and reports. Built for small and medium bookstores in Tamil Nadu.

**Stack:** Angular 19 + Angular Material · ASP.NET Core 8 Web API (modular monolith, Clean Architecture) · EF Core 8 · SQL Server 2022 · Docker Compose · GitHub Actions.

## Repository layout

```
backend/          .NET solution (Domain → Application → Infrastructure → API, xUnit tests)
frontend/         Angular SPA (standalone components, signals)
deploy/           docker-compose (dev + prod overlay with Caddy HTTPS), backup/restore scripts, SQL hardening scripts
docs/             SYSTEM.md · MODULES.md · DATABASE.md · API.md · SECURITY.md · DEPLOYMENT.md · adr/
.github/          CI: build, unit + integration tests against SQL Server, image publish to GHCR
```

## Quick start (Docker)

```bash
cp deploy/.env.example deploy/.env         # set SA_PASSWORD and JWT_KEY
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
open http://localhost:8080                 # Register your business → start billing
```

Production with automatic HTTPS: see [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

## Development

```bash
cd backend  && dotnet build && dotnet test tests/BookFlow.UnitTests
cd frontend && npm ci && npx ng serve
```
Integration tests need a SQL Server: `BOOKFLOW_TEST_CONNECTION="Server=localhost,1433;Database=BookFlowTests;User Id=sa;Password=...;TrustServerCertificate=True" dotnet test tests/BookFlow.IntegrationTests`.

## What is in the MVP (P0)

Tenant self-registration · JWT auth with rotating refresh tokens · roles (Owner, Store manager, Billing employee, Inventory manager) · book catalog with ISBN/barcode, authors, publishers, categories, Excel import/template · suppliers & customers with statements and credit limits · purchase orders, partial goods receipts, direct receipts, purchase returns · immutable stock ledger with atomic oversell protection, adjustments, damaged stock, low-stock, valuation · POS with scanner path, line & invoice discounts, GST (CGST/SGST/IGST), rounding, per-financial-year invoice numbering, idempotent submission, printable invoice · payments with allocation, credit notes, refunds, cancellations · sales returns (resalable/damaged) · dashboard KPIs & nine reports with Excel export · audit log.

Roles, flows and invariants are documented in `docs/`; product decisions in `docs/adr/`.

## End-to-end tests

`cd e2e && npm i && npx playwright install chromium && BASE_URL=http://localhost:8080 npx playwright test` — runs the critical register → add book → sell → oversell-rejected flows against a running stack.
