using BookFlow.Application.Common.Extensions;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Common.Models;
using BookFlow.Domain.Entities.Finance;
using BookFlow.Domain.Entities.Parties;
using BookFlow.Domain.Enums;
using BookFlow.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Application.Features.Parties;

public sealed record CustomerDto(Guid Id, string Name, string? Phone, string? Email, string? Gstin, string? StateCode, CustomerType Type,
    decimal CreditLimit, string? Address, bool IsActive, decimal Outstanding, byte[] RowVersion);
public sealed record SaveCustomerRequest(string Name, string? Phone, string? Email, string? Gstin, string? StateCode, CustomerType Type,
    decimal CreditLimit, string? Address, bool IsActive, byte[]? RowVersion);

public sealed record SupplierDto(Guid Id, string Name, string? ContactPerson, string? Phone, string? Email, string? Gstin, string? Address,
    bool IsActive, decimal Outstanding, byte[] RowVersion);
public sealed record SaveSupplierRequest(string Name, string? ContactPerson, string? Phone, string? Email, string? Gstin, string? Address, bool IsActive, byte[]? RowVersion);

public sealed record StatementLine(DateTime Date, string Type, string Number, decimal Debit, decimal Credit, decimal Balance, Guid ReferenceId);
public sealed record StatementDto(Guid PartyId, string PartyName, decimal OpeningBalance, decimal ClosingBalance, IReadOnlyList<StatementLine> Lines);

public sealed record SupplierPaymentRequest(decimal Amount, PaymentMethod Method, DateTime? PaidAt, string? Reference, string? Notes, Guid? GoodsReceiptId);

public sealed class SaveCustomerValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).IndianPhone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Gstin).Gstin().When(x => !string.IsNullOrWhiteSpace(x.Gstin));
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Type).IsInEnum();
    }
}

public sealed class SaveSupplierValidator : AbstractValidator<SaveSupplierRequest>
{
    public SaveSupplierValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).IndianPhone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Gstin).Gstin().When(x => !string.IsNullOrWhiteSpace(x.Gstin));
    }
}

public sealed class SupplierPaymentValidator : AbstractValidator<SupplierPaymentRequest>
{
    public SupplierPaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method).IsInEnum().NotEqual(PaymentMethod.Credit).NotEqual(PaymentMethod.CreditNote);
    }
}

public interface IPartiesService
{
    Task<PagedResult<CustomerDto>> ListCustomersAsync(string? search, bool includeInactive, int? page, int? pageSize, CancellationToken ct);
    Task<CustomerDto> GetCustomerAsync(Guid id, CancellationToken ct);
    Task<CustomerDto> CreateCustomerAsync(SaveCustomerRequest request, CancellationToken ct);
    Task<CustomerDto> UpdateCustomerAsync(Guid id, SaveCustomerRequest request, CancellationToken ct);
    Task<StatementDto> CustomerStatementAsync(Guid id, DateOnly? from, DateOnly? to, CancellationToken ct);

    Task<PagedResult<SupplierDto>> ListSuppliersAsync(string? search, bool includeInactive, int? page, int? pageSize, CancellationToken ct);
    Task<SupplierDto> GetSupplierAsync(Guid id, CancellationToken ct);
    Task<SupplierDto> CreateSupplierAsync(SaveSupplierRequest request, CancellationToken ct);
    Task<SupplierDto> UpdateSupplierAsync(Guid id, SaveSupplierRequest request, CancellationToken ct);
    Task<StatementDto> SupplierStatementAsync(Guid id, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<Guid> RecordSupplierPaymentAsync(Guid supplierId, SupplierPaymentRequest request, CancellationToken ct);
}

public sealed class PartiesService : IPartiesService
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IValidator<SaveCustomerRequest> _customerValidator;
    private readonly IValidator<SaveSupplierRequest> _supplierValidator;
    private readonly IValidator<SupplierPaymentRequest> _supplierPaymentValidator;

