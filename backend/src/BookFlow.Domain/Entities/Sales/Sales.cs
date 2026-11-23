using BookFlow.Domain.Common;
using BookFlow.Domain.Enums;

namespace BookFlow.Domain.Entities.Sales;

public class InvoiceSequence : ITenantEntity
{
    public Guid TenantId { get; set; }
    public string FinancialYear { get; set; } = default!;
    public string Kind { get; set; } = "INV"; // INV, PO, GRN, SR, PAY, CN, ADJ, PR
    public long NextNumber { get; set; } = 1;
}

public class SalesInvoice : TenantEntity, IHasRowVersion
{
    public Guid StoreId { get; set; }
    public string InvoiceNumber { get; set; } = default!;
    public DateOnly InvoiceDate { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = "Walk-in";
    public string? CustomerPhone { get; set; }
    public string? CustomerGstin { get; set; }
    public bool IsInterState { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxableTotal { get; set; }
    public decimal CgstTotal { get; set; }
    public decimal SgstTotal { get; set; }
    public decimal IgstTotal { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public string? Notes { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public List<SalesInvoiceItem> Items { get; set; } = new();
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public void RecomputePaymentStatus()
    {
        AmountDue = Money.Round2(GrandTotal - AmountPaid);
        PaymentStatus = AmountDue <= 0 ? PaymentStatus.Paid : AmountPaid > 0 ? PaymentStatus.Partial : PaymentStatus.Unpaid;
    }
}

public class SalesInvoiceItem : Entity
{
    public Guid SalesInvoiceId { get; set; }
    public Guid BookId { get; set; }
    public string Title { get; set; } = default!;
    public string? Isbn { get; set; }
    public string HsnCode { get; set; } = "4901";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }
    public int QuantityReturned { get; set; }
}

public class SalesReturn : TenantEntity
{
    public string ReturnNumber { get; set; } = default!;
    public Guid StoreId { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public Guid? CustomerId { get; set; }
    public DateOnly ReturnDate { get; set; }
    public string? Reason { get; set; }
    public RefundMode RefundMode { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public Guid? CreditNoteId { get; set; }
    public Guid? RefundPaymentId { get; set; }
    public List<SalesReturnItem> Items { get; set; } = new();
}

public class SalesReturnItem : Entity
{
    public Guid SalesReturnId { get; set; }
    public Guid SalesInvoiceItemId { get; set; }
    public Guid BookId { get; set; }
    public int Quantity { get; set; }
    public ReturnCondition Condition { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class IdempotencyKey : ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Key { get; set; } = default!;
    public int StatusCode { get; set; }
    public string ResponseJson { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}
