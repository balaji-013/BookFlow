using BookFlow.Domain.Enums;

namespace BookFlow.Application.Common.Interfaces;

public interface ITenantContext
{
    bool IsAuthenticated { get; }
    Guid TenantId { get; }
    Guid UserId { get; }
    UserRole Role { get; }
    Guid? StoreId { get; }
    string? Email { get; }
}

public interface IClock
{
    DateTime UtcNow { get; }
    /// <summary>Business date in Indian Standard Time.</summary>
    DateOnly TodayIst { get; }
    DateTime NowIst { get; }
}
