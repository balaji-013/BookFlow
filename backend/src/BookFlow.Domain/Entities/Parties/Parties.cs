using BookFlow.Domain.Common;
using BookFlow.Domain.Enums;

namespace BookFlow.Domain.Entities.Parties;

public class Customer : TenantEntity, IHasRowVersion
{
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Gstin { get; set; }
    public string? StateCode { get; set; }
    public CustomerType Type { get; set; } = CustomerType.Retail;
    public decimal CreditLimit { get; set; } // 0 = unlimited
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class Supplier : TenantEntity, IHasRowVersion
{
    public string Name { get; set; } = default!;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Gstin { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
