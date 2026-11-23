using BookFlow.Domain.Common;
using BookFlow.Domain.Enums;

namespace BookFlow.Domain.Entities.Inventory;

public class StockBalance : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid StoreId { get; set; }
    public Guid BookId { get; set; }
    public int QuantityOnHand { get; set; }
    public int QuantityDamaged { get; set; }
    public decimal AverageCost { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Immutable ledger row. Positive quantity = stock in, negative = stock out.</summary>
public class InventoryMovement : Entity, ITenantEntity, IAppendOnly
{
    public Guid TenantId { get; set; }
    public Guid StoreId { get; set; }
    public Guid BookId { get; set; }
    public MovementType MovementType { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string ReferenceType { get; set; } = default!;
    public Guid ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? Notes { get; set; }
}

public class StockAdjustment : TenantEntity
{
    public string AdjustmentNumber { get; set; } = default!;
    public Guid StoreId { get; set; }
    public string Reason { get; set; } = default!;
    public DateTime AdjustedAt { get; set; }
    public string? Notes { get; set; }
    public List<StockAdjustmentLine> Lines { get; set; } = new();
}

public class StockAdjustmentLine : Entity
{
    public Guid StockAdjustmentId { get; set; }
    public Guid BookId { get; set; }
    public int QuantityDelta { get; set; }
    public ReturnCondition Condition { get; set; } = ReturnCondition.Resalable;
}
