using BookFlow.Domain.Entities.Identity;
using BookFlow.Domain.Enums;

namespace BookFlow.Application.Common.Interfaces;

public sealed record StockDelta(Guid BookId, int Quantity, decimal UnitCost);
public sealed record MovementRef(MovementType Type, string ReferenceType, Guid ReferenceId, string? ReferenceNumber, string? Notes = null);

/// <summary>All stock changes go through here. Must be called inside an open transaction on the DbContext.</summary>
public interface IInventoryService
{
    /// <summary>Adds stock (purchase, sales return, opening stock, positive adjustment).</summary>
    Task ReceiveAsync(Guid storeId, IReadOnlyList<StockDelta> deltas, MovementRef reference, CancellationToken ct);
    /// <summary>Removes stock with an atomic availability check. Throws InsufficientStockException.</summary>
    Task IssueAsync(Guid storeId, IReadOnlyList<StockDelta> deltas, MovementRef reference, CancellationToken ct);
    /// <summary>Moves stock between on-hand and damaged buckets.</summary>
    Task MarkDamagedAsync(Guid storeId, Guid bookId, int quantity, MovementRef reference, CancellationToken ct);
}

/// <summary>Per-tenant, per-financial-year document numbering. Must run inside the caller's transaction.</summary>
public interface IDocumentNumberGenerator
{
    Task<string> NextAsync(string kind, string prefix, DateOnly date, CancellationToken ct);
}

public interface ITokenService
{
    (string token, DateTime expiresAt) CreateAccessToken(User user, Tenant tenant);
    string CreateRefreshToken();
    string Hash(string value);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface IExcelService
{
    byte[] Export<T>(string sheetName, IEnumerable<T> rows);
    List<Dictionary<string, string?>> ReadFirstSheet(Stream stream, int maxRows);
    byte[] BuildTemplate(string sheetName, string[] headers, string[]? exampleRow = null);
}
