using BookFlow.Domain.Enums;
using FluentValidation;

namespace BookFlow.Application.Features.Sales;

public sealed record InvoiceLineRequest(Guid BookId, int Quantity, decimal? UnitPrice, decimal DiscountPercent);
public sealed record InvoicePaymentRequest(PaymentMethod Method, decimal Amount, string? Reference);
public sealed record CreateInvoiceRequest(
    Guid? CustomerId, string? CustomerName, string? CustomerPhone, DateOnly? InvoiceDate,
    DiscountType DiscountType, decimal DiscountValue, string? Notes, List<InvoiceLineRequest> Lines, InvoicePaymentRequest? Payment);

public sealed record InvoiceItemDto(Guid Id, Guid BookId, string Title, string? Isbn, string HsnCode, int Quantity, decimal UnitPrice, decimal DiscountPercent,
    decimal DiscountAmount, decimal TaxableAmount, decimal GstRate, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount, decimal LineTotal, int QuantityReturned);

public sealed record InvoiceDto(Guid Id, string InvoiceNumber, DateOnly InvoiceDate, Guid? CustomerId, string CustomerName, string? CustomerPhone, string? CustomerGstin,
    bool IsInterState, InvoiceStatus Status, DiscountType DiscountType, decimal DiscountValue, decimal SubTotal, decimal DiscountTotal, decimal TaxableTotal,
    decimal CgstTotal, decimal SgstTotal, decimal IgstTotal, decimal RoundOff, decimal GrandTotal, decimal AmountPaid, decimal AmountDue, PaymentStatus PaymentStatus,
    string? Notes, DateTime CreatedAt, string? CreatedByName, DateTime? CancelledAt, string? CancelReason, List<InvoiceItemDto> Items, List<InvoicePaymentDto> Payments);

public sealed record InvoicePaymentDto(Guid PaymentId, string PaymentNumber, PaymentMethod Method, decimal Amount, DateTime PaidAt, string? Reference);

public sealed record InvoiceListItemDto(Guid Id, string InvoiceNumber, DateOnly InvoiceDate, string CustomerName, InvoiceStatus Status, decimal GrandTotal,
    decimal AmountPaid, decimal AmountDue, PaymentStatus PaymentStatus, int ItemCount, DateTime CreatedAt);

public sealed record CancelInvoiceRequest(string Reason);

public sealed record ReturnLineRequest(Guid SalesInvoiceItemId, int Quantity, ReturnCondition Condition);
public sealed record CreateSalesReturnRequest(Guid SalesInvoiceId, DateOnly? ReturnDate, string? Reason, RefundMode RefundMode, List<ReturnLineRequest> Lines);
public sealed record SalesReturnItemDto(Guid Id, Guid BookId, string Title, int Quantity, ReturnCondition Condition, decimal UnitPrice, decimal TaxableAmount, decimal TaxAmount, decimal LineTotal);
public sealed record SalesReturnDto(Guid Id, string ReturnNumber, Guid SalesInvoiceId, string InvoiceNumber, string CustomerName, DateOnly ReturnDate, string? Reason,
    RefundMode RefundMode, decimal SubTotal, decimal TaxTotal, decimal Total, Guid? CreditNoteId, string? CreditNoteNumber, DateTime CreatedAt, List<SalesReturnItemDto> Items);

public sealed class CreateInvoiceValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one book.");
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(200);
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.BookId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(10000);
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).When(x => x.UnitPrice.HasValue);
            l.RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
        });
        RuleFor(x => x.DiscountType).IsInEnum();
        RuleFor(x => x.DiscountValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DiscountValue).LessThanOrEqualTo(100).When(x => x.DiscountType == DiscountType.Percentage);
        RuleFor(x => x.CustomerName).MaximumLength(150);
        RuleFor(x => x.Payment!.Amount).GreaterThan(0).When(x => x.Payment is not null);
        RuleFor(x => x.Payment!.Method).IsInEnum().NotEqual(PaymentMethod.Credit).When(x => x.Payment is not null);
    }
}

public sealed class CancelInvoiceValidator : AbstractValidator<CancelInvoiceRequest>
{
    public CancelInvoiceValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
}

public sealed class CreateSalesReturnValidator : AbstractValidator<CreateSalesReturnRequest>
{
    public CreateSalesReturnValidator()
    {
        RuleFor(x => x.SalesInvoiceId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.RefundMode).IsInEnum();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.SalesInvoiceItemId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.Condition).IsInEnum();
        });
    }
}
