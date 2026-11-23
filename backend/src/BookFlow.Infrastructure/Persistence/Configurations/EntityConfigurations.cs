using BookFlow.Domain.Entities.Audit;
using BookFlow.Domain.Entities.Catalog;
using BookFlow.Domain.Entities.Finance;
using BookFlow.Domain.Entities.Identity;
using BookFlow.Domain.Entities.Inventory;
using BookFlow.Domain.Entities.Parties;
using BookFlow.Domain.Entities.Purchasing;
using BookFlow.Domain.Entities.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookFlow.Infrastructure.Persistence.Configurations;

internal static class Max
{
    public const string Text = "nvarchar(max)";
}

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenants");
        b.Property(x => x.BusinessName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Email).HasMaxLength(200).IsRequired();
        b.Property(x => x.Gstin).HasMaxLength(15);
        b.Property(x => x.StateCode).HasMaxLength(2).IsRequired();
        b.Property(x => x.InvoicePrefix).HasMaxLength(6).IsRequired();
        b.Property(x => x.InvoiceFooter).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.Email);
    }
}

public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> b)
    {
        b.ToTable("Stores");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.Property(x => x.Email).HasMaxLength(200).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(120).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.Email).IsUnique();           // global: login without tenant selector
        b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.Ignore(x => x.IsActive);
    }
}

public sealed class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> b)
    {
        b.ToTable("Authors");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public sealed class PublisherConfiguration : IEntityTypeConfiguration<Publisher>
{
    public void Configure(EntityTypeBuilder<Publisher> b)
    {
        b.ToTable("Publishers");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("Categories");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> b)
    {
        b.ToTable("Books");
        b.Property(x => x.Isbn).HasMaxLength(17);
        b.Property(x => x.Barcode).HasMaxLength(64);
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Subtitle).HasMaxLength(300);
        b.Property(x => x.Edition).HasMaxLength(100);
        b.Property(x => x.Language).HasMaxLength(40).IsRequired();
        b.Property(x => x.HsnCode).HasMaxLength(8).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.Isbn }).IsUnique().HasFilter("[Isbn] IS NOT NULL");
        b.HasIndex(x => new { x.TenantId, x.Barcode }).IsUnique().HasFilter("[Barcode] IS NOT NULL");
        b.HasIndex(x => new { x.TenantId, x.Title });
        b.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Publisher).WithMany().HasForeignKey(x => x.PublisherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Gstin).HasMaxLength(15);
        b.Property(x => x.StateCode).HasMaxLength(2);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.Phone });
        b.HasIndex(x => new { x.TenantId, x.Name });
    }
}

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("Suppliers");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.ContactPerson).HasMaxLength(120);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Gstin).HasMaxLength(15);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("PurchaseOrders");
        b.Property(x => x.PoNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.PoNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.SupplierId });
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> b)
    {
        b.ToTable("PurchaseOrderItems");
        b.HasIndex(x => x.BookId);
    }
}

public sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        b.ToTable("GoodsReceipts");
        b.Property(x => x.GrnNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.SupplierInvoiceNumber).HasMaxLength(60);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.TenantId, x.GrnNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ReceivedDate });
        b.HasIndex(x => new { x.TenantId, x.SupplierId });
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GoodsReceiptItemConfiguration : IEntityTypeConfiguration<GoodsReceiptItem>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptItem> b)
    {
        b.ToTable("GoodsReceiptItems");
        b.HasIndex(x => x.BookId);
    }
}

public sealed class PurchaseReturnConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    public void Configure(EntityTypeBuilder<PurchaseReturn> b)
    {
        b.ToTable("PurchaseReturns");
        b.Property(x => x.PrNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasIndex(x => new { x.TenantId, x.PrNumber }).IsUnique();
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.PurchaseReturnId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PurchaseReturnItemConfiguration : IEntityTypeConfiguration<PurchaseReturnItem>
{
    public void Configure(EntityTypeBuilder<PurchaseReturnItem> b) => b.ToTable("PurchaseReturnItems");
}

public sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.ToTable("StockBalances", t => t.HasCheckConstraint("CK_StockBalances_NonNegative", "[QuantityOnHand] >= 0 AND [QuantityDamaged] >= 0"));
        b.Property(x => x.AverageCost).HasColumnType("decimal(18,4)");
        b.HasIndex(x => new { x.TenantId, x.StoreId, x.BookId }).IsUnique();
    }
}

