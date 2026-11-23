using BookFlow.Application.Common.Extensions;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Application.Features.Tenants;

public sealed record TenantDto(Guid Id, string BusinessName, string? LegalName, string? Gstin, string StateCode, string? AddressLine1,
    string? AddressLine2, string? City, string State, string? Pincode, string? Phone, string Email, string? InvoiceFooter, string InvoicePrefix,
    string SubscriptionPlan, DateTime CreatedAt);

public sealed record UpdateTenantRequest(string BusinessName, string? LegalName, string? Gstin, string StateCode, string? AddressLine1,
    string? AddressLine2, string? City, string State, string? Pincode, string? Phone, string? InvoiceFooter, string InvoicePrefix);

public sealed record StoreDto(Guid Id, string Name, string Code, bool IsDefault, string? Address);

public sealed class UpdateTenantValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.StateCode).Matches("^[0-9]{2}$");
        RuleFor(x => x.Gstin).Gstin().When(x => !string.IsNullOrWhiteSpace(x.Gstin));
        RuleFor(x => x.Phone).IndianPhone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.InvoicePrefix).NotEmpty().MaximumLength(6).Matches("^[A-Z0-9]+$");
        RuleFor(x => x.Pincode).Matches("^[0-9]{6}$").When(x => !string.IsNullOrWhiteSpace(x.Pincode));
    }
}

public interface ITenantService
{
    Task<TenantDto> GetAsync(CancellationToken ct);
    Task<TenantDto> UpdateAsync(UpdateTenantRequest request, CancellationToken ct);
    Task<IReadOnlyList<StoreDto>> ListStoresAsync(CancellationToken ct);
}

public sealed class TenantService : ITenantService
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IValidator<UpdateTenantRequest> _validator;

    public TenantService(IAppDbContext db, ITenantContext tenant, IValidator<UpdateTenantRequest> validator)
    {
        _db = db; _tenant = tenant; _validator = validator;
    }

    private static TenantDto Map(Domain.Entities.Identity.Tenant t) => new(t.Id, t.BusinessName, t.LegalName, t.Gstin, t.StateCode, t.AddressLine1,
        t.AddressLine2, t.City, t.State, t.Pincode, t.Phone, t.Email, t.InvoiceFooter, t.InvoicePrefix, t.SubscriptionPlan, t.CreatedAt);

    public async Task<TenantDto> GetAsync(CancellationToken ct)
    {
        var t = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == _tenant.TenantId, ct) ?? throw new NotFoundException("Tenant", _tenant.TenantId);
        return Map(t);
    }

    public async Task<TenantDto> UpdateAsync(UpdateTenantRequest r, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAppAsync(r, ct);
        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == _tenant.TenantId, ct) ?? throw new NotFoundException("Tenant", _tenant.TenantId);
        t.BusinessName = r.BusinessName.Trim(); t.LegalName = r.LegalName; t.Gstin = r.Gstin?.ToUpperInvariant(); t.StateCode = r.StateCode;
        t.AddressLine1 = r.AddressLine1; t.AddressLine2 = r.AddressLine2; t.City = r.City; t.State = r.State; t.Pincode = r.Pincode;
        t.Phone = r.Phone; t.InvoiceFooter = r.InvoiceFooter; t.InvoicePrefix = r.InvoicePrefix;
        await _db.SaveChangesAsync(ct);
        return Map(t);
    }

    public async Task<IReadOnlyList<StoreDto>> ListStoresAsync(CancellationToken ct) =>
        await _db.Stores.AsNoTracking().Where(s => s.IsActive).OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name)
            .Select(s => new StoreDto(s.Id, s.Name, s.Code, s.IsDefault, s.Address)).ToListAsync(ct);
}
