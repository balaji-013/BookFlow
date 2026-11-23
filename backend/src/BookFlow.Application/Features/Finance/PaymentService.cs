using BookFlow.Application.Common.Extensions;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Common.Models;
using BookFlow.Domain.Common;
using BookFlow.Domain.Entities.Finance;
using BookFlow.Domain.Enums;
using BookFlow.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Application.Features.Finance;

public sealed record AllocationRequest(Guid InvoiceId, decimal Amount);
public sealed record CreatePaymentRequest(Guid? CustomerId, PaymentMethod Method, decimal Amount, DateTime? PaidAt, string? Reference, string? Notes,
    List<AllocationRequest> Allocations, Guid? CreditNoteId);
public sealed record AllocationDto(Guid InvoiceId, string InvoiceNumber, decimal Amount);
public sealed record PaymentDto(Guid Id, string PaymentNumber, Guid? CustomerId, string? CustomerName, PaymentDirection Direction, PaymentMethod Method, decimal Amount,
    DateTime PaidAt, string? Reference, string? Notes, decimal Allocated, decimal Unallocated, List<AllocationDto> Allocations);
public sealed record CreditNoteDto(Guid Id, string CreditNoteNumber, Guid? CustomerId, string? CustomerName, decimal Amount, decimal Balance, DateTime IssuedAt, CreditNoteStatus Status);
public sealed record OutstandingInvoiceDto(Guid Id, string InvoiceNumber, DateOnly InvoiceDate, decimal GrandTotal, decimal AmountPaid, decimal AmountDue);

public sealed class CreatePaymentValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method).IsInEnum().NotEqual(PaymentMethod.Credit);
        RuleFor(x => x.CreditNoteId).NotNull().When(x => x.Method == PaymentMethod.CreditNote).WithMessage("Select the credit note to apply.");
        RuleForEach(x => x.Allocations).ChildRules(a =>
        {
            a.RuleFor(x => x.InvoiceId).NotEmpty();
            a.RuleFor(x => x.Amount).GreaterThan(0);
        });
        RuleFor(x => x).Must(x => x.Allocations.Sum(a => a.Amount) <= x.Amount).WithMessage("Allocations cannot exceed the payment amount.");
    }
}

public interface IPaymentService
{
    Task<PaymentDto> CreateAsync(CreatePaymentRequest request, string? idempotencyKey, CancellationToken ct);
    Task<PagedResult<PaymentDto>> ListAsync(DateOnly? from, DateOnly? to, Guid? customerId, PaymentDirection? direction, int? page, int? pageSize, CancellationToken ct);
    Task<PaymentDto> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<OutstandingInvoiceDto>> OutstandingAsync(Guid customerId, CancellationToken ct);
    Task<PagedResult<CreditNoteDto>> CreditNotesAsync(Guid? customerId, bool openOnly, int? page, int? pageSize, CancellationToken ct);
}

