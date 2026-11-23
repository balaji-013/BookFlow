using BookFlow.Application.Common.Extensions;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Common.Models;
using BookFlow.Domain.Entities.Inventory;
using BookFlow.Domain.Enums;
using BookFlow.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Application.Features.Inventory;

public sealed record StockRowDto(Guid BookId, string Title, string? Isbn, string? Barcode, string? CategoryName, int QuantityOnHand, int QuantityDamaged,
    int ReorderLevel, decimal AverageCost, decimal SellingPrice, decimal StockValue, bool IsLow);
public sealed record MovementDto(Guid Id, DateTime OccurredAt, Guid BookId, string Title, MovementType MovementType, int Quantity, decimal UnitCost,
    string ReferenceType, Guid ReferenceId, string? ReferenceNumber, string? Notes);
public sealed record AdjustmentLineRequest(Guid BookId, int QuantityDelta, ReturnCondition Condition);
public sealed record StockAdjustmentRequest(string Reason, string? Notes, List<AdjustmentLineRequest> Lines);
public sealed record StockAdjustmentDto(Guid Id, string AdjustmentNumber, string Reason, DateTime AdjustedAt, string? Notes, int LineCount);
public sealed record ValuationDto(int DistinctBooks, int TotalUnits, int DamagedUnits, decimal ValueAtCost, decimal ValueAtSellingPrice);

public sealed class StockAdjustmentValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.BookId).NotEmpty();
            l.RuleFor(x => x.QuantityDelta).NotEqual(0);
            l.RuleFor(x => x.Condition).IsInEnum();
        });
    }
}

public interface IInventoryQueryService
{
    Task<PagedResult<StockRowDto>> StockAsync(string? search, bool lowOnly, int? page, int? pageSize, CancellationToken ct);
    Task<PagedResult<MovementDto>> MovementsAsync(Guid? bookId, DateOnly? from, DateOnly? to, MovementType? type, int? page, int? pageSize, CancellationToken ct);
    Task<IReadOnlyList<StockRowDto>> LowStockAsync(CancellationToken ct);
    Task<ValuationDto> ValuationAsync(CancellationToken ct);
    Task<StockAdjustmentDto> AdjustAsync(StockAdjustmentRequest request, CancellationToken ct);
    Task<PagedResult<StockAdjustmentDto>> AdjustmentsAsync(int? page, int? pageSize, CancellationToken ct);
}

