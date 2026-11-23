using System.Text.Json;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Domain.Common;
using BookFlow.Domain.Entities.Audit;
using BookFlow.Domain.Entities.Identity;
using BookFlow.Domain.Enums;
using BookFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookFlow.Infrastructure.Persistence;

/// <summary>
/// Stamps TenantId / audit columns and refuses to persist an entity that belongs to another tenant.
/// Also writes AuditLogs rows (in the same SaveChanges, therefore the same transaction).
/// </summary>
public sealed class TenantAuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> SensitiveProperties = new(StringComparer.OrdinalIgnoreCase) { "PasswordHash", "TokenHash", "ReplacedByTokenHash", "RowVersion" };
    private static readonly HashSet<Type> NotAudited = new() { typeof(AuditLog), typeof(RefreshToken), typeof(Domain.Entities.Sales.IdempotencyKey), typeof(Domain.Entities.Sales.InvoiceSequence), typeof(Domain.Entities.Inventory.StockBalance) };

    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public TenantAuditSaveChangesInterceptor(ITenantContext tenant, IClock clock)
    {
        _tenant = tenant; _clock = clock;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;
        var now = _clock.UtcNow;
        var authenticated = _tenant.IsAuthenticated;
        var audits = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList())
        {
            if (entry.Entity is ITenantEntity tenantEntity)
            {
                if (entry.State == EntityState.Added && tenantEntity.TenantId == Guid.Empty)
                {
                    if (!authenticated) throw new ForbiddenException("Cannot create tenant data without an authenticated tenant.");
                    tenantEntity.TenantId = _tenant.TenantId;
                }
                if (authenticated && tenantEntity.TenantId != _tenant.TenantId)
                    throw new ForbiddenException("Cross-tenant write rejected.");
                if (!authenticated && tenantEntity.TenantId == Guid.Empty)
                    throw new ForbiddenException("Tenant data must carry a TenantId.");
            }

            if (entry.Entity is IAuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = auditable.CreatedAt == default ? now : auditable.CreatedAt;
                    auditable.CreatedBy ??= authenticated ? _tenant.UserId : null;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = authenticated ? _tenant.UserId : null;
                    entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                }
            }

            if (entry.Entity is IAppendOnly && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new BusinessRuleException("append-only", $"{entry.Metadata.ClrType.Name} rows are append-only and cannot be changed.");

            if (!NotAudited.Contains(entry.Entity.GetType()) && (entry.Entity is ITenantEntity || entry.Entity is Tenant))
                audits.Add(BuildAudit(entry, now));
        }

        if (audits.Count > 0) context.Set<AuditLog>().AddRange(audits);
    }

    private AuditLog BuildAudit(EntityEntry entry, DateTime now)
    {
        var tenantId = entry.Entity is ITenantEntity te ? te.TenantId : entry.Entity is Tenant t ? t.Id : Guid.Empty;
        var key = string.Join(",", entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue?.ToString()));
        object? changes = entry.State switch
        {
            EntityState.Added => entry.Properties.Where(p => !SensitiveProperties.Contains(p.Metadata.Name)).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue),
            EntityState.Deleted => entry.Properties.Where(p => !SensitiveProperties.Contains(p.Metadata.Name)).ToDictionary(p => p.Metadata.Name, p => p.OriginalValue),
            _ => entry.Properties.Where(p => p.IsModified && !SensitiveProperties.Contains(p.Metadata.Name) && !Equals(p.OriginalValue, p.CurrentValue))
                    .ToDictionary(p => p.Metadata.Name, p => (object?)new { Old = p.OriginalValue, New = p.CurrentValue })
        };
        return new AuditLog
        {
            TenantId = tenantId,
            UserId = _tenant.IsAuthenticated ? _tenant.UserId : null,
            EntityName = entry.Metadata.ClrType.Name,
            EntityId = key,
            Action = entry.State switch { EntityState.Added => AuditAction.Insert, EntityState.Deleted => AuditAction.Delete, _ => AuditAction.Update },
            Changes = JsonSerializer.Serialize(changes),
            OccurredAt = now
        };
    }
}
