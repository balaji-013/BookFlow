using BookFlow.Application.Common.Extensions;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Common.Models;
using BookFlow.Domain.Entities.Identity;
using BookFlow.Domain.Enums;
using BookFlow.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookFlow.Application.Features.Auth;

public sealed record RegisterTenantRequest(
    string BusinessName, string OwnerName, string Email, string Password, string? Phone,
    string? Gstin, string? City, string? StateCode);

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UserSummaryDto(Guid Id, string Email, string FullName, UserRole Role, Guid? StoreId, bool IsActive);
public sealed record TenantSummaryDto(Guid Id, string BusinessName, string? Gstin, string StateCode, string InvoicePrefix, string? InvoiceFooter);
public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken, UserSummaryDto User, TenantSummaryDto Tenant);

public sealed class RegisterTenantValidator : AbstractValidator<RegisterTenantRequest>
{
    public RegisterTenantValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OwnerName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.Phone).IndianPhone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Gstin).Gstin().When(x => !string.IsNullOrWhiteSpace(x.Gstin));
        RuleFor(x => x.StateCode).Matches("^[0-9]{2}$").When(x => !string.IsNullOrWhiteSpace(x.StateCode));
    }
}

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}

public interface IAuthService
{
    Task<AuthResponse> RegisterTenantAsync(RegisterTenantRequest request, string? ip, CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ip, CancellationToken ct);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ip, CancellationToken ct);
    Task LogoutAsync(string refreshToken, CancellationToken ct);
    Task<AuthResponse> MeAsync(CancellationToken ct);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct);
}

