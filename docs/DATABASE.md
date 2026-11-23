# BookFlow — DATABASE.md

SQL Server 2022, single database `BookFlow`, EF Core 8 migrations in `BookFlow.Infrastructure/Persistence/Migrations`. All money columns are `decimal(18,2)`; quantities `int`; identifiers `uniqueidentifier` (sequential GUIDs generated client-side with `Guid.CreateVersion7()`-style ordering via `NEWSEQUENTIALID`-friendly Guid v7 in code).

Every tenant-owned table has `TenantId uniqueidentifier NOT NULL` with an index whose leading column is `TenantId`. Uniqueness constraints are tenant-scoped.

## Schema

### Identity
```
Tenants        (Id PK, BusinessName, LegalName, Gstin null, AddressLine1, City, State, Pincode, Phone, Email, StateCode, Status, SubscriptionPlan, CreatedAt)
Stores         (Id PK, TenantId, Name, Code, IsDefault, Address...)           UX(TenantId, Code)
Users          (Id PK, TenantId, Email, FullName, PasswordHash, Role, IsActive, StoreId null, LastLoginAt, RowVersion)   UX(TenantId, Email); IX(Email)
RefreshTokens  (Id PK, UserId, TokenHash, ExpiresAt, CreatedAt, RevokedAt null, ReplacedByTokenHash null)   IX(TokenHash)
```

### Catalog
```
Authors        (Id, TenantId, Name)                          UX(TenantId, Name)
Publishers     (Id, TenantId, Name)                          UX(TenantId, Name)
Categories     (Id, TenantId, Name, ParentId null)           UX(TenantId, Name)
Books          (Id, TenantId, Isbn null, Barcode null, Title, Subtitle null, AuthorId null, PublisherId null, CategoryId null,
                Edition null, Language, Pages null, PurchasePrice, SellingPrice, Mrp, HsnCode, GstRate, ReorderLevel, IsActive, RowVersion)
               UX(TenantId, Isbn) WHERE Isbn IS NOT NULL;  UX(TenantId, Barcode) WHERE Barcode IS NOT NULL;  IX(TenantId, Title)
```
A "Title" can have several editions: each edition is its own `Books` row (own ISBN/price/stock), sharing Title/Author (ADR-0002).

### Parties
```
Customers (Id, TenantId, Name, Phone null, Email null, Gstin null, Type [WalkIn|Retail|Institutional], CreditLimit, Address..., IsActive, RowVersion)  IX(TenantId, Phone), IX(TenantId, Name)
Suppliers (Id, TenantId, Name, ContactPerson, Phone, Email, Gstin, Address..., IsActive, RowVersion)   UX(TenantId, Name)
```

### Purchasing
```
PurchaseOrders      (Id, TenantId, PoNumber, SupplierId, StoreId, OrderDate, ExpectedDate null, Status [Draft|Submitted|PartiallyReceived|Received|Cancelled], Notes, SubTotal, TaxTotal, Total, RowVersion)  UX(TenantId, PoNumber)
PurchaseOrderItems  (Id, PurchaseOrderId, BookId, QuantityOrdered, QuantityReceived, UnitCost, GstRate, LineTotal)
GoodsReceipts       (Id, TenantId, GrnNumber, PurchaseOrderId, SupplierId, StoreId, ReceivedDate, SupplierInvoiceNumber null, SupplierInvoiceDate null, SubTotal, TaxTotal, Total, Notes)  UX(TenantId, GrnNumber)
GoodsReceiptItems   (Id, GoodsReceiptId, BookId, Quantity, UnitCost, GstRate, LineTotal)
PurchaseReturns     (Id, TenantId, PrNumber, SupplierId, StoreId, ReturnDate, Reason, Total)
PurchaseReturnItems (Id, PurchaseReturnId, BookId, Quantity, UnitCost, LineTotal)
```

### Inventory
```
StockBalances      (Id, TenantId, StoreId, BookId, QuantityOnHand, QuantityDamaged, AverageCost, UpdatedAt)   UX(TenantId, StoreId, BookId)
InventoryMovements (Id, TenantId, StoreId, BookId, MovementType [OpeningStock|Purchase|Sale|SalesReturn|PurchaseReturn|Damaged|Adjustment|TransferIn|TransferOut],
                    Quantity (signed), UnitCost, ReferenceType, ReferenceId, ReferenceNumber, OccurredAt, CreatedBy, Notes)   IX(TenantId, BookId, OccurredAt), IX(TenantId, OccurredAt)
StockAdjustments   (Id, TenantId, StoreId, AdjustmentNumber, Reason, AdjustedAt, Notes)
```
`InventoryMovements` is append-only: no UPDATE/DELETE from application code (enforced by convention + a DB trigger created in migration `AppendOnlyMovements`). Reversals are new rows.

Closing stock = Σ Quantity over movements; `StockBalances.QuantityOnHand` is the maintained projection, reconciled by `ReconcileStockReport`.

