using BookFlow.Domain.Common;
using BookFlow.Domain.Enums;

namespace BookFlow.Domain.Entities.Identity;

public class Tenant : Entity, IAuditableEntity
{
    public string BusinessName { get; set; } = default!;
    public string? LegalName { get; set; }
    public string? Gstin { get; set; }
    /// <summary>Two-digit GST state code, e.g. "33" for Tamil Nadu.</summary>
    public string StateCode { get; set; } = "33";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string State { get; set; } = "Tamil Nadu";
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string Email { get; set; } = default!;
    public string? InvoiceFooter { get; set; }
    public string InvoicePrefix { get; set; } = "INV";
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public string SubscriptionPlan { get; set; } = "Pilot";
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class Store : TenantEntity
{
    public string Name { get; set; } = default!;
    public string Code { get; set; } = "MAIN";
    public bool IsDefault { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public class User : TenantEntity, IHasRowVersion
{
    public string Email { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? StoreId { get; set; }
    public string? Phone { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
