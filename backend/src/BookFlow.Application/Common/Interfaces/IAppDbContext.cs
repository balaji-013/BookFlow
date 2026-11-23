using BookFlow.Domain.Entities.Audit;
using BookFlow.Domain.Entities.Catalog;
using BookFlow.Domain.Entities.Finance;
using BookFlow.Domain.Entities.Identity;
using BookFlow.Domain.Entities.Inventory;
using BookFlow.Domain.Entities.Parties;
using BookFlow.Domain.Entities.Purchasing;
using BookFlow.Domain.Entities.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BookFlow.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Store> Stores { get; }
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Author> Authors { get; }
    DbSet<Publisher> Publishers { get; }
    DbSet<Category> Categories { get; }
    DbSet<Book> Books { get; }

    DbSet<Customer> Customers { get; }
    DbSet<Supplier> Suppliers { get; }

    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderItem> PurchaseOrderItems { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<GoodsReceiptItem> GoodsReceiptItems { get; }
    DbSet<PurchaseReturn> PurchaseReturns { get; }
    DbSet<PurchaseReturnItem> PurchaseReturnItems { get; }

    DbSet<StockBalance> StockBalances { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<StockAdjustment> StockAdjustments { get; }
    DbSet<StockAdjustmentLine> StockAdjustmentLines { get; }

    DbSet<InvoiceSequence> InvoiceSequences { get; }
    DbSet<SalesInvoice> SalesInvoices { get; }
    DbSet<SalesInvoiceItem> SalesInvoiceItems { get; }
    DbSet<SalesReturn> SalesReturns { get; }
    DbSet<SalesReturnItem> SalesReturnItems { get; }
    DbSet<IdempotencyKey> IdempotencyKeys { get; }

    DbSet<Payment> Payments { get; }
    DbSet<PaymentAllocation> PaymentAllocations { get; }
    DbSet<CreditNote> CreditNotes { get; }
    DbSet<SupplierPayment> SupplierPayments { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
