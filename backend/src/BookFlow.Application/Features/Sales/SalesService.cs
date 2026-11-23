using System.Text.Json;
using System.Text.Json.Serialization;
using BookFlow.Application.Common.Extensions;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Common.Models;
using BookFlow.Domain.Common;
using BookFlow.Domain.Entities.Finance;
using BookFlow.Domain.Entities.Sales;
using BookFlow.Domain.Enums;
using BookFlow.Domain.Exceptions;
using BookFlow.Domain.Pricing;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookFlow.Application.Features.Sales;

public interface ISalesService
{
    Task<(InvoiceDto Invoice, bool Replayed)> CreateInvoiceAsync(CreateInvoiceRequest request, string idempotencyKey, CancellationToken ct);
    Task<PagedResult<InvoiceListItemDto>> ListInvoicesAsync(DateOnly? from, DateOnly? to, Guid? customerId, InvoiceStatus? status, PaymentStatus? paymentStatus, string? search, int? page, int? pageSize, CancellationToken ct);
    Task<InvoiceDto> GetInvoiceAsync(Guid id, CancellationToken ct);
    Task<InvoiceDto> CancelInvoiceAsync(Guid id, CancelInvoiceRequest request, CancellationToken ct);
    Task<SalesReturnDto> CreateReturnAsync(CreateSalesReturnRequest request, CancellationToken ct);
    Task<PagedResult<SalesReturnDto>> ListReturnsAsync(DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct);
    Task<SalesReturnDto> GetReturnAsync(Guid id, CancellationToken ct);
}