public sealed class AuthService : IAuthService
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IPasswordService _passwords;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly ILogger<AuthService> _log;
    private readonly IValidator<RegisterTenantRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;

    public AuthService(IAppDbContext db, ITokenService tokens, IPasswordService passwords, ITenantContext tenant, IClock clock,
        ILogger<AuthService> log, IValidator<RegisterTenantRequest> registerValidator, IValidator<LoginRequest> loginValidator,
        IValidator<ChangePasswordRequest> changePasswordValidator)
    {
        _db = db; _tokens = tokens; _passwords = passwords; _tenant = tenant; _clock = clock; _log = log;
        _registerValidator = registerValidator; _loginValidator = loginValidator; _changePasswordValidator = changePasswordValidator;
    }

    public async Task<AuthResponse> RegisterTenantAsync(RegisterTenantRequest request, string? ip, CancellationToken ct)
    {
        await _registerValidator.ValidateAndThrowAppAsync(request, ct);
        var email = request.Email.Trim().ToLowerInvariant();

        // Emails are globally unique across tenants so login does not need a tenant selector.
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("email-taken", "An account with this email already exists.");

        var tenant = new Tenant
        {
            BusinessName = request.BusinessName.Trim(),
            Email = email,
            Phone = request.Phone,
            Gstin = request.Gstin?.ToUpperInvariant(),
            City = request.City,
            StateCode = string.IsNullOrWhiteSpace(request.StateCode) ? (request.Gstin?.Length >= 2 ? request.Gstin[..2] : "33") : request.StateCode!,
            CreatedAt = _clock.UtcNow
        };
        var store = new Store { TenantId = tenant.Id, Name = tenant.BusinessName, Code = "MAIN", IsDefault = true, CreatedAt = _clock.UtcNow };
        var owner = new User
        {
            TenantId = tenant.Id,
            Email = email,
            FullName = request.OwnerName.Trim(),
            PasswordHash = _passwords.Hash(request.Password),
            Role = UserRole.Owner,
            StoreId = store.Id,
            Phone = request.Phone,
            CreatedAt = _clock.UtcNow
        };

        _db.Tenants.Add(tenant);
        _db.Stores.Add(store);
        _db.Users.Add(owner);
        var refresh = IssueRefreshToken(owner, ip);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Tenant {TenantId} registered by {Email}", tenant.Id, email);
        return BuildResponse(owner, tenant, refresh);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ip, CancellationToken ct)
    {
        await _loginValidator.ValidateAndThrowAppAsync(request, ct);
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !_passwords.Verify(user.PasswordHash, request.Password))
        {
            _log.LogWarning("Failed login for {Email}", email);
            throw new ForbiddenException("Invalid email or password.");
        }
        if (!user.IsActive) throw new ForbiddenException("This account is deactivated.");

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == user.TenantId, ct);
        if (tenant.Status != TenantStatus.Active) throw new ForbiddenException("This business account is suspended.");

        user.LastLoginAt = _clock.UtcNow;
        var refresh = IssueRefreshToken(user, ip);
        await _db.SaveChangesAsync(ct);
        return BuildResponse(user, tenant, refresh);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) throw new ForbiddenException("Refresh token is required.");
        var hash = _tokens.Hash(request.RefreshToken);
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct)
                       ?? throw new ForbiddenException("Invalid refresh token.");

        if (existing.RevokedAt is not null)
        {
            // Reuse detected: revoke the whole family for this user.
            _log.LogWarning("Refresh token reuse detected for user {UserId}", existing.UserId);
            await _db.RefreshTokens.Where(r => r.UserId == existing.UserId && r.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, _clock.UtcNow), ct);
            throw new ForbiddenException("Refresh token has been revoked.");
        }
        if (existing.ExpiresAt <= _clock.UtcNow) throw new ForbiddenException("Refresh token expired.");

        var user = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == existing.UserId, ct);
        if (!user.IsActive) throw new ForbiddenException("This account is deactivated.");
        var tenant = await _db.Tenants.FirstAsync(t => t.Id == user.TenantId, ct);

        var newToken = IssueRefreshToken(user, ip);
        existing.RevokedAt = _clock.UtcNow;
        existing.ReplacedByTokenHash = _tokens.Hash(newToken);
        await _db.SaveChangesAsync(ct);
        return BuildResponse(user, tenant, newToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = _tokens.Hash(refreshToken);
        await _db.RefreshTokens.Where(r => r.TokenHash == hash && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, _clock.UtcNow), ct);
    }

    public async Task<AuthResponse> MeAsync(CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == _tenant.UserId, ct) ?? throw new NotFoundException("User", _tenant.UserId);
        var tenant = await _db.Tenants.FirstAsync(t => t.Id == _tenant.TenantId, ct);
        return BuildResponse(user, tenant, string.Empty);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        await _changePasswordValidator.ValidateAndThrowAppAsync(request, ct);
        var user = await _db.Users.FirstAsync(u => u.Id == _tenant.UserId, ct);
        if (!_passwords.Verify(user.PasswordHash, request.CurrentPassword))
            throw new AppValidationException(nameof(request.CurrentPassword), "Current password is incorrect.");
        user.PasswordHash = _passwords.Hash(request.NewPassword);
        await _db.RefreshTokens.Where(r => r.UserId == user.Id && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, _clock.UtcNow), ct);
        await _db.SaveChangesAsync(ct);
    }

    private string IssueRefreshToken(User user, string? ip)
    {
        var raw = _tokens.CreateRefreshToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokens.Hash(raw),
            CreatedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.Add(RefreshTokenLifetime),
            CreatedByIp = ip
        });
        return raw;
    }

    private AuthResponse BuildResponse(User user, Tenant tenant, string refreshToken)
    {
        var (access, expires) = _tokens.CreateAccessToken(user, tenant);
        return new AuthResponse(access, expires, refreshToken,
            new UserSummaryDto(user.Id, user.Email, user.FullName, user.Role, user.StoreId, user.IsActive),
            new TenantSummaryDto(tenant.Id, tenant.BusinessName, tenant.Gstin, tenant.StateCode, tenant.InvoicePrefix, tenant.InvoiceFooter));
    }
}