public sealed class InventoryQueryService : IInventoryQueryService
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IInventoryService _inventory;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IClock _clock;
    private readonly IValidator<StockAdjustmentRequest> _validator;

    public InventoryQueryService(IAppDbContext db, ITenantContext tenant, IInventoryService inventory, IDocumentNumberGenerator numbers, IClock clock,
        IValidator<StockAdjustmentRequest> validator)
    {
        _db = db; _tenant = tenant; _inventory = inventory; _numbers = numbers; _clock = clock; _validator = validator;
    }

    private IQueryable<StockRowDto> StockQuery(string? search, bool lowOnly)
    {
        var books = _db.Books.AsNoTracking().Where(b => b.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            books = books.Where(b => b.Title.Contains(s) || (b.Isbn != null && b.Isbn.Contains(s)) || (b.Barcode != null && b.Barcode == s));
        }
        if (lowOnly)
            books = books.Where(b => (_db.StockBalances.Where(s => s.BookId == b.Id).Sum(s => (int?)s.QuantityOnHand) ?? 0) <= b.ReorderLevel);
        return books.OrderBy(b => b.Title).Select(b => new StockRowDto(
            b.Id, b.Title, b.Isbn, b.Barcode, b.Category != null ? b.Category.Name : null,
            _db.StockBalances.Where(s => s.BookId == b.Id).Sum(s => (int?)s.QuantityOnHand) ?? 0,
            _db.StockBalances.Where(s => s.BookId == b.Id).Sum(s => (int?)s.QuantityDamaged) ?? 0,
            b.ReorderLevel,
            _db.StockBalances.Where(s => s.BookId == b.Id).Select(s => (decimal?)s.AverageCost).FirstOrDefault() ?? b.PurchasePrice,
            b.SellingPrice,
            (_db.StockBalances.Where(s => s.BookId == b.Id).Sum(s => (int?)s.QuantityOnHand) ?? 0) *
            (_db.StockBalances.Where(s => s.BookId == b.Id).Select(s => (decimal?)s.AverageCost).FirstOrDefault() ?? b.PurchasePrice),
            (_db.StockBalances.Where(s => s.BookId == b.Id).Sum(s => (int?)s.QuantityOnHand) ?? 0) <= b.ReorderLevel));
    }

    public async Task<PagedResult<StockRowDto>> StockAsync(string? search, bool lowOnly, int? page, int? pageSize, CancellationToken ct)
    {
        return await StockQuery(search, lowOnly).ToPagedResultAsync(page, pageSize, ct);
    }

    public async Task<IReadOnlyList<StockRowDto>> LowStockAsync(CancellationToken ct) =>
        (await StockQuery(null, true).Take(500).ToListAsync(ct)).OrderBy(r => r.QuantityOnHand).ThenBy(r => r.Title).ToList();

    public async Task<PagedResult<MovementDto>> MovementsAsync(Guid? bookId, DateOnly? from, DateOnly? to, MovementType? type, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.InventoryMovements.AsNoTracking().AsQueryable();
        if (bookId.HasValue) q = q.Where(m => m.BookId == bookId);
        if (type.HasValue) q = q.Where(m => m.MovementType == type);
        if (from.HasValue) { var f = from.Value.ToDateTime(TimeOnly.MinValue); q = q.Where(m => m.OccurredAt >= f); }
        if (to.HasValue) { var t = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue); q = q.Where(m => m.OccurredAt < t); }
        var projected = from m in q
                        join b in _db.Books on m.BookId equals b.Id
                        orderby m.OccurredAt descending
                        select new MovementDto(m.Id, m.OccurredAt, m.BookId, b.Title, m.MovementType, m.Quantity, m.UnitCost, m.ReferenceType, m.ReferenceId, m.ReferenceNumber, m.Notes);
        return await projected.ToPagedResultAsync(page, pageSize, ct);
    }

    public async Task<ValuationDto> ValuationAsync(CancellationToken ct)
    {
        var rows = await (from sb in _db.StockBalances.AsNoTracking()
                          join b in _db.Books on sb.BookId equals b.Id
                          select new { sb.QuantityOnHand, sb.QuantityDamaged, sb.AverageCost, b.SellingPrice }).ToListAsync(ct);
        return new ValuationDto(
            rows.Count(r => r.QuantityOnHand > 0),
            rows.Sum(r => r.QuantityOnHand),
            rows.Sum(r => r.QuantityDamaged),
            Domain.Common.Money.Round2(rows.Sum(r => r.QuantityOnHand * r.AverageCost)),
            Domain.Common.Money.Round2(rows.Sum(r => r.QuantityOnHand * r.SellingPrice)));
    }

    public async Task<StockAdjustmentDto> AdjustAsync(StockAdjustmentRequest r, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAppAsync(r, ct);
        var storeId = _tenant.StoreId ?? (await _db.Stores.FirstOrDefaultAsync(s => s.IsDefault, ct))?.Id ?? throw new BusinessRuleException("no-store", "No default store.");
        var bookIds = r.Lines.Select(l => l.BookId).Distinct().ToList();
        var books = await _db.Books.Where(b => bookIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var missing = bookIds.Where(id => !books.ContainsKey(id)).ToList();
        if (missing.Count > 0) throw new NotFoundException("Book", missing[0]);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var number = await _numbers.NextAsync("ADJ", "ADJ", _clock.TodayIst, ct);
        var adj = new StockAdjustment
        {
            TenantId = _tenant.TenantId, StoreId = storeId, AdjustmentNumber = number, Reason = r.Reason.Trim(), Notes = r.Notes, AdjustedAt = _clock.UtcNow,
            Lines = r.Lines.Select(l => new StockAdjustmentLine { BookId = l.BookId, QuantityDelta = l.QuantityDelta, Condition = l.Condition }).ToList()
        };
        _db.StockAdjustments.Add(adj);
        await _db.SaveChangesAsync(ct);

        var reference = new MovementRef(MovementType.Adjustment, "StockAdjustment", adj.Id, number, r.Reason);
        foreach (var l in r.Lines)
        {
            var cost = books[l.BookId].PurchasePrice;
            if (l.Condition == ReturnCondition.Damaged)
            {
                if (l.QuantityDelta >= 0) throw new BusinessRuleException("invalid-damaged", "Damaged quantity must be negative (moves stock out of on-hand).");
                await _inventory.MarkDamagedAsync(storeId, l.BookId, -l.QuantityDelta, reference with { Type = MovementType.Damaged }, ct);
            }
            else if (l.QuantityDelta > 0)
                await _inventory.ReceiveAsync(storeId, new[] { new StockDelta(l.BookId, l.QuantityDelta, cost) }, reference, ct);
            else
                await _inventory.IssueAsync(storeId, new[] { new StockDelta(l.BookId, -l.QuantityDelta, cost) }, reference, ct);
        }
        await tx.CommitAsync(ct);
        return new StockAdjustmentDto(adj.Id, adj.AdjustmentNumber, adj.Reason, adj.AdjustedAt, adj.Notes, adj.Lines.Count);
    }

    public async Task<PagedResult<StockAdjustmentDto>> AdjustmentsAsync(int? page, int? pageSize, CancellationToken ct) =>
        await _db.StockAdjustments.AsNoTracking().OrderByDescending(a => a.AdjustedAt)
            .Select(a => new StockAdjustmentDto(a.Id, a.AdjustmentNumber, a.Reason, a.AdjustedAt, a.Notes, a.Lines.Count))
            .ToPagedResultAsync(page, pageSize, ct);
}