public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> b)
    {
        b.ToTable("InventoryMovements");
        b.Property(x => x.MovementType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ReferenceType).HasMaxLength(40).IsRequired();
        b.Property(x => x.ReferenceNumber).HasMaxLength(30);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.UnitCost).HasColumnType("decimal(18,4)");
        b.HasIndex(x => new { x.TenantId, x.BookId, x.OccurredAt });
        b.HasIndex(x => new { x.TenantId, x.OccurredAt });
        b.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
    }
}

public sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> b)
    {
        b.ToTable("StockAdjustments");
        b.Property(x => x.AdjustmentNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(200).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.TenantId, x.AdjustmentNumber }).IsUnique();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.StockAdjustmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StockAdjustmentLineConfiguration : IEntityTypeConfiguration<StockAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentLine> b)
    {
        b.ToTable("StockAdjustmentLines");
        b.Property(x => x.Condition).HasConversion<string>().HasMaxLength(20);
    }
}

public sealed class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> b)
    {
        b.ToTable("InvoiceSequences");
        b.HasKey(x => new { x.TenantId, x.FinancialYear, x.Kind });
        b.Property(x => x.FinancialYear).HasMaxLength(7);
        b.Property(x => x.Kind).HasMaxLength(10);
    }
}

public sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> b)
    {
        b.ToTable("SalesInvoices");
        b.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(150).IsRequired();
        b.Property(x => x.CustomerPhone).HasMaxLength(20);
        b.Property(x => x.CustomerGstin).HasMaxLength(15);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.CancelReason).HasMaxLength(300);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.InvoiceNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.InvoiceDate });
        b.HasIndex(x => new { x.TenantId, x.CustomerId }).HasFilter("[AmountDue] > 0");
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.SalesInvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SalesInvoiceItemConfiguration : IEntityTypeConfiguration<SalesInvoiceItem>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceItem> b)
    {
        b.ToTable("SalesInvoiceItems");
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Isbn).HasMaxLength(17);
        b.Property(x => x.HsnCode).HasMaxLength(8).IsRequired();
        b.Property(x => x.UnitCost).HasColumnType("decimal(18,4)");
        b.HasIndex(x => x.BookId);
    }
}

public sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> b)
    {
        b.ToTable("SalesReturns");
        b.Property(x => x.ReturnNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.RefundMode).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.TenantId, x.ReturnNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ReturnDate });
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.SalesReturnId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SalesReturnItemConfiguration : IEntityTypeConfiguration<SalesReturnItem>
{
    public void Configure(EntityTypeBuilder<SalesReturnItem> b)
    {
        b.ToTable("SalesReturnItems");
        b.Property(x => x.Condition).HasConversion<string>().HasMaxLength(20);
    }
}

public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> b)
    {
        b.ToTable("IdempotencyKeys");
        b.HasKey(x => new { x.TenantId, x.Key });
        b.Property(x => x.Key).HasMaxLength(120);
        b.Property(x => x.ResponseJson).HasColumnType(Max.Text).IsRequired();
        b.HasIndex(x => x.CreatedAt);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments");
        b.Property(x => x.PaymentNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(x => new { x.TenantId, x.PaymentNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.PaidAt });
        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        b.HasMany(x => x.Allocations).WithOne().HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> b)
    {
        b.ToTable("PaymentAllocations");
        b.HasIndex(x => x.SalesInvoiceId);
    }
}

public sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> b)
    {
        b.ToTable("CreditNotes");
        b.Property(x => x.CreditNoteNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.TenantId, x.CreditNoteNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CustomerId });
    }
}

public sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> b)
    {
        b.ToTable("SupplierPayments");
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.TenantId, x.SupplierId });
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd();
        b.Property(x => x.EntityName).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Changes).HasColumnType(Max.Text);
        b.HasIndex(x => new { x.TenantId, x.OccurredAt });
        b.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId });
    }
}
