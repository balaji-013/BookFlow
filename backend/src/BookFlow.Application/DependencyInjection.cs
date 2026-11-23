using BookFlow.Application.Features.Audit;
using BookFlow.Application.Features.Auth;
using BookFlow.Application.Features.Catalog;
using BookFlow.Application.Features.Finance;
using BookFlow.Application.Features.Inventory;
using BookFlow.Application.Features.Parties;
using BookFlow.Application.Features.Purchasing;
using BookFlow.Application.Features.Reports;
using BookFlow.Application.Features.Sales;
using BookFlow.Application.Features.Tenants;
using BookFlow.Application.Features.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BookFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateInvoiceValidator>(ServiceLifetime.Singleton);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IPartiesService, PartiesService>();
        services.AddScoped<IPurchasingService, PurchasingService>();
        services.AddScoped<IInventoryQueryService, InventoryQueryService>();
        services.AddScoped<ISalesService, SalesService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        return services;
    }
}
