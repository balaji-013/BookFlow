using BookFlow.Application.Common.Extensions;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Common.Models;
using BookFlow.Domain.Common;
using BookFlow.Domain.Entities.Purchasing;
using BookFlow.Domain.Enums;
using BookFlow.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Application.Features.Purchasing;

public sealed record PoLineRequest(Guid BookId, int Quantity, decimal UnitCost);
public sealed record CreatePurchaseOrderRequest(Guid SupplierId, DateOnly? OrderDate, DateOnly? ExpectedDate, string? Notes, List<PoLineRequest> Lines, bool Submit);
public sealed record PoItemDto(Guid Id, Guid BookId, string Title, string? Isbn, int QuantityOrdered, int QuantityReceived, decimal UnitCost, decimal GstRate, decimal LineTotal);
public sealed record PurchaseOrderDto(Guid Id, string PoNumber, Guid SupplierId, string SupplierName, DateOnly OrderDate, DateOnly? ExpectedDate,
    PurchaseOrderStatus Status, string? Notes, decimal SubTotal, decimal TaxTotal, decimal Total, DateTime CreatedAt, List<PoItemDto> Items);

public sealed record ReceiptLineRequest(Guid BookId, int Quantity, decimal? UnitCost);
public sealed record ReceiveGoodsRequest(DateOnly? ReceivedDate, string? SupplierInvoiceNumber, DateOnly? SupplierInvoiceDate, string? Notes, List<ReceiptLineRequest> Lines);
public sealed record DirectReceiptRequest(Guid SupplierId, DateOnly? ReceivedDate, string? SupplierInvoiceNumber, DateOnly? SupplierInvoiceDate, string? Notes, List<ReceiptLineRequest> Lines);
public sealed record GrnItemDto(Guid BookId, string Title, int Quantity, decimal UnitCost, decimal GstRate, decimal LineTotal);
public sealed record GoodsReceiptDto(Guid Id, string GrnNumber, Guid? PurchaseOrderId, string? PoNumber, Guid SupplierId, string SupplierName, DateOnly ReceivedDate,
    string? SupplierInvoiceNumber, DateOnly? SupplierInvoiceDate, decimal SubTotal, decimal TaxTotal, decimal Total, decimal AmountPaid, string? Notes, List<GrnItemDto> Items);

public sealed record PurchaseReturnLineRequest(Guid BookId, int Quantity, decimal UnitCost);
public sealed record PurchaseReturnRequest(Guid SupplierId, Guid? GoodsReceiptId, DateOnly? ReturnDate, string? Reason, List<PurchaseReturnLineRequest> Lines);
public sealed record PurchaseReturnDto(Guid Id, string PrNumber, Guid SupplierId, string SupplierName, DateOnly ReturnDate, string? Reason, decimal Total, int LineCount);

public sealed class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.BookId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class ReceiveGoodsValidator : AbstractValidator<ReceiveGoodsRequest>
{
    public ReceiveGoodsValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.BookId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost.HasValue);
        });
    }
}

public sealed class DirectReceiptValidator : AbstractValidator<DirectReceiptRequest>
{
    public DirectReceiptValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.BookId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public sealed class PurchaseReturnValidator : AbstractValidator<PurchaseReturnRequest>
{
    public PurchaseReturnValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.BookId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0);
        });
    }
}

public interface IPurchasingService
{
    Task<PagedResult<PurchaseOrderDto>> ListOrdersAsync(Guid? supplierId, PurchaseOrderStatus? status, int? page, int? pageSize, CancellationToken ct);
    Task<PurchaseOrderDto> GetOrderAsync(Guid id, CancellationToken ct);
    Task<PurchaseOrderDto> CreateOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct);
    Task<PurchaseOrderDto> SubmitOrderAsync(Guid id, CancellationToken ct);
    Task<PurchaseOrderDto> CancelOrderAsync(Guid id, CancellationToken ct);
    Task<GoodsReceiptDto> ReceiveAsync(Guid purchaseOrderId, ReceiveGoodsRequest request, CancellationToken ct);
    Task<GoodsReceiptDto> DirectReceiptAsync(DirectReceiptRequest request, CancellationToken ct);
    Task<PagedResult<GoodsReceiptDto>> ListReceiptsAsync(Guid? supplierId, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct);
    Task<GoodsReceiptDto> GetReceiptAsync(Guid id, CancellationToken ct);
    Task<PurchaseReturnDto> CreateReturnAsync(PurchaseReturnRequest request, CancellationToken ct);
    Task<PagedResult<PurchaseReturnDto>> ListReturnsAsync(int? page, int? pageSize, CancellationToken ct);
}

