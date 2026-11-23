using BookFlow.Application.Common.Interfaces;
using BookFlow.Infrastructure.Identity;
using BookFlow.Infrastructure.Persistence;
using BookFlow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<TenantAuditSaveChangesInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.CommandTimeout(30);
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                // Connection resiliency deliberately off: services use explicit transactions (SYSTEM.md §4).
            });
            options.AddInterceptors(sp.GetRequiredService<TenantAuditSaveChangesInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IExcelService, ExcelService>();

        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();
        return services;
    }
}
