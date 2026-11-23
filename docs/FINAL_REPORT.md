# BookFlow — Build Report (v1.0.0 MVP candidate)

Date: 2026-11-09. Scope: full P0 MVP as approved (tenancy, auth, users/roles, catalog, suppliers, customers, purchasing, inventory, POS/invoices, payments, returns, dashboard/reports, audit). Deployment target: Docker Compose on a VPS.

## 1. Executive summary

The complete system is written: an ASP.NET Core 8 modular monolith (4 projects + 2 test projects, ~60 source files), an Angular 19 SPA (14 feature screens), a production Docker Compose stack with automatic HTTPS and scheduled backups, CI with SQL Server-backed integration tests, and the architecture document set with 10 ADRs.

**Verification status — read this first.**

| Item | Status | Evidence |
|---|---|---|
| Angular production build | **MEASURED – passes** | `ng build --configuration production`: 0 errors, initial bundle 597 kB raw / 140 kB transfer |
| Angular login/register render | **MEASURED – passes** | Playwright screenshots, 0 console errors |
| .NET Domain project (pricing engine, entities) | **MEASURED – compiles** | `dotnet build` with .NET SDK 8.0.133 |
| .NET Application/Infrastructure/API/tests | **NOT COMPILED HERE** | NuGet (`api.nuget.org`) was blocked by the sandbox's egress policy for the whole session, so packages (EF Core, JwtBearer, Serilog, FluentValidation, ClosedXML, xUnit) could not be restored. Sources were syntax-checked with Roslyn against the framework reference assemblies (0 syntax errors) and manually reviewed, but type errors against third-party APIs are possible. **Run `dotnet build` first** — see BUILD_VERIFY.md. |
| Unit tests (23) / integration tests (16) | **WRITTEN, NOT EXECUTED** | Same cause. CI workflow runs them against SQL Server on every push. |
| EF Core migration `InitialCreate` | **NOT GENERATED** | Requires `dotnet ef` (NuGet). The API falls back to `EnsureCreated` when no migrations exist so the stack boots; generate the migration before the first production deployment (BUILD_VERIFY.md step 3). |
| Docker images / compose | **NOT RUN HERE** | Docker daemon unavailable in the sandbox. Compose files follow the documented images' conventions. |

Everything labelled "not executed" is a known limitation of this environment, not of the design; the first run on a developer machine or in GitHub Actions will surface any remaining compile issues, which are expected to be small (API-shape mismatches), not architectural.

## 2. Implemented features (P0)

Tenant self-registration with default store and Owner · JWT access (15 min) + rotating refresh tokens with reuse detection · roles Owner / StoreManager / BillingEmployee / InventoryManager enforced by policies · tenant profile (GSTIN, state code, invoice prefix/footer) · employees CRUD, activate/deactivate, password reset · books (edition-level records, ISBN/barcode, HSN, GST rate, reorder level, opening stock), authors/publishers/categories, Excel import with template · customers (type, credit limit, GSTIN/state), statements, outstanding; suppliers, statements, supplier payments · purchase orders (draft/submit/cancel), partial goods receipts, direct receipts, purchase returns · stock balances + immutable movement ledger, adjustments, damaged bucket, low-stock, valuation · POS: scanner path (`/books/lookup`), quick search, line and invoice discounts (Strategy), GST intra/inter-state split, rupee rounding, per-FY gap-free numbering, idempotent submission, credit sales with limit check, printable HTML invoice · payments with allocation to invoices, credit notes, refunds · invoice cancellation with reversal movements · sales returns (resalable/damaged; cash/credit-note/adjust) · dashboard KPIs + trend + top books + payment mix · 9 reports with Excel export · audit log of all business-entity changes · health endpoints, rate limiting, security headers, problem-details errors.

P1 delivered early: barcode scanner support, bulk Excel import, Excel reports, low-stock list, credit management, inventory aging. Not in this release: Tamil UI strings, offline POS, PDF reports, multi-branch, SaaS subscription billing.

## 3. Architecture (as built)