public sealed class PurchasingService : IPurchasingService
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IInventoryService _inventory;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IClock _clock;
    private readonly IValidator<CreatePurchaseOrderRequest> _poValidator;
    private readonly IValidator<ReceiveGoodsRequest> _receiveValidator;
    private readonly IValidator<DirectReceiptRequest> _directValidator;
    private readonly IValidator<PurchaseReturnRequest> _returnValidator;

    public PurchasingService(IAppDbContext db, ITenantContext tenant, IInventoryService inventory, IDocumentNumberGenerator numbers, IClock clock,
        IValidator<CreatePurchaseOrderRequest> poValidator, IValidator<ReceiveGoodsRequest> receiveValidator, IValidator<DirectReceiptRequest> directValidator,
        IValidator<PurchaseReturnRequest> returnValidator)
    {
        _db = db; _tenant = tenant; _inventory = inventory; _numbers = numbers; _clock = clock;
        _poValidator = poValidator; _receiveValidator = receiveValidator; _directValidator = directValidator; _returnValidator = returnValidator;
    }

    private async Task<Guid> StoreIdAsync(CancellationToken ct) =>
        _tenant.StoreId ?? (await _db.Stores.FirstOrDefaultAsync(s => s.IsDefault, ct))?.Id ?? throw new BusinessRuleException("no-store", "No default store.");

    // ---------- purchase orders ----------

    private async Task<PurchaseOrderDto> MapOrderAsync(PurchaseOrder po, CancellationToken ct)
    {
        var supplierName = await _db.Suppliers.Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        var bookIds = po.Items.Select(i => i.BookId).ToList();
        var books = await _db.Books.Where(b => bookIds.Contains(b.Id)).Select(b => new { b.Id, b.Title, b.Isbn }).ToDictionaryAsync(b => b.Id, ct);
        return new PurchaseOrderDto(po.Id, po.PoNumber, po.SupplierId, supplierName, po.OrderDate, po.ExpectedDate, po.Status, po.Notes, po.SubTotal, po.TaxTotal, po.Total, po.CreatedAt,
            po.Items.Select(i => new PoItemDto(i.Id, i.BookId, books.TryGetValue(i.BookId, out var b) ? b.Title : "", books.TryGetValue(i.BookId, out var b2) ? b2.Isbn : null,
                i.QuantityOrdered, i.QuantityReceived, i.UnitCost, i.GstRate, i.LineTotal)).ToList());
    }

    public async Task<PagedResult<PurchaseOrderDto>> ListOrdersAsync(Guid? supplierId, PurchaseOrderStatus? status, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.PurchaseOrders.AsNoTracking().Include(p => p.Items).AsQueryable();
        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId);
        if (status.HasValue) q = q.Where(p => p.Status == status);
        var (p, s) = PagingExtensions.Normalize(page, pageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.CreatedAt).Skip((p - 1) * s).Take(s).ToListAsync(ct);
        var items = new List<PurchaseOrderDto>();
        foreach (var row in rows) items.Add(await MapOrderAsync(row, ct));
        return new PagedResult<PurchaseOrderDto> { Items = items, Page = p, PageSize = s, TotalCount = total };
    }

    public async Task<PurchaseOrderDto> GetOrderAsync(Guid id, CancellationToken ct)
    {
        var po = await _db.PurchaseOrders.AsNoTracking().Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("PurchaseOrder", id);
        return await MapOrderAsync(po, ct);
    }

    public async Task<PurchaseOrderDto> CreateOrderAsync(CreatePurchaseOrderRequest r, CancellationToken ct)
    {
        await _poValidator.ValidateAndThrowAppAsync(r, ct);
        _ = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == r.SupplierId && s.IsActive, ct) ?? throw new NotFoundException("Supplier", r.SupplierId);
        var bookIds = r.Lines.Select(l => l.BookId).Distinct().ToList();
        var books = await _db.Books.Where(b => bookIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        foreach (var id in bookIds) if (!books.ContainsKey(id)) throw new NotFoundException("Book", id);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var po = new PurchaseOrder
        {
            TenantId = _tenant.TenantId, SupplierId = r.SupplierId, StoreId = await StoreIdAsync(ct),
            OrderDate = r.OrderDate ?? _clock.TodayIst, ExpectedDate = r.ExpectedDate, Notes = r.Notes,
            Status = r.Submit ? PurchaseOrderStatus.Submitted : PurchaseOrderStatus.Draft,
            PoNumber = await _numbers.NextAsync("PO", "PO", r.OrderDate ?? _clock.TodayIst, ct)
        };
        foreach (var g in r.Lines.GroupBy(l => l.BookId))
        {
            var qty = g.Sum(x => x.Quantity);
            var cost = g.Last().UnitCost;
            var gst = books[g.Key].GstRate;
            var lineBase = Money.Round2(qty * cost);
            po.Items.Add(new PurchaseOrderItem { BookId = g.Key, QuantityOrdered = qty, UnitCost = cost, GstRate = gst, LineTotal = lineBase });
        }
        po.SubTotal = po.Items.Sum(i => i.LineTotal);
        po.TaxTotal = Money.Round2(po.Items.Sum(i => i.LineTotal * i.GstRate / 100m));
        po.Total = po.SubTotal + po.TaxTotal;
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await MapOrderAsync(po, ct);
    }

    public async Task<PurchaseOrderDto> SubmitOrderAsync(Guid id, CancellationToken ct)
    {
        var po = await _db.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("PurchaseOrder", id);
        if (po.Status != PurchaseOrderStatus.Draft) throw new BusinessRuleException("invalid-state", "Only draft orders can be submitted.");
        po.Status = PurchaseOrderStatus.Submitted;
        await _db.SaveChangesAsync(ct);
        return await MapOrderAsync(po, ct);
    }

    public async Task<PurchaseOrderDto> CancelOrderAsync(Guid id, CancellationToken ct)
    {
        var po = await _db.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("PurchaseOrder", id);
        if (po.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled) throw new BusinessRuleException("invalid-state", "This order cannot be cancelled.");
        if (po.Items.Any(i => i.QuantityReceived > 0)) throw new BusinessRuleException("partially-received", "Partially received orders cannot be cancelled; close it by receiving the remainder or record a purchase return.");
        po.Status = PurchaseOrderStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
        return await MapOrderAsync(po, ct);
    }

    // ---------- goods receipts ----------

    private async Task<GoodsReceiptDto> MapReceiptAsync(GoodsReceipt g, CancellationToken ct)
    {
        var supplierName = await _db.Suppliers.Where(s => s.Id == g.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        var poNumber = g.PurchaseOrderId.HasValue ? await _db.PurchaseOrders.Where(p => p.Id == g.PurchaseOrderId).Select(p => p.PoNumber).FirstOrDefaultAsync(ct) : null;
        var bookIds = g.Items.Select(i => i.BookId).ToList();
        var titles = await _db.Books.Where(b => bookIds.Contains(b.Id)).Select(b => new { b.Id, b.Title }).ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        return new GoodsReceiptDto(g.Id, g.GrnNumber, g.PurchaseOrderId, poNumber, g.SupplierId, supplierName, g.ReceivedDate, g.SupplierInvoiceNumber, g.SupplierInvoiceDate,
            g.SubTotal, g.TaxTotal, g.Total, g.AmountPaid, g.Notes,
            g.Items.Select(i => new GrnItemDto(i.BookId, titles.GetValueOrDefault(i.BookId, ""), i.Quantity, i.UnitCost, i.GstRate, i.LineTotal)).ToList());
    }

    public async Task<GoodsReceiptDto> ReceiveAsync(Guid purchaseOrderId, ReceiveGoodsRequest r, CancellationToken ct)
    {
        await _receiveValidator.ValidateAndThrowAppAsync(r, ct);
        var po = await _db.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == purchaseOrderId, ct) ?? throw new NotFoundException("PurchaseOrder", purchaseOrderId);
        if (po.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Received or PurchaseOrderStatus.Draft)
            throw new BusinessRuleException("invalid-state", $"Cannot receive goods for an order in state {po.Status}. Submit it first.");

        var bookIds = r.Lines.Select(l => l.BookId).Distinct().ToList();
        var books = await _db.Books.Where(b => bookIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var date = r.ReceivedDate ?? _clock.TodayIst;
        var grn = new GoodsReceipt
        {
            TenantId = _tenant.TenantId, PurchaseOrderId = po.Id, SupplierId = po.SupplierId, StoreId = po.StoreId, ReceivedDate = date,
            SupplierInvoiceNumber = r.SupplierInvoiceNumber, SupplierInvoiceDate = r.SupplierInvoiceDate, Notes = r.Notes,
            GrnNumber = await _numbers.NextAsync("GRN", "GRN", date, ct)
        };
        var deltas = new List<StockDelta>();
        foreach (var g in r.Lines.GroupBy(l => l.BookId))
        {
            var item = po.Items.FirstOrDefault(i => i.BookId == g.Key) ?? throw new BusinessRuleException("not-on-order", $"Book {g.Key} is not on this purchase order.");
            var qty = g.Sum(x => x.Quantity);
            var remaining = item.QuantityOrdered - item.QuantityReceived;
            if (qty > remaining) throw new BusinessRuleException("over-receipt", $"Receiving {qty} exceeds remaining quantity {remaining} for '{books.GetValueOrDefault(g.Key)?.Title}'.");
            var cost = g.Last().UnitCost ?? item.UnitCost;
            item.QuantityReceived += qty;
            var lineTotal = Money.Round2(qty * cost);
            grn.Items.Add(new GoodsReceiptItem { BookId = g.Key, Quantity = qty, UnitCost = cost, GstRate = item.GstRate, LineTotal = lineTotal });
            deltas.Add(new StockDelta(g.Key, qty, cost));
            if (books.TryGetValue(g.Key, out var book) && cost > 0) book.PurchasePrice = cost; // keep latest cost on the catalog
        }
        grn.SubTotal = grn.Items.Sum(i => i.LineTotal);
        grn.TaxTotal = Money.Round2(grn.Items.Sum(i => i.LineTotal * i.GstRate / 100m));
        grn.Total = grn.SubTotal + grn.TaxTotal;
        po.Status = po.Items.All(i => i.QuantityReceived >= i.QuantityOrdered) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;

        _db.GoodsReceipts.Add(grn);
        await _db.SaveChangesAsync(ct);
        await _inventory.ReceiveAsync(po.StoreId, deltas, new MovementRef(MovementType.Purchase, "GoodsReceipt", grn.Id, grn.GrnNumber), ct);
        await tx.CommitAsync(ct);
        return await MapReceiptAsync(grn, ct);
    }

    public async Task<GoodsReceiptDto> DirectReceiptAsync(DirectReceiptRequest r, CancellationToken ct)
    {
        await _directValidator.ValidateAndThrowAppAsync(r, ct);
        _ = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == r.SupplierId, ct) ?? throw new NotFoundException("Supplier", r.SupplierId);
        var bookIds = r.Lines.Select(l => l.BookId).Distinct().ToList();
        var books = await _db.Books.Where(b => bookIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        foreach (var id in bookIds) if (!books.ContainsKey(id)) throw new NotFoundException("Book", id);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var date = r.ReceivedDate ?? _clock.TodayIst;
        var storeId = await StoreIdAsync(ct);
        var grn = new GoodsReceipt
        {
            TenantId = _tenant.TenantId, SupplierId = r.SupplierId, StoreId = storeId, ReceivedDate = date,
            SupplierInvoiceNumber = r.SupplierInvoiceNumber, SupplierInvoiceDate = r.SupplierInvoiceDate, Notes = r.Notes,
            GrnNumber = await _numbers.NextAsync("GRN", "GRN", date, ct)
        };
        var deltas = new List<StockDelta>();
        foreach (var g in r.Lines.GroupBy(l => l.BookId))
        {
            var book = books[g.Key];
            var qty = g.Sum(x => x.Quantity);
            var cost = g.Last().UnitCost ?? book.PurchasePrice;
            grn.Items.Add(new GoodsReceiptItem { BookId = g.Key, Quantity = qty, UnitCost = cost, GstRate = book.GstRate, LineTotal = Money.Round2(qty * cost) });
            deltas.Add(new StockDelta(g.Key, qty, cost));
            if (cost > 0) book.PurchasePrice = cost;
        }
        grn.SubTotal = grn.Items.Sum(i => i.LineTotal);
        grn.TaxTotal = Money.Round2(grn.Items.Sum(i => i.LineTotal * i.GstRate / 100m));
        grn.Total = grn.SubTotal + grn.TaxTotal;
        _db.GoodsReceipts.Add(grn);
        await _db.SaveChangesAsync(ct);
        await _inventory.ReceiveAsync(storeId, deltas, new MovementRef(MovementType.Purchase, "GoodsReceipt", grn.Id, grn.GrnNumber), ct);
        await tx.CommitAsync(ct);
        return await MapReceiptAsync(grn, ct);
    }

    public async Task<PagedResult<GoodsReceiptDto>> ListReceiptsAsync(Guid? supplierId, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.GoodsReceipts.AsNoTracking().Include(g => g.Items).AsQueryable();
        if (supplierId.HasValue) q = q.Where(g => g.SupplierId == supplierId);
        if (from.HasValue) q = q.Where(g => g.ReceivedDate >= from);
        if (to.HasValue) q = q.Where(g => g.ReceivedDate <= to);
        var (p, s) = PagingExtensions.Normalize(page, pageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(g => g.ReceivedDate).ThenByDescending(g => g.CreatedAt).Skip((p - 1) * s).Take(s).ToListAsync(ct);
        var items = new List<GoodsReceiptDto>();
        foreach (var row in rows) items.Add(await MapReceiptAsync(row, ct));
        return new PagedResult<GoodsReceiptDto> { Items = items, Page = p, PageSize = s, TotalCount = total };
    }

    public async Task<GoodsReceiptDto> GetReceiptAsync(Guid id, CancellationToken ct)
    {
        var g = await _db.GoodsReceipts.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("GoodsReceipt", id);
        return await MapReceiptAsync(g, ct);
    }

    // ---------- purchase returns ----------

    public async Task<PurchaseReturnDto> CreateReturnAsync(PurchaseReturnRequest r, CancellationToken ct)
    {
        await _returnValidator.ValidateAndThrowAppAsync(r, ct);
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == r.SupplierId, ct) ?? throw new NotFoundException("Supplier", r.SupplierId);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var date = r.ReturnDate ?? _clock.TodayIst;
        var storeId = await StoreIdAsync(ct);
        var pr = new PurchaseReturn
        {
            TenantId = _tenant.TenantId, SupplierId = r.SupplierId, GoodsReceiptId = r.GoodsReceiptId, StoreId = storeId, ReturnDate = date, Reason = r.Reason,
            PrNumber = await _numbers.NextAsync("PR", "PR", date, ct)
        };
        var deltas = new List<StockDelta>();
        foreach (var g in r.Lines.GroupBy(l => l.BookId))
        {
            var qty = g.Sum(x => x.Quantity);
            var cost = g.Last().UnitCost;
            pr.Items.Add(new PurchaseReturnItem { BookId = g.Key, Quantity = qty, UnitCost = cost, LineTotal = Money.Round2(qty * cost) });
            deltas.Add(new StockDelta(g.Key, qty, cost));
        }
        pr.Total = pr.Items.Sum(i => i.LineTotal);
        _db.PurchaseReturns.Add(pr);
        await _db.SaveChangesAsync(ct);
        await _inventory.IssueAsync(storeId, deltas, new MovementRef(MovementType.PurchaseReturn, "PurchaseReturn", pr.Id, pr.PrNumber, r.Reason), ct);
        await tx.CommitAsync(ct);
        return new PurchaseReturnDto(pr.Id, pr.PrNumber, pr.SupplierId, supplier.Name, pr.ReturnDate, pr.Reason, pr.Total, pr.Items.Count);
    }

    public async Task<PagedResult<PurchaseReturnDto>> ListReturnsAsync(int? page, int? pageSize, CancellationToken ct)
    {
        var q = from pr in _db.PurchaseReturns.AsNoTracking()
                join s in _db.Suppliers on pr.SupplierId equals s.Id
                orderby pr.ReturnDate descending
                select new PurchaseReturnDto(pr.Id, pr.PrNumber, pr.SupplierId, s.Name, pr.ReturnDate, pr.Reason, pr.Total, pr.Items.Count);
        return await q.ToPagedResultAsync(page, pageSize, ct);
    }
}