public sealed class SalesService : ISalesService
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IInventoryService _inventory;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IClock _clock;
    private readonly ILogger<SalesService> _log;
    private readonly IValidator<CreateInvoiceRequest> _createValidator;
    private readonly IValidator<CancelInvoiceRequest> _cancelValidator;
    private readonly IValidator<CreateSalesReturnRequest> _returnValidator;

    public SalesService(IAppDbContext db, ITenantContext tenant, IInventoryService inventory, IDocumentNumberGenerator numbers, IClock clock, ILogger<SalesService> log,
        IValidator<CreateInvoiceRequest> createValidator, IValidator<CancelInvoiceRequest> cancelValidator, IValidator<CreateSalesReturnRequest> returnValidator)
    {
        _db = db; _tenant = tenant; _inventory = inventory; _numbers = numbers; _clock = clock; _log = log;
        _createValidator = createValidator; _cancelValidator = cancelValidator; _returnValidator = returnValidator;
    }

    // ---------------- create invoice ----------------

    public async Task<(InvoiceDto Invoice, bool Replayed)> CreateInvoiceAsync(CreateInvoiceRequest r, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new AppValidationException("Idempotency-Key", "An Idempotency-Key header (1–100 chars) is required.");
        await _createValidator.ValidateAndThrowAppAsync(r, ct);

        // Fast path: already processed.
        var existing = await _db.IdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Key == idempotencyKey, ct);
        if (existing is not null)
            return (JsonSerializer.Deserialize<InvoiceDto>(existing.ResponseJson, JsonOptions)!, true);

        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == _tenant.TenantId, ct);
        var storeId = _tenant.StoreId ?? (await _db.Stores.FirstOrDefaultAsync(s => s.IsDefault, ct))?.Id ?? throw new BusinessRuleException("no-store", "No default store.");
        var invoiceDate = r.InvoiceDate ?? _clock.TodayIst;
        if (invoiceDate > _clock.TodayIst.AddDays(1)) throw new AppValidationException(nameof(r.InvoiceDate), "Invoice date cannot be in the future.");

        // Customer
        Domain.Entities.Parties.Customer? customer = null;
        if (r.CustomerId.HasValue)
            customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CustomerId && c.IsActive, ct) ?? throw new NotFoundException("Customer", r.CustomerId);
        var customerName = customer?.Name ?? (string.IsNullOrWhiteSpace(r.CustomerName) ? "Walk-in" : r.CustomerName!.Trim());
        var interState = customer?.StateCode is { Length: 2 } sc && sc != tenant.StateCode;

        // Books
        var lines = r.Lines.GroupBy(l => l.BookId).Select(g => new InvoiceLineRequest(g.Key, g.Sum(x => x.Quantity), g.Last().UnitPrice, g.Last().DiscountPercent)).ToList();
        var bookIds = lines.Select(l => l.BookId).ToList();
        var books = await _db.Books.AsNoTracking().Where(b => bookIds.Contains(b.Id) && b.IsActive).ToDictionaryAsync(b => b.Id, ct);
        foreach (var id in bookIds) if (!books.ContainsKey(id)) throw new NotFoundException("Book", id);

        var priced = lines.Select(l => new PricedLine(l.BookId, l.Quantity, l.UnitPrice ?? books[l.BookId].SellingPrice, l.DiscountPercent, books[l.BookId].GstRate)).ToList();
        var totals = InvoiceCalculator.Calculate(priced, DiscountPolicyFactory.Create(r.DiscountType, r.DiscountValue), interState);

        // Payment rules (ADR-0006)
        var paying = r.Payment?.Amount ?? 0m;
        if (paying > totals.GrandTotal) throw new AppValidationException("Payment.Amount", "Payment cannot exceed the invoice total.");
        if (customer is null && paying < totals.GrandTotal)
            throw new BusinessRuleException("walk-in-must-pay", "Walk-in customers must pay the full amount. Select a customer for a credit sale.");
        if (customer is not null && customer.CreditLimit > 0 && paying < totals.GrandTotal)
        {
            var outstanding = await _db.SalesInvoices.Where(i => i.CustomerId == customer.Id && i.Status != InvoiceStatus.Cancelled).SumAsync(i => (decimal?)i.AmountDue, ct) ?? 0m;
            if (outstanding + (totals.GrandTotal - paying) > customer.CreditLimit)
                throw new BusinessRuleException("credit-limit", $"Credit limit of ₹{customer.CreditLimit:N2} would be exceeded (current outstanding ₹{outstanding:N2}).");
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // Re-check idempotency inside the transaction (race between two identical requests).
            var dup = await _db.IdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Key == idempotencyKey, ct);
            if (dup is not null)
            {
                await tx.RollbackAsync(ct);
                return (JsonSerializer.Deserialize<InvoiceDto>(dup.ResponseJson, JsonOptions)!, true);
            }

            var number = await _numbers.NextAsync("INV", tenant.InvoicePrefix, invoiceDate, ct);
            var invoice = new SalesInvoice
            {
                TenantId = _tenant.TenantId, StoreId = storeId, InvoiceNumber = number, InvoiceDate = invoiceDate,
                CustomerId = customer?.Id, CustomerName = customerName, CustomerPhone = customer?.Phone ?? r.CustomerPhone, CustomerGstin = customer?.Gstin,
                IsInterState = interState, DiscountType = r.DiscountType, DiscountValue = r.DiscountValue, Notes = r.Notes,
                SubTotal = totals.SubTotal, DiscountTotal = totals.DiscountTotal, TaxableTotal = totals.TaxableTotal,
                CgstTotal = totals.CgstTotal, SgstTotal = totals.SgstTotal, IgstTotal = totals.IgstTotal, RoundOff = totals.RoundOff, GrandTotal = totals.GrandTotal
            };
            foreach (var cl in totals.Lines)
            {
                var b = books[cl.BookId];
                invoice.Items.Add(new SalesInvoiceItem
                {
                    BookId = b.Id, Title = b.Title, Isbn = b.Isbn, HsnCode = b.HsnCode, Quantity = cl.Quantity, UnitPrice = cl.UnitPrice, UnitCost = b.PurchasePrice,
                    DiscountPercent = cl.DiscountPercent, DiscountAmount = cl.DiscountAmount, TaxableAmount = cl.TaxableAmount, GstRate = cl.GstRate,
                    CgstAmount = cl.CgstAmount, SgstAmount = cl.SgstAmount, IgstAmount = cl.IgstAmount, LineTotal = cl.LineTotal
                });
            }

            Payment? payment = null;
            if (r.Payment is not null && r.Payment.Amount > 0)
            {
                payment = new Payment
                {
                    TenantId = _tenant.TenantId, PaymentNumber = await _numbers.NextAsync("PAY", "PAY", invoiceDate, ct), CustomerId = customer?.Id,
                    Direction = PaymentDirection.In, Method = r.Payment.Method, Amount = r.Payment.Amount, PaidAt = _clock.UtcNow, Reference = r.Payment.Reference
                };
                payment.Allocations.Add(new PaymentAllocation { SalesInvoiceId = invoice.Id, Amount = r.Payment.Amount });
                invoice.AmountPaid = r.Payment.Amount;
                _db.Payments.Add(payment);
            }
            invoice.RecomputePaymentStatus();
            _db.SalesInvoices.Add(invoice);
            await _db.SaveChangesAsync(ct);

            // Atomic stock deduction — throws InsufficientStockException → rollback.
            await _inventory.IssueAsync(storeId, invoice.Items.Select(i => new StockDelta(i.BookId, i.Quantity, i.UnitCost)).ToList(),
                new MovementRef(MovementType.Sale, "SalesInvoice", invoice.Id, invoice.InvoiceNumber), ct);

            var dto = await BuildInvoiceDtoAsync(invoice, ct);
            _db.IdempotencyKeys.Add(new IdempotencyKey
            {
                TenantId = _tenant.TenantId, Key = idempotencyKey, StatusCode = 201, CreatedAt = _clock.UtcNow,
                ResponseJson = JsonSerializer.Serialize(dto, JsonOptions)
            });
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _log.LogInformation("Invoice {InvoiceNumber} created for tenant {TenantId}, total {Total}", invoice.InvoiceNumber, _tenant.TenantId, invoice.GrandTotal);
            return (dto, false);
        });
    }

    // ---------------- queries ----------------

    public async Task<PagedResult<InvoiceListItemDto>> ListInvoicesAsync(DateOnly? from, DateOnly? to, Guid? customerId, InvoiceStatus? status, PaymentStatus? paymentStatus,
        string? search, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.SalesInvoices.AsNoTracking().AsQueryable();
        if (from.HasValue) q = q.Where(i => i.InvoiceDate >= from);
        if (to.HasValue) q = q.Where(i => i.InvoiceDate <= to);
        if (customerId.HasValue) q = q.Where(i => i.CustomerId == customerId);
        if (status.HasValue) q = q.Where(i => i.Status == status);
        if (paymentStatus.HasValue) q = q.Where(i => i.PaymentStatus == paymentStatus);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(i => i.InvoiceNumber.Contains(s) || i.CustomerName.Contains(s) || (i.CustomerPhone != null && i.CustomerPhone.Contains(s)));
        }
        return await q.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.CreatedAt)
            .Select(i => new InvoiceListItemDto(i.Id, i.InvoiceNumber, i.InvoiceDate, i.CustomerName, i.Status, i.GrandTotal, i.AmountPaid, i.AmountDue, i.PaymentStatus,
                i.Items.Count, i.CreatedAt))
            .ToPagedResultAsync(page, pageSize, ct);
    }

    public async Task<InvoiceDto> GetInvoiceAsync(Guid id, CancellationToken ct)
    {
        var inv = await _db.SalesInvoices.AsNoTracking().Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice", id);
        return await BuildInvoiceDtoAsync(inv, ct);
    }

    private async Task<InvoiceDto> BuildInvoiceDtoAsync(SalesInvoice i, CancellationToken ct)
    {
        var payments = await (from a in _db.PaymentAllocations
                              join p in _db.Payments on a.PaymentId equals p.Id
                              where a.SalesInvoiceId == i.Id
                              orderby p.PaidAt
                              select new InvoicePaymentDto(p.Id, p.PaymentNumber, p.Method, a.Amount, p.PaidAt, p.Reference)).ToListAsync(ct);
        var createdBy = i.CreatedBy.HasValue ? await _db.Users.Where(u => u.Id == i.CreatedBy).Select(u => u.FullName).FirstOrDefaultAsync(ct) : null;
        return new InvoiceDto(i.Id, i.InvoiceNumber, i.InvoiceDate, i.CustomerId, i.CustomerName, i.CustomerPhone, i.CustomerGstin, i.IsInterState, i.Status,
            i.DiscountType, i.DiscountValue, i.SubTotal, i.DiscountTotal, i.TaxableTotal, i.CgstTotal, i.SgstTotal, i.IgstTotal, i.RoundOff, i.GrandTotal,
            i.AmountPaid, i.AmountDue, i.PaymentStatus, i.Notes, i.CreatedAt, createdBy, i.CancelledAt, i.CancelReason,
            i.Items.OrderBy(x => x.Title).Select(x => new InvoiceItemDto(x.Id, x.BookId, x.Title, x.Isbn, x.HsnCode, x.Quantity, x.UnitPrice, x.DiscountPercent, x.DiscountAmount,
                x.TaxableAmount, x.GstRate, x.CgstAmount, x.SgstAmount, x.IgstAmount, x.LineTotal, x.QuantityReturned)).ToList(),
            payments);
    }

    // ---------------- cancel ----------------

    public async Task<InvoiceDto> CancelInvoiceAsync(Guid id, CancelInvoiceRequest r, CancellationToken ct)
    {
        await _cancelValidator.ValidateAndThrowAppAsync(r, ct);
        var inv = await _db.SalesInvoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice", id);
        if (inv.Status == InvoiceStatus.Cancelled) throw new BusinessRuleException("already-cancelled", "Invoice is already cancelled.");
        if (inv.Items.Any(x => x.QuantityReturned > 0)) throw new BusinessRuleException("has-returns", "Invoices with returns cannot be cancelled.");
        if (inv.InvoiceDate < _clock.TodayIst.AddDays(-1)) throw new BusinessRuleException("too-old", "Only invoices from today or yesterday can be cancelled; use a sales return instead.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Reverse payments: record an outgoing refund payment for the paid amount.
        if (inv.AmountPaid > 0)
        {
            var refund = new Payment
            {
                TenantId = _tenant.TenantId, PaymentNumber = await _numbers.NextAsync("PAY", "PAY", _clock.TodayIst, ct), CustomerId = inv.CustomerId,
                Direction = PaymentDirection.Out, Method = PaymentMethod.Cash, Amount = inv.AmountPaid, PaidAt = _clock.UtcNow,
                Reference = inv.InvoiceNumber, Notes = $"Refund on cancellation: {r.Reason}"
            };
            refund.Allocations.Add(new PaymentAllocation { SalesInvoiceId = inv.Id, Amount = -inv.AmountPaid });
            _db.Payments.Add(refund);
        }
        inv.Status = InvoiceStatus.Cancelled;
        inv.CancelledAt = _clock.UtcNow;
        inv.CancelReason = r.Reason.Trim();
        inv.AmountPaid = 0; inv.AmountDue = 0; inv.PaymentStatus = PaymentStatus.Paid;
        await _db.SaveChangesAsync(ct);
        await _inventory.ReceiveAsync(inv.StoreId, inv.Items.Select(i => new StockDelta(i.BookId, i.Quantity, i.UnitCost)).ToList(),
            new MovementRef(MovementType.SaleCancellation, "SalesInvoice", inv.Id, inv.InvoiceNumber, r.Reason), ct);
        await tx.CommitAsync(ct);
        return await BuildInvoiceDtoAsync(inv, ct);
    }

    // ---------------- returns ----------------

    public async Task<SalesReturnDto> CreateReturnAsync(CreateSalesReturnRequest r, CancellationToken ct)
    {
        await _returnValidator.ValidateAndThrowAppAsync(r, ct);
        var inv = await _db.SalesInvoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == r.SalesInvoiceId, ct) ?? throw new NotFoundException("Invoice", r.SalesInvoiceId);
        if (inv.Status == InvoiceStatus.Cancelled) throw new BusinessRuleException("cancelled", "Cannot return items from a cancelled invoice.");
        if (r.RefundMode == RefundMode.CreditNote && inv.CustomerId is null)
            throw new BusinessRuleException("walk-in-credit-note", "Credit notes need a named customer. Refund in cash instead.");

        var date = r.ReturnDate ?? _clock.TodayIst;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var sr = new SalesReturn
        {
            TenantId = _tenant.TenantId, StoreId = inv.StoreId, SalesInvoiceId = inv.Id, CustomerId = inv.CustomerId, ReturnDate = date, Reason = r.Reason,
            RefundMode = r.RefundMode, ReturnNumber = await _numbers.NextAsync("SR", "SR", date, ct)
        };
        var resalable = new List<StockDelta>();
        var damaged = new List<(Guid bookId, int qty, decimal cost)>();
        foreach (var g in r.Lines.GroupBy(l => l.SalesInvoiceItemId))
        {
            var item = inv.Items.FirstOrDefault(x => x.Id == g.Key) ?? throw new NotFoundException("InvoiceItem", g.Key);
            var totalQty = g.Sum(x => x.Quantity);
            if (totalQty > item.Quantity - item.QuantityReturned)
                throw new BusinessRuleException("over-return", $"Cannot return {totalQty} of '{item.Title}'; only {item.Quantity - item.QuantityReturned} eligible.");
            item.QuantityReturned += totalQty;

            // One return line per condition, so a mixed return (some resalable, some damaged) books each part correctly.
            foreach (var byCondition in g.GroupBy(x => x.Condition))
            {
                var condition = byCondition.Key;
                var qty = byCondition.Sum(x => x.Quantity);
                // proportional amounts from the invoice line
                var taxable = Money.Round2(item.TaxableAmount * qty / item.Quantity);
                var tax = Money.Round2((item.CgstAmount + item.SgstAmount + item.IgstAmount) * qty / item.Quantity);
                sr.Items.Add(new SalesReturnItem
                {
                    SalesInvoiceItemId = item.Id, BookId = item.BookId, Quantity = qty, Condition = condition, UnitPrice = item.UnitPrice,
                    TaxableAmount = taxable, TaxAmount = tax, LineTotal = taxable + tax
                });
                if (condition == ReturnCondition.Resalable) resalable.Add(new StockDelta(item.BookId, qty, item.UnitCost));
                else damaged.Add((item.BookId, qty, item.UnitCost));
            }
        }
        sr.SubTotal = sr.Items.Sum(i => i.TaxableAmount);
        sr.TaxTotal = sr.Items.Sum(i => i.TaxAmount);
        sr.Total = sr.Items.Sum(i => i.LineTotal);
        inv.Status = inv.Items.All(x => x.QuantityReturned >= x.Quantity) ? InvoiceStatus.Returned : InvoiceStatus.PartiallyReturned;

        // Money side
        string? creditNoteNumber = null;
        switch (r.RefundMode)
        {
            case RefundMode.CreditNote:
            {
                var cn = new CreditNote
                {
                    TenantId = _tenant.TenantId, CreditNoteNumber = await _numbers.NextAsync("CN", "CN", date, ct), CustomerId = inv.CustomerId, SalesReturnId = sr.Id,
                    Amount = sr.Total, Balance = sr.Total, IssuedAt = _clock.UtcNow
                };
                _db.CreditNotes.Add(cn);
                sr.CreditNoteId = cn.Id; creditNoteNumber = cn.CreditNoteNumber;
                break;
            }
            case RefundMode.AdjustAgainstDue:
            {
                // Reduce what the customer owes on this invoice first.
                var reduce = Math.Min(sr.Total, inv.AmountDue);
                inv.GrandTotal -= sr.Total;
                inv.AmountPaid = Math.Min(inv.AmountPaid, inv.GrandTotal);
                inv.RecomputePaymentStatus();
                var leftover = sr.Total - reduce;
                if (leftover > 0)
                {
                    if (inv.CustomerId is null) goto case RefundMode.Cash;
                    var cn = new CreditNote
                    {
                        TenantId = _tenant.TenantId, CreditNoteNumber = await _numbers.NextAsync("CN", "CN", date, ct), CustomerId = inv.CustomerId, SalesReturnId = sr.Id,
                        Amount = leftover, Balance = leftover, IssuedAt = _clock.UtcNow
                    };
                    _db.CreditNotes.Add(cn);
                    sr.CreditNoteId = cn.Id; creditNoteNumber = cn.CreditNoteNumber;
                }
                break;
            }
            case RefundMode.Cash:
            default:
            {
                var refund = new Payment
                {
                    TenantId = _tenant.TenantId, PaymentNumber = await _numbers.NextAsync("PAY", "PAY", date, ct), CustomerId = inv.CustomerId,
                    Direction = PaymentDirection.Out, Method = PaymentMethod.Cash, Amount = sr.Total, PaidAt = _clock.UtcNow, Reference = sr.ReturnNumber, Notes = "Sales return refund"
                };
                refund.Allocations.Add(new PaymentAllocation { SalesInvoiceId = inv.Id, Amount = -sr.Total });
                _db.Payments.Add(refund);
                sr.RefundPaymentId = refund.Id;
                inv.GrandTotal -= sr.Total;
                inv.AmountPaid -= sr.Total;
                if (inv.AmountPaid < 0) inv.AmountPaid = 0;
                inv.RecomputePaymentStatus();
                break;
            }
        }

        _db.SalesReturns.Add(sr);
        await _db.SaveChangesAsync(ct);
        if (resalable.Count > 0)
            await _inventory.ReceiveAsync(inv.StoreId, resalable, new MovementRef(MovementType.SalesReturn, "SalesReturn", sr.Id, sr.ReturnNumber, r.Reason), ct);
        foreach (var (bookId, qty, cost) in damaged)
        {
            // Net zero on-hand: +qty as a return, then -qty into the damaged bucket.
            var reference = new MovementRef(MovementType.SalesReturn, "SalesReturn", sr.Id, sr.ReturnNumber, "damaged return");
            await _inventory.ReceiveAsync(inv.StoreId, new[] { new StockDelta(bookId, qty, cost) }, reference, ct);
            await _inventory.MarkDamagedAsync(inv.StoreId, bookId, qty, reference with { Type = MovementType.Damaged }, ct);
        }
        await tx.CommitAsync(ct);
        return await GetReturnAsync(sr.Id, ct);
    }

    public async Task<PagedResult<SalesReturnDto>> ListReturnsAsync(DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.SalesReturns.AsNoTracking().AsQueryable();
        if (from.HasValue) q = q.Where(x => x.ReturnDate >= from);
        if (to.HasValue) q = q.Where(x => x.ReturnDate <= to);
        var (p, s) = PagingExtensions.Normalize(page, pageSize);
        var total = await q.CountAsync(ct);
        var ids = await q.OrderByDescending(x => x.ReturnDate).ThenByDescending(x => x.CreatedAt).Skip((p - 1) * s).Take(s).Select(x => x.Id).ToListAsync(ct);
        var items = new List<SalesReturnDto>();
        foreach (var id in ids) items.Add(await GetReturnAsync(id, ct));
        return new PagedResult<SalesReturnDto> { Items = items, Page = p, PageSize = s, TotalCount = total };
    }

    public async Task<SalesReturnDto> GetReturnAsync(Guid id, CancellationToken ct)
    {
        var sr = await _db.SalesReturns.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("SalesReturn", id);
        var inv = await _db.SalesInvoices.AsNoTracking().Where(i => i.Id == sr.SalesInvoiceId).Select(i => new { i.InvoiceNumber, i.CustomerName }).FirstAsync(ct);
        var cnNumber = sr.CreditNoteId.HasValue ? await _db.CreditNotes.Where(c => c.Id == sr.CreditNoteId).Select(c => c.CreditNoteNumber).FirstOrDefaultAsync(ct) : null;
        var bookIds = sr.Items.Select(i => i.BookId).ToList();
        var titles = await _db.Books.Where(b => bookIds.Contains(b.Id)).Select(b => new { b.Id, b.Title }).ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        return new SalesReturnDto(sr.Id, sr.ReturnNumber, sr.SalesInvoiceId, inv.InvoiceNumber, inv.CustomerName, sr.ReturnDate, sr.Reason, sr.RefundMode,
            sr.SubTotal, sr.TaxTotal, sr.Total, sr.CreditNoteId, cnNumber, sr.CreatedAt,
            sr.Items.Select(i => new SalesReturnItemDto(i.Id, i.BookId, titles.GetValueOrDefault(i.BookId, ""), i.Quantity, i.Condition, i.UnitPrice, i.TaxableAmount, i.TaxAmount, i.LineTotal)).ToList());
    }
}
