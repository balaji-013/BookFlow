# BookFlow — API.md

Base URL: `/api/v1`. JSON, UTF-8. Authentication: `Authorization: Bearer <accessToken>`. All list endpoints accept `page` (1-based), `pageSize` (≤ 200), `search`, and return `{ items, page, pageSize, totalCount }`. Errors are RFC 7807 `application/problem+json` with `traceId`.

Live Swagger UI at `/swagger` (enabled in Development; behind `Swagger__Enabled=true` in production).

| Area | Method & path | Roles | Notes |
|---|---|---|---|
| Health | `GET /health`, `GET /health/ready` | anon | liveness / DB readiness |
| Auth | `POST /auth/register-tenant` | anon | creates Tenant + default Store + Owner user; returns tokens. Rate limit 5/min/IP |
| | `POST /auth/login` | anon | `{ email, password }` → `{ accessToken, refreshToken, expiresAt, user }`. Rate limit 10/min/IP |
| | `POST /auth/refresh` | anon | rotates refresh token |
| | `POST /auth/logout` | user | revokes refresh token |
| | `GET /auth/me` | user | current user + tenant summary |
| | `POST /auth/change-password` | user | |
| Users | `GET/POST /users`, `GET/PUT /users/{id}`, `POST /users/{id}/activate`, `POST /users/{id}/deactivate`, `POST /users/{id}/reset-password` | Owner | roles: Owner, StoreManager, BillingEmployee, InventoryManager |
| Tenant | `GET/PUT /tenant` | Owner | business profile, GSTIN, invoice footer |
| Stores | `GET /stores` | user | single default store in v1 |
| Catalog | `GET /books?search=&categoryId=&lowStock=` | user | search by title/ISBN/barcode; includes `quantityOnHand` |
| | `GET /books/lookup?code=` | user | exact ISBN or barcode — POS scanner path |
| | `POST /books`, `GET/PUT /books/{id}`, `DELETE /books/{id}` (deactivate) | Owner, StoreManager, InventoryManager | |
| | `POST /books/import` (multipart xlsx) | Owner, StoreManager, InventoryManager | columns: Title, ISBN, Barcode, Author, Publisher, Category, Edition, Language, PurchasePrice, SellingPrice, MRP, HSN, GstRate, OpeningStock, ReorderLevel |
| | `GET /books/import/template` | user | xlsx template |
| | `GET/POST /authors`, `/publishers`, `/categories`; `PUT/DELETE .../{id}` | Owner, StoreManager, InventoryManager | |
| Customers | `GET/POST /customers`, `GET/PUT /customers/{id}`, `GET /customers/{id}/statement` | user (write: not InventoryManager) | statement = invoices, payments, balance |
| Suppliers | `GET/POST /suppliers`, `GET/PUT /suppliers/{id}`, `GET /suppliers/{id}/statement`, `POST /suppliers/{id}/payments` | Owner, StoreManager, InventoryManager | |
| Purchasing | `GET/POST /purchase-orders`, `GET /purchase-orders/{id}`, `POST .../{id}/submit`, `POST .../{id}/cancel` | Owner, StoreManager, InventoryManager | |
| | `POST /purchase-orders/{id}/receipts` | same | partial receipt; updates stock + movements + supplier outstanding |
| | `GET /goods-receipts`, `GET /goods-receipts/{id}` | same | |
| | `POST /purchase-returns`, `GET /purchase-returns` | same | reduces stock |
| Inventory | `GET /inventory/stock?search=&lowStock=true` | user | balances with valuation |
| | `GET /inventory/movements?bookId=&from=&to=` | user | ledger |
| | `POST /inventory/adjustments` | Owner, StoreManager, InventoryManager | `{ reason, lines:[{bookId, quantityDelta, condition}] }` damaged = negative onHand, +damaged |
| | `GET /inventory/low-stock` | user | onHand ≤ reorderLevel |
| | `GET /inventory/valuation` | Owner, StoreManager | Σ onHand × averageCost |
| Sales | `POST /sales/invoices` | Owner, StoreManager, BillingEmployee | header `Idempotency-Key` required. Body below |
| | `GET /sales/invoices?from=&to=&customerId=&status=` ; `GET /sales/invoices/{id}` ; `GET /sales/invoices/{id}/print` (HTML) | user | |
| | `POST /sales/invoices/{id}/cancel` | Owner, StoreManager | only if no payments allocated or payments reversed; restores stock via reversal movements |
| | `POST /sales/returns`, `GET /sales/returns`, `GET /sales/returns/{id}` | Owner, StoreManager, BillingEmployee | partial returns; resalable → stock back; damaged → damaged bucket; refund cash or credit note |
| Payments | `POST /payments` `{ customerId?, method, amount, paidAt, reference, allocations:[{invoiceId, amount}] }` | Owner, StoreManager, BillingEmployee | allocations ≤ invoice due |
| | `GET /payments?from=&to=&customerId=` , `GET /payments/{id}` | user | |
| | `GET /credit-notes?customerId=` | user | |
| Reports | `GET /reports/dashboard?from=&to=` | Owner, StoreManager | KPIs: todaySales, monthSales, grossSales, discounts, netSales, grossProfit, purchases, inventoryValuation, lowStockCount, customerOutstanding, supplierOutstanding, invoiceCount |
| | `GET /reports/sales/daily?from=&to=` , `/reports/sales/monthly?year=` , `/reports/sales/by-book` , `/reports/sales/by-category` , `/reports/purchases/by-supplier` , `/reports/inventory/movement` , `/reports/inventory/aging` , `/reports/returns` , `/reports/profitability` | Owner, StoreManager | add `format=xlsx` for Excel |
| Audit | `GET /audit?entity=&from=&to=` | Owner | |

### `POST /sales/invoices` body

```json
{
  "customerId": null,
  "customerName": "Walk-in",
  "customerPhone": null,
  "invoiceDate": "2026-11-09",
  "discountType": "None | Percentage | Fixed",
  "discountValue": 0,
  "lines": [ { "bookId": "…", "quantity": 2, "unitPrice": 250.00, "discountPercent": 0 } ],
  "payment": { "method": "Cash", "amount": 500.00, "reference": null }   // optional; omit for credit sale
}
```
Response `201` with the full invoice (totals, GST split, number, amounts). `409` `insufficient-stock` lists the offending `bookId`s. Replays with the same `Idempotency-Key` return the original response with header `Idempotent-Replay: true`.

### Pricing rules (order of operations)

1. Line taxable = qty × unitPrice − lineDiscount (percent of gross, rounded half-away-from-zero to 2 dp).
2. Invoice-level discount (Percentage or Fixed) is distributed proportionally across lines by taxable amount; remainder cent goes to the largest line.
3. GST per line on taxable: intra-state → CGST = SGST = rate/2; inter-state (customer state code ≠ tenant state code) → IGST = rate. Books (HSN 4901) are 0 % by default; rate comes from the book record.
4. Grand total = Σ line totals, rounded to nearest rupee; `RoundOff` stores the difference.
