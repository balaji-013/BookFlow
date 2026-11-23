using System.Linq.Expressions;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Domain.Common;
using BookFlow.Domain.Entities.Audit;
using BookFlow.Domain.Entities.Catalog;
using BookFlow.Domain.Entities.Finance;
using BookFlow.Domain.Entities.Identity;
using BookFlow.Domain.Entities.Inventory;
using BookFlow.Domain.Entities.Parties;
using BookFlow.Domain.Entities.Purchasing;
using BookFlow.Domain.Entities.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BookFlow.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    private readonly ITenantContext _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Publisher> Publishers => Set<Publisher>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnItem> PurchaseReturnItems => Set<PurchaseReturnItem>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentLine> StockAdjustmentLines => Set<StockAdjustmentLine>();
    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceItem> SalesInvoiceItems => Set<SalesInvoiceItem>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnItem> SalesReturnItems => Set<SalesReturnItem>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Conventions: decimal(18,2) for all money, tenant query filters, TenantId indexes.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties().Where(p => (p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?))
                         && p.FindAnnotation(RelationalAnnotationNames.ColumnType) is null))
                property.SetColumnType("decimal(18,2)");

            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType) && entityType.BaseType is null)
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var tenantProperty = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
                var currentTenant = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
                var body = Expression.Equal(tenantProperty, currentTenant);
                entityType.SetQueryFilter(Expression.Lambda(body, parameter));
                entityType.AddIndex(entityType.FindProperty(nameof(ITenantEntity.TenantId))!);
            }
        }
    }

    /// <summary>Used by the global query filter; evaluated per query so it follows the current request.</summary>
    public Guid CurrentTenantId => _tenant.IsAuthenticated ? _tenant.TenantId : Guid.Empty;
}
