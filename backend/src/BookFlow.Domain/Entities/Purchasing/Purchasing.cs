using BookFlow.Domain.Common;
using BookFlow.Domain.Enums;

namespace BookFlow.Domain.Entities.Purchasing;

public class PurchaseOrder : TenantEntity, IHasRowVersion
{
    public string PoNumber { get; set; } = default!;
    public Guid SupplierId { get; set; }
    public Guid StoreId { get; set; }
    public DateOnly OrderDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public string? Notes { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public List<PurchaseOrderItem> Items { get; set; } = new();
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class PurchaseOrderItem : Entity
{
    public Guid PurchaseOrderId { get; set; }
    public Guid BookId { get; set; }
    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
    public decimal GstRate { get; set; }
    public decimal LineTotal { get; set; }
}

public class GoodsReceipt : TenantEntity
{
    public string GrnNumber { get; set; } = default!;
    public Guid? PurchaseOrderId { get; set; }
    public Guid SupplierId { get; set; }
    public Guid StoreId { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public DateOnly? SupplierInvoiceDate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public string? Notes { get; set; }
    public List<GoodsReceiptItem> Items { get; set; } = new();
}

public class GoodsReceiptItem : Entity
{
    public Guid GoodsReceiptId { get; set; }
    public Guid BookId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal GstRate { get; set; }
    public decimal LineTotal { get; set; }
}

public class PurchaseReturn : TenantEntity
{
    public string PrNumber { get; set; } = default!;
    public Guid SupplierId { get; set; }
    public Guid StoreId { get; set; }
    public Guid? GoodsReceiptId { get; set; }
    public DateOnly ReturnDate { get; set; }
    public string? Reason { get; set; }
    public decimal Total { get; set; }
    public List<PurchaseReturnItem> Items { get; set; } = new();
}

public class PurchaseReturnItem : Entity
{
    public Guid PurchaseReturnId { get; set; }
    public Guid BookId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}
