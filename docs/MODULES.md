# BookFlow — MODULES.md

Each module lives in `BookFlow.Application/Features/<Module>` (use cases + DTOs + validators), with entities in `BookFlow.Domain/Entities/<Module>` and persistence in `BookFlow.Infrastructure`. Modules talk to each other only through application-layer interfaces (never through each other's DbSets from controllers).

| Module | Responsibility | Owns tables | Depends on | Failure behaviour |
|---|---|---|---|---|
| **Identity** | Tenant registration, login, refresh tokens, users, roles | Tenants, Users, RefreshTokens, Stores | — | Login failures → 401; locked after 10 failures/15 min via rate limiter |
| **Catalog** | Books (edition-level records), authors, publishers, categories, Excel import, search | Books, Authors, Publishers, Categories | Identity (tenant) | Duplicate ISBN/barcode in tenant → 409 |
| **Parties** | Customers, suppliers, outstanding balance view | Customers, Suppliers | Finance (balances) | — |
| **Purchasing** | Purchase orders, goods receipts (partial), purchase returns | PurchaseOrders, PurchaseOrderItems, GoodsReceipts, GoodsReceiptItems, PurchaseReturns, PurchaseReturnItems | Catalog, Parties, Inventory | Receipt over ordered qty → 400; posting is transactional |
| **Inventory** | Stock balances, immutable movement ledger, adjustments, damaged stock, low-stock, valuation | StockBalances, InventoryMovements, StockAdjustments | Catalog | Insufficient stock → `InsufficientStockException` → 409 |
| **Sales** | POS invoice creation, pricing (discount strategy), GST calc, invoice numbering, cancellation, sales returns | SalesInvoices, SalesInvoiceItems, InvoiceSequences, SalesReturns, SalesReturnItems, IdempotencyKeys | Catalog, Parties, Inventory, Finance | Oversell → 409; duplicate key → replay; cancel after payment → 409 |
| **Finance** | Payments, allocations to invoices, credit notes, supplier payments, outstanding | Payments, PaymentAllocations, CreditNotes, SupplierPayments | Sales, Purchasing, Parties | Over-allocation → 400 |
| **Reporting** | Dashboard KPIs, daily/monthly/book/category/supplier reports, stock movement, Excel export | (read-only) | all | Large exports capped at 100k rows |
| **Audit** | Record create/update/delete of business entities with user, tenant, timestamp, before/after JSON | AuditLogs | — | Never blocks a business transaction; failures logged |

## Contracts (application-layer interfaces)

```csharp
IInventoryService
  Task ReceiveAsync(IEnumerable<StockDelta> deltas, MovementRef reference, CancellationToken ct)   // +qty
  Task IssueAsync(IEnumerable<StockDelta> deltas, MovementRef reference, CancellationToken ct)     // -qty, atomic check
  Task AdjustAsync(StockAdjustmentCommand cmd, CancellationToken ct)

IInvoiceNumberGenerator
  Task<string> NextAsync(Guid tenantId, DateOnly invoiceDate, CancellationToken ct)   // INV/2026-27/000001

IPricingService
  InvoiceTotals Calculate(IEnumerable<PricedLine> lines, IDiscountPolicy discount)

IIdempotencyStore
  Task<IdempotentResult?> TryGetAsync(string key, CancellationToken ct)
  Task SaveAsync(string key, int statusCode, string responseJson, CancellationToken ct)

ITenantContext      Guid TenantId; Guid UserId; string Role; Guid? StoreId   (from JWT claims — never from request body)
IClock              DateTime UtcNow; DateOnly TodayIst
```

## Cross-cutting

* `TenantSaveChangesInterceptor` — sets `TenantId`, `CreatedAt/By`, `UpdatedAt/By`; refuses to save an entity whose TenantId ≠ current tenant.
* `AuditSaveChangesInterceptor` — writes `AuditLogs` rows in the same transaction.
* Global query filter `e => e.TenantId == _tenant.TenantId` on every `ITenantEntity`.
* `ExceptionHandlingMiddleware` — maps domain exceptions to RFC 7807 problem details (400 validation, 404 not found, 409 conflict/concurrency/insufficient stock, 403 forbidden).
* `Idempotency-Key` handled inside `SalesService.CreateInvoiceAsync` (not generic middleware) because the response must be stored in the same transaction as the invoice.
