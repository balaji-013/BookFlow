using BookFlow.Application.Common.Interfaces;
using BookFlow.Domain.Common;
using BookFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Infrastructure.Services;

/// <summary>
/// Gap-free, per-tenant, per-financial-year numbering: INV/2026-27/000001.
/// Takes an UPDLOCK/HOLDLOCK on the sequence row so concurrent invoices serialise only on this row,
/// inside the caller's transaction (so a rolled-back invoice releases its number).
/// </summary>
public sealed class DocumentNumberGenerator : IDocumentNumberGenerator
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public DocumentNumberGenerator(AppDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    public async Task<string> NextAsync(string kind, string prefix, DateOnly date, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Document numbers must be generated inside a transaction.");
        var tenantId = _tenant.TenantId;
        var fy = FinancialYear.Of(date);

        var rows = await _db.Database.SqlQuery<long>($@"
SELECT [NextNumber] AS [Value] FROM [InvoiceSequences] WITH (UPDLOCK, HOLDLOCK)
 WHERE [TenantId] = {tenantId} AND [FinancialYear] = {fy} AND [Kind] = {kind}").ToListAsync(ct);

        long next;
        if (rows.Count == 0)
        {
            next = 1;
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO [InvoiceSequences] ([TenantId], [FinancialYear], [Kind], [NextNumber]) VALUES ({tenantId}, {fy}, {kind}, 2)", ct);
        }
        else
        {
            next = rows[0];
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [InvoiceSequences] SET [NextNumber] = [NextNumber] + 1
 WHERE [TenantId] = {tenantId} AND [FinancialYear] = {fy} AND [Kind] = {kind}", ct);
        }
        return $"{prefix}/{fy}/{next:000000}";
    }
}