### Sales
```
InvoiceSequences  (TenantId, FinancialYear '2026-27', NextNumber)   PK(TenantId, FinancialYear)
SalesInvoices     (Id, TenantId, StoreId, InvoiceNumber, InvoiceDate, CustomerId null, CustomerName, CustomerPhone, CustomerGstin,
                   Status [Issued|Cancelled|PartiallyReturned|Returned], SubTotal, DiscountTotal, TaxableTotal, CgstTotal, SgstTotal, IgstTotal, RoundOff, GrandTotal,
                   AmountPaid, AmountDue, PaymentStatus [Unpaid|Partial|Paid], Notes, CancelledAt null, CancelReason null, CreatedBy, CreatedAt, RowVersion)
                  UX(TenantId, InvoiceNumber), IX(TenantId, InvoiceDate), IX(TenantId, CustomerId)
SalesInvoiceItems (Id, SalesInvoiceId, BookId, Title, Isbn, HsnCode, Quantity, UnitPrice, DiscountPercent, DiscountAmount, TaxableAmount, GstRate, CgstAmount, SgstAmount, IgstAmount, LineTotal, QuantityReturned)
SalesReturns      (Id, TenantId, StoreId, ReturnNumber, SalesInvoiceId, ReturnDate, Reason, RefundMode [Cash|CreditNote|Adjust], SubTotal, TaxTotal, Total, CreditNoteId null)  UX(TenantId, ReturnNumber)
SalesReturnItems  (Id, SalesReturnId, SalesInvoiceItemId, BookId, Quantity, Condition [Resalable|Damaged], UnitPrice, TaxableAmount, TaxAmount, LineTotal)
IdempotencyKeys   (TenantId, Key, StatusCode, ResponseJson, CreatedAt)   PK(TenantId, Key); purge > 7 days
```

### Finance
```
Payments           (Id, TenantId, PaymentNumber, CustomerId null, Direction [In|Out], Method [Cash|Upi|Card|BankTransfer|Credit], Amount, PaidAt, Reference null, Notes, CreatedBy)  UX(TenantId, PaymentNumber)
PaymentAllocations (Id, PaymentId, SalesInvoiceId, Amount)
CreditNotes        (Id, TenantId, CreditNoteNumber, CustomerId, SalesReturnId, Amount, Balance, IssuedAt, Status)  UX(TenantId, CreditNoteNumber)
SupplierPayments   (Id, TenantId, SupplierId, Amount, Method, PaidAt, Reference, Notes, GoodsReceiptId null)
```

### Audit
```
AuditLogs (Id bigint identity, TenantId, UserId, EntityName, EntityId, Action [Insert|Update|Delete], Changes nvarchar(max) JSON, OccurredAt)   IX(TenantId, OccurredAt), IX(TenantId, EntityName, EntityId)
```

## Critical transaction: create invoice

```
BEGIN TRAN (ReadCommitted)
  1. idempotency: SELECT IdempotencyKeys WHERE TenantId=@t AND Key=@k  → replay if found
  2. SELECT NextNumber FROM InvoiceSequences WITH (UPDLOCK, HOLDLOCK) WHERE TenantId=@t AND FinancialYear=@fy  (insert row if missing)
  3. for each line: UPDATE StockBalances SET QuantityOnHand -= @q WHERE TenantId=@t AND StoreId=@s AND BookId=@b AND QuantityOnHand >= @q;  IF @@ROWCOUNT=0 → rollback, 409
  4. INSERT SalesInvoice + Items
  5. INSERT InventoryMovements (Sale, -q) per line
  6. optional: INSERT Payment + PaymentAllocation; update AmountPaid/AmountDue
  7. INSERT IdempotencyKeys (response)
  8. AuditLogs via interceptor
COMMIT
```

## Indexing summary

Hot queries and their covering indexes:

* Book search by title/ISBN/barcode within tenant → `IX_Books_TenantId_Title`, filtered unique indexes on Isbn/Barcode.
* POS stock lookup → `UX_StockBalances_TenantId_StoreId_BookId`.
* Daily sales report → `IX_SalesInvoices_TenantId_InvoiceDate` (+ Status, GrandTotal included).
* Customer outstanding → `IX_SalesInvoices_TenantId_CustomerId` filtered on `AmountDue > 0`.
* Movement history → `IX_InventoryMovements_TenantId_BookId_OccurredAt`.

## Retention

Business documents are never deleted (soft `IsActive`/`Status` only). `IdempotencyKeys` purged after 7 days, `RefreshTokens` after expiry + 30 days, `AuditLogs` retained 7 years (GST requirement: 6 years from annual return).

## Backups

Full nightly + transaction-log every 15 minutes via `deploy/scripts/backup.sh` (SQL Server `BACKUP DATABASE`/`BACKUP LOG` to the `bookflow_backups` volume, then rsync/S3 off-host). Restore drill documented in DEPLOYMENT.md.