    public PartiesService(IAppDbContext db, ITenantContext tenant, IClock clock, IValidator<SaveCustomerRequest> customerValidator,
        IValidator<SaveSupplierRequest> supplierValidator, IValidator<SupplierPaymentRequest> supplierPaymentValidator)
    {
        _db = db; _tenant = tenant; _clock = clock; _customerValidator = customerValidator; _supplierValidator = supplierValidator;
        _supplierPaymentValidator = supplierPaymentValidator;
    }

    // ---------- customers ----------

    private IQueryable<CustomerDto> ProjectCustomers(IQueryable<Customer> q) =>
        q.Select(c => new CustomerDto(c.Id, c.Name, c.Phone, c.Email, c.Gstin, c.StateCode, c.Type, c.CreditLimit, c.Address, c.IsActive,
            _db.SalesInvoices.Where(i => i.CustomerId == c.Id && i.Status != InvoiceStatus.Cancelled).Sum(i => (decimal?)i.AmountDue) ?? 0m,
            c.RowVersion));

    public async Task<PagedResult<CustomerDto>> ListCustomersAsync(string? search, bool includeInactive, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.Customers.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.Name.Contains(s) || (c.Phone != null && c.Phone.Contains(s)) || (c.Gstin != null && c.Gstin.Contains(s)));
        }
        return await ProjectCustomers(q.OrderBy(c => c.Name)).ToPagedResultAsync(page, pageSize, ct);
    }

    public async Task<CustomerDto> GetCustomerAsync(Guid id, CancellationToken ct) =>
        await ProjectCustomers(_db.Customers.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Customer", id);

    public async Task<CustomerDto> CreateCustomerAsync(SaveCustomerRequest r, CancellationToken ct)
    {
        await _customerValidator.ValidateAndThrowAppAsync(r, ct);
        var c = new Customer { TenantId = _tenant.TenantId };
        Apply(c, r);
        if (c.Phone is not null && await _db.Customers.AnyAsync(x => x.Phone == c.Phone, ct))
            throw new ConflictException("duplicate-phone", "A customer with this phone number already exists.");
        _db.Customers.Add(c);
        await _db.SaveChangesAsync(ct);
        return await GetCustomerAsync(c.Id, ct);
    }

    public async Task<CustomerDto> UpdateCustomerAsync(Guid id, SaveCustomerRequest r, CancellationToken ct)
    {
        await _customerValidator.ValidateAndThrowAppAsync(r, ct);
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Customer", id);
        if (r.RowVersion is { Length: > 0 } && !r.RowVersion.SequenceEqual(c.RowVersion))
            throw new ConflictException("concurrency", "This customer was modified by someone else. Reload and try again.");
        Apply(c, r);
        if (c.Phone is not null && await _db.Customers.AnyAsync(x => x.Phone == c.Phone && x.Id != id, ct))
            throw new ConflictException("duplicate-phone", "A customer with this phone number already exists.");
        await _db.SaveChangesAsync(ct);
        return await GetCustomerAsync(c.Id, ct);
    }

    private static void Apply(Customer c, SaveCustomerRequest r)
    {
        c.Name = r.Name.Trim();
        c.Phone = string.IsNullOrWhiteSpace(r.Phone) ? null : r.Phone.Trim();
        c.Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim().ToLowerInvariant();
        c.Gstin = string.IsNullOrWhiteSpace(r.Gstin) ? null : r.Gstin.Trim().ToUpperInvariant();
        c.StateCode = string.IsNullOrWhiteSpace(r.StateCode) ? (c.Gstin?.Length >= 2 ? c.Gstin[..2] : null) : r.StateCode;
        c.Type = r.Type;
        c.CreditLimit = r.CreditLimit;
        c.Address = r.Address;
        c.IsActive = r.IsActive;
    }

    public async Task<StatementDto> CustomerStatementAsync(Guid id, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var c = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Customer", id);
        var fromDt = from?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        var toDt = to?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;

        var invoices = await _db.SalesInvoices.AsNoTracking()
            .Where(i => i.CustomerId == id && i.Status != InvoiceStatus.Cancelled)
            .Select(i => new { i.Id, Date = i.InvoiceDate, i.InvoiceNumber, i.GrandTotal }).ToListAsync(ct);
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == id && p.Direction == PaymentDirection.In)
            .Select(p => new { p.Id, Date = p.PaidAt, p.PaymentNumber, p.Amount }).ToListAsync(ct);
        var returns = await _db.SalesReturns.AsNoTracking()
            .Where(r => r.CustomerId == id)
            .Select(r => new { r.Id, Date = r.ReturnDate, r.ReturnNumber, r.Total }).ToListAsync(ct);

        var all = invoices.Select(i => (Date: i.Date.ToDateTime(TimeOnly.MinValue), Type: "Invoice", Number: i.InvoiceNumber, Debit: i.GrandTotal, Credit: 0m, Id: i.Id))
            .Concat(payments.Select(p => (Date: p.Date, Type: "Payment", Number: p.PaymentNumber, Debit: 0m, Credit: p.Amount, Id: p.Id)))
            .Concat(returns.Select(r => (Date: r.Date.ToDateTime(TimeOnly.MinValue), Type: "Return", Number: r.ReturnNumber, Debit: 0m, Credit: r.Total, Id: r.Id)))
            .OrderBy(x => x.Date).ToList();

        var opening = all.Where(x => x.Date < fromDt).Sum(x => x.Debit - x.Credit);
        var running = opening;
        var lines = new List<StatementLine>();
        foreach (var x in all.Where(x => x.Date >= fromDt && x.Date <= toDt))
        {
            running += x.Debit - x.Credit;
            lines.Add(new StatementLine(x.Date, x.Type, x.Number, x.Debit, x.Credit, running, x.Id));
        }
        return new StatementDto(c.Id, c.Name, opening, running, lines);
    }

    // ---------- suppliers ----------

    private IQueryable<SupplierDto> ProjectSuppliers(IQueryable<Supplier> q) =>
        q.Select(s => new SupplierDto(s.Id, s.Name, s.ContactPerson, s.Phone, s.Email, s.Gstin, s.Address, s.IsActive,
            (_db.GoodsReceipts.Where(g => g.SupplierId == s.Id).Sum(g => (decimal?)g.Total) ?? 0m)
            - (_db.PurchaseReturns.Where(p => p.SupplierId == s.Id).Sum(p => (decimal?)p.Total) ?? 0m)
            - (_db.SupplierPayments.Where(p => p.SupplierId == s.Id).Sum(p => (decimal?)p.Amount) ?? 0m),
            s.RowVersion));

    public async Task<PagedResult<SupplierDto>> ListSuppliersAsync(string? search, bool includeInactive, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.Suppliers.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.Name.Contains(s) || (c.Phone != null && c.Phone.Contains(s)) || (c.ContactPerson != null && c.ContactPerson.Contains(s)));
        }
        return await ProjectSuppliers(q.OrderBy(c => c.Name)).ToPagedResultAsync(page, pageSize, ct);
    }

    public async Task<SupplierDto> GetSupplierAsync(Guid id, CancellationToken ct) =>
        await ProjectSuppliers(_db.Suppliers.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Supplier", id);

    public async Task<SupplierDto> CreateSupplierAsync(SaveSupplierRequest r, CancellationToken ct)
    {
        await _supplierValidator.ValidateAndThrowAppAsync(r, ct);
        var name = r.Name.Trim();
        if (await _db.Suppliers.AnyAsync(x => x.Name == name, ct)) throw new ConflictException("duplicate", "A supplier with this name already exists.");
        var s = new Supplier { TenantId = _tenant.TenantId };
        Apply(s, r);
        _db.Suppliers.Add(s);
        await _db.SaveChangesAsync(ct);
        return await GetSupplierAsync(s.Id, ct);
    }

    public async Task<SupplierDto> UpdateSupplierAsync(Guid id, SaveSupplierRequest r, CancellationToken ct)
    {
        await _supplierValidator.ValidateAndThrowAppAsync(r, ct);
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Supplier", id);
        if (r.RowVersion is { Length: > 0 } && !r.RowVersion.SequenceEqual(s.RowVersion))
            throw new ConflictException("concurrency", "This supplier was modified by someone else. Reload and try again.");
        var name = r.Name.Trim();
        if (await _db.Suppliers.AnyAsync(x => x.Name == name && x.Id != id, ct)) throw new ConflictException("duplicate", "A supplier with this name already exists.");
        Apply(s, r);
        await _db.SaveChangesAsync(ct);
        return await GetSupplierAsync(s.Id, ct);
    }

    private static void Apply(Supplier s, SaveSupplierRequest r)
    {
        s.Name = r.Name.Trim(); s.ContactPerson = r.ContactPerson; s.Phone = r.Phone; s.Email = r.Email?.ToLowerInvariant();
        s.Gstin = r.Gstin?.ToUpperInvariant(); s.Address = r.Address; s.IsActive = r.IsActive;
    }

    public async Task<StatementDto> SupplierStatementAsync(Guid id, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var s = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Supplier", id);
        var fromDt = from?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        var toDt = to?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;

        var grns = await _db.GoodsReceipts.AsNoTracking().Where(g => g.SupplierId == id)
            .Select(g => new { g.Id, Date = g.ReceivedDate, g.GrnNumber, g.Total }).ToListAsync(ct);
        var returns = await _db.PurchaseReturns.AsNoTracking().Where(p => p.SupplierId == id)
            .Select(p => new { p.Id, Date = p.ReturnDate, p.PrNumber, p.Total }).ToListAsync(ct);
        var pays = await _db.SupplierPayments.AsNoTracking().Where(p => p.SupplierId == id)
            .Select(p => new { p.Id, Date = p.PaidAt, p.Reference, p.Amount }).ToListAsync(ct);

        var all = grns.Select(g => (Date: g.Date.ToDateTime(TimeOnly.MinValue), Type: "Goods receipt", Number: g.GrnNumber, Debit: 0m, Credit: g.Total, Id: g.Id))
            .Concat(returns.Select(r => (Date: r.Date.ToDateTime(TimeOnly.MinValue), Type: "Purchase return", Number: r.PrNumber, Debit: r.Total, Credit: 0m, Id: r.Id)))
            .Concat(pays.Select(p => (Date: p.Date, Type: "Payment", Number: p.Reference ?? "", Debit: p.Amount, Credit: 0m, Id: p.Id)))
            .OrderBy(x => x.Date).ToList();

        // Supplier balance is what we owe: credits (receipts) minus debits (payments/returns).
        var opening = all.Where(x => x.Date < fromDt).Sum(x => x.Credit - x.Debit);
        var running = opening;
        var lines = new List<StatementLine>();
        foreach (var x in all.Where(x => x.Date >= fromDt && x.Date <= toDt))
        {
            running += x.Credit - x.Debit;
            lines.Add(new StatementLine(x.Date, x.Type, x.Number, x.Debit, x.Credit, running, x.Id));
        }
        return new StatementDto(s.Id, s.Name, opening, running, lines);
    }

    public async Task<Guid> RecordSupplierPaymentAsync(Guid supplierId, SupplierPaymentRequest r, CancellationToken ct)
    {
        await _supplierPaymentValidator.ValidateAndThrowAppAsync(r, ct);
        _ = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == supplierId, ct) ?? throw new NotFoundException("Supplier", supplierId);
        var p = new SupplierPayment
        {
            TenantId = _tenant.TenantId, SupplierId = supplierId, Amount = r.Amount, Method = r.Method,
            PaidAt = r.PaidAt ?? _clock.UtcNow, Reference = r.Reference, Notes = r.Notes, GoodsReceiptId = r.GoodsReceiptId
        };
        _db.SupplierPayments.Add(p);
        if (r.GoodsReceiptId.HasValue)
        {
            var grn = await _db.GoodsReceipts.FirstOrDefaultAsync(g => g.Id == r.GoodsReceiptId, ct) ?? throw new NotFoundException("GoodsReceipt", r.GoodsReceiptId);
            grn.AmountPaid += r.Amount;
        }
        await _db.SaveChangesAsync(ct);
        return p.Id;
    }
}
