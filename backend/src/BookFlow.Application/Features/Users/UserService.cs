using BookFlow.Application.Common.Extensions;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Common.Models;
using BookFlow.Application.Features.Auth;
using BookFlow.Domain.Entities.Identity;
using BookFlow.Domain.Enums;
using BookFlow.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Application.Features.Users;

public sealed record CreateUserRequest(string Email, string FullName, string Password, UserRole Role, string? Phone);
public sealed record UpdateUserRequest(string FullName, UserRole Role, string? Phone);
public sealed record ResetPasswordRequest(string NewPassword);
public sealed record UserDto(Guid Id, string Email, string FullName, UserRole Role, string? Phone, bool IsActive, DateTime? LastLoginAt, DateTime CreatedAt);

public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.Role).IsInEnum().NotEqual(UserRole.SuperAdmin);
        RuleFor(x => x.Phone).IndianPhone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Role).IsInEnum().NotEqual(UserRole.SuperAdmin);
    }
}

public interface IUserService
{
    Task<PagedResult<UserDto>> ListAsync(string? search, int? page, int? pageSize, CancellationToken ct);
    Task<UserDto> GetAsync(Guid id, CancellationToken ct);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct);
    Task SetActiveAsync(Guid id, bool active, CancellationToken ct);
    Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct);
}

public sealed class UserService : IUserService
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordService _passwords;
    private readonly IClock _clock;
    private readonly IValidator<CreateUserRequest> _createValidator;
    private readonly IValidator<UpdateUserRequest> _updateValidator;

    public UserService(IAppDbContext db, ITenantContext tenant, IPasswordService passwords, IClock clock,
        IValidator<CreateUserRequest> createValidator, IValidator<UpdateUserRequest> updateValidator)
    {
        _db = db; _tenant = tenant; _passwords = passwords; _clock = clock;
        _createValidator = createValidator; _updateValidator = updateValidator;
    }

    private static UserDto Map(User u) => new(u.Id, u.Email, u.FullName, u.Role, u.Phone, u.IsActive, u.LastLoginAt, u.CreatedAt);

    public async Task<PagedResult<UserDto>> ListAsync(string? search, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.Users.AsNoTracking().Where(u => u.Role != UserRole.SuperAdmin);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(u => u.FullName.Contains(search) || u.Email.Contains(search));
        return await q.OrderBy(u => u.FullName).ToPagedResultAsync(page, pageSize, Map, ct);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User", id);
        return Map(u);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAppAsync(request, ct);
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("email-taken", "An account with this email already exists.");
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.IsDefault, ct);
        var user = new User
        {
            TenantId = _tenant.TenantId,
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = _passwords.Hash(request.Password),
            Role = request.Role,
            Phone = request.Phone,
            StoreId = store?.Id,
            CreatedAt = _clock.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAppAsync(request, ct);
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User", id);
        if (user.Id == _tenant.UserId && request.Role != user.Role)
            throw new BusinessRuleException("self-role-change", "You cannot change your own role.");
        user.FullName = request.FullName.Trim();
        user.Role = request.Role;
        user.Phone = request.Phone;
        await _db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User", id);
        if (user.Id == _tenant.UserId && !active)
            throw new BusinessRuleException("self-deactivate", "You cannot deactivate your own account.");
        user.IsActive = active;
        if (!active)
            await _db.RefreshTokens.Where(r => r.UserId == user.Id && r.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, _clock.UtcNow), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            throw new AppValidationException(nameof(request.NewPassword), "Password must be at least 8 characters.");
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User", id);
        user.PasswordHash = _passwords.Hash(request.NewPassword);
        await _db.RefreshTokens.Where(r => r.UserId == user.Id && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, _clock.UtcNow), ct);
        await _db.SaveChangesAsync(ct);
    }
}
