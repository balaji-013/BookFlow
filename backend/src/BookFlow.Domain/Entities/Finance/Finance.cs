using BookFlow.Domain.Common;
using BookFlow.Domain.Enums;

namespace BookFlow.Domain.Entities.Finance;

public class Payment : TenantEntity
{
    public string PaymentNumber { get; set; } = default!;
    public Guid? CustomerId { get; set; }
    public PaymentDirection Direction { get; set; } = PaymentDirection.In;
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public List<PaymentAllocation> Allocations { get; set; } = new();
}

public class PaymentAllocation : Entity
{
    public Guid PaymentId { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public decimal Amount { get; set; }
}

public class CreditNote : TenantEntity
{
    public string CreditNoteNumber { get; set; } = default!;
    public Guid? CustomerId { get; set; }
    public Guid? SalesReturnId { get; set; }
    public decimal Amount { get; set; }
    public decimal Balance { get; set; }
    public DateTime IssuedAt { get; set; }
    public CreditNoteStatus Status { get; set; } = CreditNoteStatus.Open;
}

public class SupplierPayment : TenantEntity
{
    public Guid SupplierId { get; set; }
    public Guid? GoodsReceiptId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTime PaidAt { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}
