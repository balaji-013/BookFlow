using BookFlow.Application.Common.Interfaces;
using BookFlow.Domain.Entities.Inventory;
using BookFlow.Domain.Exceptions;
using BookFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Infrastructure.Services;

/// <summary>
/// Stock changes with database-enforced invariants (ADR-0010). Every call appends immutable ledger rows.
/// Callers own the transaction; this class never commits.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public InventoryService(AppDbContext db, ITenantContext tenant, IClock clock)
    {
        _db = db; _tenant = tenant; _clock = clock;
    }

    public async Task ReceiveAsync(Guid storeId, IReadOnlyList<StockDelta> deltas, MovementRef reference, CancellationToken ct)
    {
        EnsureTransaction();
        var tenantId = _tenant.TenantId;
        foreach (var d in deltas)
        {
            if (d.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(d.Quantity), "Receive quantity must be positive.");

            // Upsert balance with weighted average cost. MERGE is avoided (concurrency quirks); use update-then-insert.
            var updated = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [StockBalances]
   SET [AverageCost] = CASE WHEN [QuantityOnHand] + {d.Quantity} = 0 THEN {d.UnitCost}
                            ELSE (([QuantityOnHand] * [AverageCost]) + ({d.Quantity} * {d.UnitCost})) / ([QuantityOnHand] + {d.Quantity}) END,
       [QuantityOnHand] = [QuantityOnHand] + {d.Quantity},
       [UpdatedAt] = {_clock.UtcNow}
 WHERE [TenantId] = {tenantId} AND [StoreId] = {storeId} AND [BookId] = {d.BookId}", ct);

            if (updated == 0)
            {
                _db.StockBalances.Add(new StockBalance
                {
                    TenantId = tenantId, StoreId = storeId, BookId = d.BookId, QuantityOnHand = d.Quantity, QuantityDamaged = 0,
                    AverageCost = d.UnitCost, UpdatedAt = _clock.UtcNow
                });
            }
            _db.InventoryMovements.Add(Movement(tenantId, storeId, d.BookId, d.Quantity, d.UnitCost, reference));
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task IssueAsync(Guid storeId, IReadOnlyList<StockDelta> deltas, MovementRef reference, CancellationToken ct)
    {
        EnsureTransaction();
        var tenantId = _tenant.TenantId;
        var failed = new List<Guid>();
        foreach (var d in deltas)
        {
            if (d.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(d.Quantity), "Issue quantity must be positive.");

            // Atomic conditional decrement: the WHERE clause is the stock check.
            var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [StockBalances]
   SET [QuantityOnHand] = [QuantityOnHand] - {d.Quantity}, [UpdatedAt] = {_clock.UtcNow}
 WHERE [TenantId] = {tenantId} AND [StoreId] = {storeId} AND [BookId] = {d.BookId} AND [QuantityOnHand] >= {d.Quantity}", ct);

            if (affected == 0) { failed.Add(d.BookId); continue; }
            _db.InventoryMovements.Add(Movement(tenantId, storeId, d.BookId, -d.Quantity, d.UnitCost, reference));
        }
        if (failed.Count > 0) throw new InsufficientStockException(failed);
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkDamagedAsync(Guid storeId, Guid bookId, int quantity, MovementRef reference, CancellationToken ct)
    {
        EnsureTransaction();
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var tenantId = _tenant.TenantId;
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [StockBalances]
   SET [QuantityOnHand] = [QuantityOnHand] - {quantity}, [QuantityDamaged] = [QuantityDamaged] + {quantity}, [UpdatedAt] = {_clock.UtcNow}
 WHERE [TenantId] = {tenantId} AND [StoreId] = {storeId} AND [BookId] = {bookId} AND [QuantityOnHand] >= {quantity}", ct);
        if (affected == 0) throw new InsufficientStockException(new[] { bookId });
        var cost = await _db.StockBalances.AsNoTracking().Where(s => s.BookId == bookId && s.StoreId == storeId).Select(s => s.AverageCost).FirstOrDefaultAsync(ct);
        _db.InventoryMovements.Add(Movement(tenantId, storeId, bookId, -quantity, cost, reference));
        await _db.SaveChangesAsync(ct);
    }

    private InventoryMovement Movement(Guid tenantId, Guid storeId, Guid bookId, int qty, decimal cost, MovementRef r) => new()
    {
        TenantId = tenantId, StoreId = storeId, BookId = bookId, MovementType = r.Type, Quantity = qty, UnitCost = cost,
        ReferenceType = r.ReferenceType, ReferenceId = r.ReferenceId, ReferenceNumber = r.ReferenceNumber, Notes = r.Notes,
        OccurredAt = _clock.UtcNow, CreatedBy = _tenant.IsAuthenticated ? _tenant.UserId : null
    };

    private void EnsureTransaction()
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Inventory operations must run inside a transaction owned by the calling use case.");
    }
}
