using BookFlow.Application.Common.Interfaces;
using BookFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookFlow.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` tooling.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                 ?? "Server=localhost,1433;Database=BookFlow;User Id=sa;Password=Your_password123;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs, o => o.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)).Options;
        return new AppDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public bool IsAuthenticated => false;
        public Guid TenantId => Guid.Empty;
        public Guid UserId => Guid.Empty;
        public UserRole Role => UserRole.SuperAdmin;
        public Guid? StoreId => null;
        public string? Email => null;
    }
}
