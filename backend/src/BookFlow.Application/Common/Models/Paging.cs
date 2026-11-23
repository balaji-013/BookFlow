using Microsoft.EntityFrameworkCore;

namespace BookFlow.Application.Common.Models;

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagingExtensions
{
    public const int MaxPageSize = 200;

    public static (int page, int pageSize) Normalize(int? page, int? pageSize)
    {
        var p = page.GetValueOrDefault(1) < 1 ? 1 : page.GetValueOrDefault(1);
        var s = pageSize.GetValueOrDefault(25);
        if (s < 1) s = 25;
        if (s > MaxPageSize) s = MaxPageSize;
        return (p, s);
    }

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, s) = Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * s).Take(s).ToListAsync(ct);
        return new PagedResult<T> { Items = items, Page = p, PageSize = s, TotalCount = total };
    }

    /// <summary>Pages the entity query in SQL, then maps in memory (for mappers EF cannot translate).</summary>
    public static async Task<PagedResult<TDest>> ToPagedResultAsync<TSource, TDest>(this IQueryable<TSource> query, int? page, int? pageSize,
        Func<TSource, TDest> map, CancellationToken ct)
    {
        var (p, s) = Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * s).Take(s).ToListAsync(ct);
        return new PagedResult<TDest> { Items = items.Select(map).ToList(), Page = p, PageSize = s, TotalCount = total };
    }
}
