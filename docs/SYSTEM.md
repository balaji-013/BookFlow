# BookFlow — SYSTEM.md

Architecture decisions, targets and constraints for the BookFlow MVP (multi-tenant bookstore SaaS, Tamil Nadu pilot).

Source documents: `Product Vision / BRD / MVP Scope` (skills.txt), engineering method (knowledgeBase.txt), learning roadmap (Requirement.txt).

## 1. Product understanding

| Question | Answer |
|---|---|
| Problem | Small/medium bookstores run billing, stock and purchasing on paper or disconnected tools; stock is inaccurate, reports are manual, dues are untracked. |
| Users | Bookstore Owner, Billing Employee, Inventory Manager, Business Administrator (one tenant = one bookstore in v1, see ADR-0001). |
| Core use cases | POS billing with GST invoice; purchase → goods receipt → stock; stock ledger and adjustments; customer/supplier dues and payments; sales returns; daily/monthly reports. |
| Essential (P0) | Tenant onboarding, auth, RBAC, catalog, suppliers, customers, purchasing, inventory, POS, invoices, payments, sales returns, dashboard, reports, audit log, tenant isolation. |
| Optional (P1) | Barcode scanner (works today: scanners type into the search box), Excel import (included), Excel/PDF reports (Excel included), low-stock list (included), Tamil UI, offline POS. |
| Out of scope | Distributor/publisher ERP, marketplace, AI recommendations, royalties, data warehouse, microservices, multi-country tax. |
| Data CRUD | Tenants, users, books/authors/publishers/categories, suppliers, customers, purchase orders, goods receipts, stock balances, inventory movements (append-only), sales invoices, sales returns, payments & allocations, credit notes, audit logs. |
| External services | None required in v1. SMTP for password reset is optional and off by default. |

## 2. Requirements and estimates

All figures are labelled **MEASURED**, **TARGET** or **GUESS**.

| Metric | Value | Label | Reasoning |
|---|---|---|---|
| Tenants (month 6) | 10–20 | TARGET | From BRD commercial goal |
| Tenants (year 1) | 100 | GUESS | Plan capacity for 5× the target |
| Users per tenant | 3–5 | GUESS | Owner + 2–4 staff |
| Daily active users | ≤ 500 | GUESS | 100 tenants × 5 users |
| Invoices per tenant per day | 50–200 | GUESS | Small/medium bookstore footfall |
| Invoices per day (platform, year 1) | ≤ 20,000 | GUESS | 100 × 200 |
| Peak requests/second | ~25 rps | GUESS | Evening peak 5× average; each invoice ≈ 10 API calls (search, cart, submit) |
| Average requests/second | ~5 rps | GUESS | 20k invoices × 10 calls ÷ 40,000 business seconds |
| Read/write ratio | 80/20 | GUESS | Search-heavy POS |
| Storage growth | ~2 KB per invoice incl. items/movements → ~40 MB/day worst case; ≤ 15 GB/year | GUESS | Fits a single SQL Server instance comfortably |
| API latency (search, add to cart) | p95 < 300 ms | TARGET | Billing goal: invoice in < 1 min |
| API latency (create invoice) | p95 < 800 ms | TARGET | Transactional write |
| Availability | 99.9 % monthly | TARGET | BRD |
| RPO | 15 min (log backups) / 24 h (full) | TARGET | Backup scripts in `deploy/scripts` |
| RTO | 1 h | TARGET | Restore runbook in DEPLOYMENT.md |

Little's Law: concurrency = 25 rps × 0.3 s ≈ 8 requests in flight at peak. A single API instance with a 32-connection pool is more than enough; the pool size is **not** derived from concurrency, it is a safety bound.

## 3. High-level architecture

**Modular monolith + Clean Architecture + DDD-lite**, one deployable API, one SQL Server database, Angular SPA served by nginx.