public sealed class PaymentService : IPaymentService
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IClock _clock;
    private readonly IValidator<CreatePaymentRequest> _validator;

    public PaymentService(IAppDbContext db, ITenantContext tenant, IDocumentNumberGenerator numbers, IClock clock, IValidator<CreatePaymentRequest> validator)
    {
        _db = db; _tenant = tenant; _numbers = numbers; _clock = clock; _validator = validator;
    }

    public async Task<PaymentDto> CreateAsync(CreatePaymentRequest r, string? idempotencyKey, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAppAsync(r, ct);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var key = "PAY:" + idempotencyKey;
            var existing = await _db.IdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Key == key, ct);
            if (existing is not null) return await GetAsync(Guid.Parse(existing.ResponseJson), ct);
        }

        var invoiceIds = r.Allocations.Select(a => a.InvoiceId).Distinct().ToList();
        var invoices = await _db.SalesInvoices.Where(i => invoiceIds.Contains(i.Id)).ToListAsync(ct);
        foreach (var id in invoiceIds) if (invoices.All(i => i.Id != id)) throw new NotFoundException("Invoice", id);
        if (r.CustomerId.HasValue && invoices.Any(i => i.CustomerId != r.CustomerId))
            throw new BusinessRuleException("customer-mismatch", "All allocated invoices must belong to the selected customer.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var payment = new Payment
        {
            TenantId = _tenant.TenantId, PaymentNumber = await _numbers.NextAsync("PAY", "PAY", _clock.TodayIst, ct), CustomerId = r.CustomerId,
            Direction = PaymentDirection.In, Method = r.Method, Amount = r.Amount, PaidAt = r.PaidAt ?? _clock.UtcNow, Reference = r.Reference, Notes = r.Notes
        };

        if (r.Method == PaymentMethod.CreditNote)
        {
            var cn = await _db.CreditNotes.FirstOrDefaultAsync(c => c.Id == r.CreditNoteId, ct) ?? throw new NotFoundException("CreditNote", r.CreditNoteId!);
            if (cn.Status is CreditNoteStatus.Used or CreditNoteStatus.Cancelled || cn.Balance < r.Amount)
                throw new BusinessRuleException("credit-note-balance", $"Credit note balance ₹{cn.Balance:N2} is insufficient.");
            if (r.CustomerId.HasValue && cn.CustomerId != r.CustomerId) throw new BusinessRuleException("customer-mismatch", "Credit note belongs to another customer.");
            cn.Balance = Money.Round2(cn.Balance - r.Amount);
            cn.Status = cn.Balance <= 0 ? CreditNoteStatus.Used : CreditNoteStatus.PartiallyUsed;
            payment.Reference ??= cn.CreditNoteNumber;
        }

        foreach (var a in r.Allocations.GroupBy(a => a.InvoiceId))
        {
            var inv = invoices.First(i => i.Id == a.Key);
            var amount = a.Sum(x => x.Amount);
            if (inv.Status == InvoiceStatus.Cancelled) throw new BusinessRuleException("cancelled", $"Invoice {inv.InvoiceNumber} is cancelled.");
            if (amount > inv.AmountDue + 0.005m)
                throw new BusinessRuleException("over-allocation", $"Allocation ₹{amount:N2} exceeds amount due ₹{inv.AmountDue:N2} on {inv.InvoiceNumber}.");
            payment.Allocations.Add(new PaymentAllocation { SalesInvoiceId = inv.Id, Amount = amount });
            inv.AmountPaid = Money.Round2(inv.AmountPaid + amount);
            inv.RecomputePaymentStatus();
        }

        _db.Payments.Add(payment);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            _db.IdempotencyKeys.Add(new Domain.Entities.Sales.IdempotencyKey { TenantId = _tenant.TenantId, Key = "PAY:" + idempotencyKey, StatusCode = 201, ResponseJson = payment.Id.ToString(), CreatedAt = _clock.UtcNow });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(payment.Id, ct);
    }

    public async Task<PagedResult<PaymentDto>> ListAsync(DateOnly? from, DateOnly? to, Guid? customerId, PaymentDirection? direction, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.Payments.AsNoTracking().AsQueryable();
        if (from.HasValue) { var f = from.Value.ToDateTime(TimeOnly.MinValue); q = q.Where(p => p.PaidAt >= f); }
        if (to.HasValue) { var t = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue); q = q.Where(p => p.PaidAt < t); }
        if (customerId.HasValue) q = q.Where(p => p.CustomerId == customerId);
        if (direction.HasValue) q = q.Where(p => p.Direction == direction);
        var (pg, s) = PagingExtensions.Normalize(page, pageSize);
        var total = await q.CountAsync(ct);
        var ids = await q.OrderByDescending(p => p.PaidAt).Skip((pg - 1) * s).Take(s).Select(p => p.Id).ToListAsync(ct);
        var items = new List<PaymentDto>();
        foreach (var id in ids) items.Add(await GetAsync(id, ct));
        return new PagedResult<PaymentDto> { Items = items, Page = pg, PageSize = s, TotalCount = total };
    }

    public async Task<PaymentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.Payments.AsNoTracking().Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Payment", id);
        var customerName = p.CustomerId.HasValue ? await _db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;
        var invIds = p.Allocations.Select(a => a.SalesInvoiceId).ToList();
        var numbers = await _db.SalesInvoices.Where(i => invIds.Contains(i.Id)).Select(i => new { i.Id, i.InvoiceNumber }).ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber, ct);
        var allocated = p.Allocations.Sum(a => a.Amount);
        return new PaymentDto(p.Id, p.PaymentNumber, p.CustomerId, customerName, p.Direction, p.Method, p.Amount, p.PaidAt, p.Reference, p.Notes, allocated,
            p.Direction == PaymentDirection.In ? Money.Round2(p.Amount - allocated) : 0m,
            p.Allocations.Select(a => new AllocationDto(a.SalesInvoiceId, numbers.GetValueOrDefault(a.SalesInvoiceId, ""), a.Amount)).ToList());
    }

    public async Task<IReadOnlyList<OutstandingInvoiceDto>> OutstandingAsync(Guid customerId, CancellationToken ct) =>
        await _db.SalesInvoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId && i.Status != InvoiceStatus.Cancelled && i.AmountDue > 0)
            .OrderBy(i => i.InvoiceDate)
            .Select(i => new OutstandingInvoiceDto(i.Id, i.InvoiceNumber, i.InvoiceDate, i.GrandTotal, i.AmountPaid, i.AmountDue)).ToListAsync(ct);

    public async Task<PagedResult<CreditNoteDto>> CreditNotesAsync(Guid? customerId, bool openOnly, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.CreditNotes.AsNoTracking().AsQueryable();
        if (customerId.HasValue) q = q.Where(c => c.CustomerId == customerId);
        if (openOnly) q = q.Where(c => c.Status == CreditNoteStatus.Open || c.Status == CreditNoteStatus.PartiallyUsed);
        var projected = from c in q
                        join cu in _db.Customers on c.CustomerId equals (Guid?)cu.Id into cus
                        from cu in cus.DefaultIfEmpty()
                        orderby c.IssuedAt descending
                        select new CreditNoteDto(c.Id, c.CreditNoteNumber, c.CustomerId, cu != null ? cu.Name : null, c.Amount, c.Balance, c.IssuedAt, c.Status);
        return await projected.ToPagedResultAsync(page, pageSize, ct);
    }
}