Modular monolith, Clean Architecture: `Domain` (entities, enums, pricing engine, exceptions) → `Application` (use-case services, DTOs, FluentValidation validators, interfaces) → `Infrastructure` (EF Core DbContext + configurations, tenant/audit interceptor, inventory SQL, numbering, JWT, Excel) → `API` (controllers, middleware, auth policies, Program). Angular SPA talks to `/api/v1`. See SYSTEM.md, MODULES.md.

## 4. Database

30 tables (DATABASE.md). Every tenant-owned table has `TenantId` with a leading-column index and an EF global query filter; uniqueness is tenant-scoped; money is `decimal(18,2)`, costs `decimal(18,4)`; `InventoryMovements` is append-only (enforced by the interceptor); `StockBalances` has a non-negative CHECK constraint as a second line of defence behind the atomic conditional UPDATE.

## 5. Security controls

See SECURITY.md. Highlights implemented: tenant identity only from JWT claims; cross-tenant writes rejected by interceptor; PBKDF2 password hashing; refresh-token family revocation on reuse; FluentValidation on every command; parameterised SQL everywhere (two `FormattableString` raw statements); rate limits (auth 10/min/IP, search 120/min/user, global 600/min/tenant); request timeout 30 s; security headers + CSP in nginx; secrets via environment; audit log with masked sensitive fields; non-root containers.

## 6. Tests written

Unit (BookFlow.UnitTests, 23 tests): invoice calculator (GST split, discounts, proportional distribution, rounding, guards), financial year labels, money rounding, sequential GUID uniqueness, validators.
Integration (BookFlow.IntegrationTests, 16 tests, real SQL Server via `BOOKFLOW_TEST_CONNECTION`): full sale flow, sequential numbering, **10 concurrent sales of the last copy → exactly one 201**, oversell 409 leaves no partial invoice, idempotent replay, missing key rejected, walk-in must pay / credit sale / payment allocation, returns (resalable + damaged), cancellation, dashboard totals + Excel export, **tenant isolation across read/update/sell**, per-tenant numbering, anonymous 401, wrong password, refresh-token reuse revocation, role enforcement, problem-details validation, PO partial receipt + supplier balance, Excel import.
E2E (Playwright, `e2e/`): register → add book → sell; oversell rejected in the UI.

## 7. Performance

All figures in SYSTEM.md §2 are TARGET or GUESS; nothing has been load-tested. The POS hot path is two indexed lookups and one transaction with row-level work only; no N+1 on list endpoints except purchase-order/receipt lists which do one extra query per row (bounded by page size ≤ 200, acceptable for pilot, noted as debt).

## 8. Known limitations and technical debt

1. First document of a new financial year under concurrent load can hit a deadlock on the sequence row insert (DocumentNumberGenerator); the client retries with the same Idempotency-Key. Fix later by pre-creating the FY rows on 1 April.
2. Excel import is synchronous and capped at 5,000 rows.
3. Reports run on the OLTP database.
4. Purchase order / GRN list endpoints map rows with per-row lookups (N+1, bounded).
5. Customer `Outstanding` is computed by subquery per row; add a materialised column if customer lists grow past a few thousand.
6. No email/SMS; password reset is Owner-driven.
7. No Tamil UI strings yet (fonts and layout are ready; `Noto Sans Tamil` font stack declared).
8. Migrations not yet generated (see verification table).

## 9. Deployment, backup, recovery

DEPLOYMENT.md: one-command Compose deployment with Caddy HTTPS; `migrate` job gates API start; nightly full + 15-minute log backups to a volume with retention; off-site copy via rclone cron; `restore.sh` applies full + logs; RPO 15 min / RTO 1 h (TARGET).

## 10. Recommended next steps

1. `dotnet build` + `dotnet test` on a dev machine (BUILD_VERIFY.md) and fix anything the compiler reports.
2. Generate `InitialCreate` migration, commit it, run the integration tests against SQL Server (or push to GitHub and let CI do it).
3. Deploy to a pilot VPS, run the Playwright e2e against it, do a restore drill.
4. Pilot with 3–5 bookstores; capture p95 latencies (turns GUESS into MEASURED) and invoice-per-minute timings against the 1-minute billing goal.
5. P1 backlog: Tamil UI, PDF invoice, GSTR-1 export, subscription management.
