using System.Security.Claims;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Domain.Enums;
using BookFlow.Infrastructure.Identity;

namespace BookFlow.API.Auth;

/// <summary>Tenant identity comes ONLY from the validated JWT. Request bodies and query strings are never consulted.</summary>
public sealed class HttpTenantContext : ITenantContext
{
    private readonly ClaimsPrincipal? _user;

    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        _user = accessor.HttpContext?.User;
    }

    public bool IsAuthenticated => _user?.Identity?.IsAuthenticated == true && Guid.TryParse(_user.FindFirstValue(BookFlowClaims.TenantId), out _);
    public Guid TenantId => Guid.TryParse(_user?.FindFirstValue(BookFlowClaims.TenantId), out var id) ? id : Guid.Empty;
    public Guid UserId => Guid.TryParse(_user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? _user?.FindFirstValue("sub"), out var id) ? id : Guid.Empty;
    public UserRole Role => Enum.TryParse<UserRole>(_user?.FindFirstValue(ClaimTypes.Role), out var r) ? r : UserRole.BillingEmployee;
    public Guid? StoreId => Guid.TryParse(_user?.FindFirstValue(BookFlowClaims.StoreId), out var id) ? id : null;
    public string? Email => _user?.FindFirstValue(ClaimTypes.Email) ?? _user?.FindFirstValue("email");
}
