using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Common.Models;
using BookFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Application.Features.Audit;

public sealed record AuditLogDto(long Id, Guid? UserId, string? UserName, string EntityName, string EntityId, AuditAction Action, string? Changes, DateTime OccurredAt);

public interface IAuditQueryService
{
    Task<PagedResult<AuditLogDto>> ListAsync(string? entity, string? entityId, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct);
}

public sealed class AuditQueryService : IAuditQueryService
{
    private readonly IAppDbContext _db;
    public AuditQueryService(IAppDbContext db) => _db = db;

    public async Task<PagedResult<AuditLogDto>> ListAsync(string? entity, string? entityId, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entity)) q = q.Where(a => a.EntityName == entity);
        if (!string.IsNullOrWhiteSpace(entityId)) q = q.Where(a => a.EntityId == entityId);
        if (from.HasValue) { var f = from.Value.ToDateTime(TimeOnly.MinValue); q = q.Where(a => a.OccurredAt >= f); }
        if (to.HasValue) { var t = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue); q = q.Where(a => a.OccurredAt < t); }
        var projected = from a in q
                        join u in _db.Users on a.UserId equals (Guid?)u.Id into us
                        from u in us.DefaultIfEmpty()
                        orderby a.OccurredAt descending
                        select new AuditLogDto(a.Id, a.UserId, u != null ? u.FullName : null, a.EntityName, a.EntityId, a.Action, a.Changes, a.OccurredAt);
        return await projected.ToPagedResultAsync(page, pageSize, ct);
    }
}
