using BookFlow.Domain.Common;
using BookFlow.Domain.Enums;

namespace BookFlow.Domain.Entities.Audit;

public class AuditLog : ITenantEntity
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string EntityName { get; set; } = default!;
    public string EntityId { get; set; } = default!;
    public AuditAction Action { get; set; }
    public string? Changes { get; set; }
    public DateTime OccurredAt { get; set; }
}
