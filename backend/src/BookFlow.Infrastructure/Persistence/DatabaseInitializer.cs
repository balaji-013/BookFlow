using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookFlow.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies EF Core migrations. If the assembly contains no migrations yet (fresh checkout before
    /// `dotnet ef migrations add InitialCreate` has been run), falls back to EnsureCreated so the stack still boots.
    /// Production deployments must use migrations (DEPLOYMENT.md §3).
    /// </summary>
    public static async Task InitializeAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var migrations = db.Database.GetMigrations().ToList();
        if (migrations.Count > 0)
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            await db.Database.MigrateAsync(ct);
            logger.LogInformation("Database migrated. Applied {Count} pending migration(s): {Names}", pending.Count, string.Join(", ", pending));
        }
        else
        {
            var created = await db.Database.EnsureCreatedAsync(ct);
            logger.LogWarning("No EF Core migrations found in the assembly; used EnsureCreated (created={Created}). Generate migrations before production.", created);
        }
    }
}