```
Browser (Angular 19 SPA, Angular Material)
        │ HTTPS
   nginx (static SPA + reverse proxy /api)
        │
ASP.NET Core 8 Web API  ──  Serilog → stdout (docker logs / Loki)
   ├─ Identity & Tenancy      (register tenant, login, JWT + refresh, users, roles)
   ├─ Catalog                 (books, authors, publishers, categories, import)
   ├─ Parties                 (customers, suppliers)
   ├─ Purchasing              (purchase orders, goods receipts)
   ├─ Inventory               (stock balances, movement ledger, adjustments)
   ├─ Sales                   (POS invoices, invoice numbering, returns)
   ├─ Finance                 (payments, allocations, credit notes, outstanding)
   ├─ Reporting               (dashboard KPIs, reports, Excel export)
   └─ Audit                   (change log interceptor)
        │ EF Core 8 (SQL Server provider)
   SQL Server 2022 (single DB, TenantId on every tenant-owned table)
```

Component contracts (responsibility / inputs / outputs / dependencies / scaling / failure) are in `MODULES.md`.

Not used in v1 (with reasons, see ADR-0004): Redis, message queue, Hangfire, API gateway, microservices. The design keeps seams (interfaces for cache, clock, email) so they can be added when measured need appears.

## 4. Distributed-system considerations

| Concern | Decision |
|---|---|
| Consistency | Strong consistency for stock, invoices, payments (single DB, transactions). Dashboard numbers are read-committed snapshots — acceptable. |
| Overselling | Atomic conditional `UPDATE StockBalances ... WHERE QuantityOnHand >= @qty`; `@@ROWCOUNT = 0` ⇒ `InsufficientStockException` ⇒ 409. Inside the invoice transaction. |
| Duplicate invoice submission | `Idempotency-Key` header persisted in `IdempotencyKeys (TenantId, Key)` unique; replay returns stored response. Also a client-side submit lock. |
| Invoice numbering | `InvoiceSequences` row per tenant + financial year, read with `UPDLOCK, HOLDLOCK` inside the invoice transaction. |
| Concurrency on master data | `RowVersion` (rowversion) on editable aggregates; 409 on conflict. |
| Scaling | Stateless API → horizontal scale behind nginx when needed; SQL Server vertical first, read replica later. |
| Caching | None in v1; `IMemoryCache` for tax/HSN lookups only. Cache-aside for catalog search is a documented P1 option. |
| Background jobs | None required. Reports computed on request with indexed queries; export limited to 100k rows. |
| Retries | Client retries only idempotent GETs; invoice POST retried with the same Idempotency-Key. EF connection resiliency (`EnableRetryOnFailure`) is deliberately **off** because services open explicit transactions; transient SQL failures surface as 503 and the client retries with the same Idempotency-Key. |

## 5. Security and reliability summary

See `SECURITY.md`. Highlights: JWT access (15 min) + rotating refresh tokens (7 days, hashed at rest); tenant from token claims only; EF global query filters + explicit checks; RBAC policies; FluentValidation on every command; rate limits on login and search; security headers; secrets via environment; audit log of all create/update/delete on business entities.

Highest-risk failures: cross-tenant leak (mitigated by filters + integration tests), oversell (atomic update + concurrent test), double invoice (idempotency), lost database (backups + restore drill).

## 6. Documentation set

`SYSTEM.md` (this), `MODULES.md`, `API.md`, `DATABASE.md`, `SECURITY.md`, `DEPLOYMENT.md`, `adr/`.

## 7. Design review — known weaknesses

1. Single SQL Server instance is a SPOF; mitigated by backups, acceptable for pilot (ADR-0004).
2. Reports query the OLTP database; fine at pilot scale, revisit with read replica at > 1M invoice rows.
3. No offline POS — a network outage stops billing (ADR-0005, explicitly deferred).
4. GST rules are configurable per HSN but only intra-state CGST/SGST is implemented; IGST appears when institutional customers in other states are onboarded (ADR-0003).
5. Excel import runs synchronously; limited to 5,000 rows per file in v1.
