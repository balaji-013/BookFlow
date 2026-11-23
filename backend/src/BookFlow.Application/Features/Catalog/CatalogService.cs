using System.Globalization;
using BookFlow.Application.Common.Extensions;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Common.Models;
using BookFlow.Domain.Entities.Catalog;
using BookFlow.Domain.Enums;
using BookFlow.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Application.Features.Catalog;

public interface ICatalogService
{
    Task<PagedResult<BookDto>> ListBooksAsync(string? search, Guid? categoryId, bool? lowStock, bool includeInactive, int? page, int? pageSize, CancellationToken ct);
    Task<BookDto> GetBookAsync(Guid id, CancellationToken ct);
    Task<BookLookupDto?> LookupAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<BookLookupDto>> QuickSearchAsync(string term, int take, CancellationToken ct);
    Task<BookDto> CreateBookAsync(SaveBookRequest request, CancellationToken ct);
    Task<BookDto> UpdateBookAsync(Guid id, SaveBookRequest request, CancellationToken ct);
    Task DeactivateBookAsync(Guid id, CancellationToken ct);
    Task<ImportResult> ImportBooksAsync(Stream xlsx, bool overwriteStock, CancellationToken ct);
    byte[] ImportTemplate();

    Task<IReadOnlyList<NamedDto>> ListAuthorsAsync(string? search, CancellationToken ct);
    Task<NamedDto> SaveAuthorAsync(Guid? id, SaveNamedRequest request, CancellationToken ct);
    Task DeleteAuthorAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<NamedDto>> ListPublishersAsync(string? search, CancellationToken ct);
    Task<NamedDto> SavePublisherAsync(Guid? id, SaveNamedRequest request, CancellationToken ct);
    Task DeletePublisherAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<NamedDto>> ListCategoriesAsync(string? search, CancellationToken ct);
    Task<NamedDto> SaveCategoryAsync(Guid? id, SaveNamedRequest request, CancellationToken ct);
    Task DeleteCategoryAsync(Guid id, CancellationToken ct);
}

public sealed class CatalogService : ICatalogService
{
    public static readonly string[] ImportHeaders =
    {
        "Title", "ISBN", "Barcode", "Author", "Publisher", "Category", "Edition", "Language",
        "PurchasePrice", "SellingPrice", "MRP", "HSN", "GstRate", "OpeningStock", "ReorderLevel"
    };
    private const int MaxImportRows = 5000;

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IInventoryService _inventory;
    private readonly IExcelService _excel;
    private readonly IClock _clock;
    private readonly IValidator<SaveBookRequest> _bookValidator;
    private readonly IValidator<SaveNamedRequest> _namedValidator;

    public CatalogService(IAppDbContext db, ITenantContext tenant, IInventoryService inventory, IExcelService excel, IClock clock,
        IValidator<SaveBookRequest> bookValidator, IValidator<SaveNamedRequest> namedValidator)
    {
        _db = db; _tenant = tenant; _inventory = inventory; _excel = excel; _clock = clock;
        _bookValidator = bookValidator; _namedValidator = namedValidator;
    }

    // ---------- queries ----------

    private IQueryable<BookDto> ProjectBooks(IQueryable<Book> books)
    {
        return books.Select(b => new BookDto(
            b.Id, b.Isbn, b.Barcode, b.Title, b.Subtitle, b.AuthorId, b.Author != null ? b.Author.Name : null,
            b.PublisherId, b.Publisher != null ? b.Publisher.Name : null, b.CategoryId, b.Category != null ? b.Category.Name : null,
            b.Edition, b.Language, b.Pages, b.PurchasePrice, b.SellingPrice, b.Mrp, b.HsnCode, b.GstRate, b.ReorderLevel, b.IsActive,
            _db.StockBalances.Where(s => s.BookId == b.Id).Sum(s => (int?)s.QuantityOnHand) ?? 0, b.RowVersion));
    }

    public async Task<PagedResult<BookDto>> ListBooksAsync(string? search, Guid? categoryId, bool? lowStock, bool includeInactive, int? page, int? pageSize, CancellationToken ct)
    {
        var q = _db.Books.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(b => b.IsActive);
        if (categoryId.HasValue) q = q.Where(b => b.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(b => b.Title.Contains(s) || (b.Isbn != null && b.Isbn.Contains(s)) || (b.Barcode != null && b.Barcode == s)
                             || (b.Author != null && b.Author.Name.Contains(s)));
        }
        if (lowStock == true)
            q = q.Where(b => (_db.StockBalances.Where(s => s.BookId == b.Id).Sum(s => (int?)s.QuantityOnHand) ?? 0) <= b.ReorderLevel);
        return await ProjectBooks(q.OrderBy(b => b.Title)).ToPagedResultAsync(page, pageSize, ct);
    }

    public async Task<BookDto> GetBookAsync(Guid id, CancellationToken ct) =>
        await ProjectBooks(_db.Books.AsNoTracking().Where(b => b.Id == id)).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Book", id);

    public async Task<BookLookupDto?> LookupAsync(string code, CancellationToken ct)
    {
        var c = code.Trim();
        var normalizedIsbn = c.Replace("-", "");
        var q = _db.Books.AsNoTracking().Where(b => b.IsActive && (b.Barcode == c || b.Isbn == normalizedIsbn || b.Isbn == c));
        return await ProjectLookup(q).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<BookLookupDto>> QuickSearchAsync(string term, int take, CancellationToken ct)
    {
        var s = term.Trim();
        if (s.Length == 0) return Array.Empty<BookLookupDto>();
        take = Math.Clamp(take, 1, 50);
        var q = _db.Books.AsNoTracking().Where(b => b.IsActive &&
            (b.Title.Contains(s) || (b.Isbn != null && b.Isbn.StartsWith(s)) || (b.Barcode != null && b.Barcode.StartsWith(s))
             || (b.Author != null && b.Author.Name.Contains(s))));
        return await ProjectLookup(q).OrderBy(b => b.Title).Take(take).ToListAsync(ct);
    }

    private IQueryable<BookLookupDto> ProjectLookup(IQueryable<Book> books) =>
        books.Select(b => new BookLookupDto(b.Id, b.Title, b.Isbn, b.Barcode, b.Author != null ? b.Author.Name : null, b.SellingPrice, b.Mrp, b.HsnCode, b.GstRate,
            _db.StockBalances.Where(s => s.BookId == b.Id).Sum(s => (int?)s.QuantityOnHand) ?? 0));

    // ---------- commands ----------

    public async Task<BookDto> CreateBookAsync(SaveBookRequest r, CancellationToken ct)
    {
        await _bookValidator.ValidateAndThrowAppAsync(r, ct);
        var book = new Book { TenantId = _tenant.TenantId };
        await ApplyAsync(book, r, ct);
        await EnsureUniqueAsync(book, ct);
        _db.Books.Add(book);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.SaveChangesAsync(ct);
        if (r.OpeningStock is > 0)
        {
            var store = await DefaultStoreIdAsync(ct);
            await _inventory.ReceiveAsync(store, new[] { new StockDelta(book.Id, r.OpeningStock.Value, book.PurchasePrice) },
                new MovementRef(MovementType.OpeningStock, "Book", book.Id, null, "Opening stock on book creation"), ct);
        }
        await tx.CommitAsync(ct);
        return await GetBookAsync(book.Id, ct);
    }

    public async Task<BookDto> UpdateBookAsync(Guid id, SaveBookRequest r, CancellationToken ct)
    {
        await _bookValidator.ValidateAndThrowAppAsync(r, ct);
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Book", id);
        if (r.RowVersion is { Length: > 0 } && !r.RowVersion.SequenceEqual(book.RowVersion))
            throw new ConflictException("concurrency", "This book was modified by someone else. Reload and try again.");
        await ApplyAsync(book, r, ct);
        await EnsureUniqueAsync(book, ct);
        await _db.SaveChangesAsync(ct);
        return await GetBookAsync(book.Id, ct);
    }

    public async Task DeactivateBookAsync(Guid id, CancellationToken ct)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Book", id);
        book.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Book b, SaveBookRequest r, CancellationToken ct)
    {
        b.Isbn = string.IsNullOrWhiteSpace(r.Isbn) ? null : r.Isbn.Replace("-", "").Trim().ToUpperInvariant();
        b.Barcode = string.IsNullOrWhiteSpace(r.Barcode) ? null : r.Barcode.Trim();
        b.Title = r.Title.Trim();
        b.Subtitle = r.Subtitle?.Trim();
        b.Edition = r.Edition?.Trim();
        b.Language = r.Language.Trim();
        b.Pages = r.Pages;
        b.PurchasePrice = r.PurchasePrice;
        b.SellingPrice = r.SellingPrice;
        b.Mrp = r.Mrp == 0 ? r.SellingPrice : r.Mrp;
        b.HsnCode = r.HsnCode.Trim();
        b.GstRate = r.GstRate;
        b.ReorderLevel = r.ReorderLevel;
        b.IsActive = r.IsActive;
        b.AuthorId = r.AuthorId ?? (string.IsNullOrWhiteSpace(r.AuthorName) ? null : (await GetOrCreateAuthorAsync(r.AuthorName!, ct)).Id);
        b.PublisherId = r.PublisherId ?? (string.IsNullOrWhiteSpace(r.PublisherName) ? null : (await GetOrCreatePublisherAsync(r.PublisherName!, ct)).Id);
        b.CategoryId = r.CategoryId ?? (string.IsNullOrWhiteSpace(r.CategoryName) ? null : (await GetOrCreateCategoryAsync(r.CategoryName!, ct)).Id);
    }

    private async Task EnsureUniqueAsync(Book b, CancellationToken ct)
    {
        if (b.Isbn is not null && await _db.Books.AnyAsync(x => x.Id != b.Id && x.Isbn == b.Isbn, ct))
            throw new ConflictException("duplicate-isbn", $"Another book already uses ISBN {b.Isbn}.");
        if (b.Barcode is not null && await _db.Books.AnyAsync(x => x.Id != b.Id && x.Barcode == b.Barcode, ct))
            throw new ConflictException("duplicate-barcode", $"Another book already uses barcode {b.Barcode}.");
        if (b.Isbn is null && b.Barcode is null &&
            await _db.Books.AnyAsync(x => x.Id != b.Id && x.Title == b.Title && x.Edition == b.Edition && x.AuthorId == b.AuthorId, ct))
            throw new ConflictException("duplicate-title", "A book with the same title, author and edition already exists. Add an ISBN or edition to distinguish it.");
    }

    private async Task<Guid> DefaultStoreIdAsync(CancellationToken ct)
    {
        if (_tenant.StoreId.HasValue) return _tenant.StoreId.Value;
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.IsDefault, ct) ?? throw new BusinessRuleException("no-store", "No default store configured.");
        return store.Id;
    }

    // ---------- import ----------

    public byte[] ImportTemplate() => _excel.BuildTemplate("Books", ImportHeaders,
        new[] { "Ponniyin Selvan Part 1", "9788184934052", "", "Kalki", "Vanathi Pathippagam", "Tamil Novel", "Reprint 2023", "Tamil", "180", "250", "250", "4901", "0", "10", "3" });

    public async Task<ImportResult> ImportBooksAsync(Stream xlsx, bool overwriteStock, CancellationToken ct)
    {
        var rows = _excel.ReadFirstSheet(xlsx, MaxImportRows + 1);
        if (rows.Count > MaxImportRows) throw new BusinessRuleException("import-too-large", $"Import at most {MaxImportRows} rows per file.");
        var result = new ImportResult(0, 0, 0, new List<string>());
        int created = 0, updated = 0, skipped = 0;
        var storeId = await DefaultStoreIdAsync(ct);

        // Cache lookups for this tenant to avoid N queries.
        var authors = await _db.Authors.ToDictionaryAsync(a => a.Name.ToLowerInvariant(), ct);
        var publishers = await _db.Publishers.ToDictionaryAsync(p => p.Name.ToLowerInvariant(), ct);
        var categories = await _db.Categories.ToDictionaryAsync(c => c.Name.ToLowerInvariant(), ct);
        var existing = await _db.Books.ToListAsync(ct);
        var byIsbn = existing.Where(b => b.Isbn != null).ToDictionary(b => b.Isbn!, b => b);
        var byBarcode = existing.Where(b => b.Barcode != null).ToDictionary(b => b.Barcode!, b => b);

        var openingDeltas = new List<(Book book, int qty, bool isNew)>();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 2;
            try
            {
                string Get(string h) => row.TryGetValue(h, out var v) ? (v ?? string.Empty).Trim() : string.Empty;
                decimal Dec(string h, decimal def = 0) => decimal.TryParse(Get(h), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : def;
                int Int(string h, int def = 0) => int.TryParse(Get(h), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : def;

                var title = Get("Title");
                if (title.Length == 0) { skipped++; result.Errors.Add($"Row {line}: Title is empty, skipped."); continue; }
                var isbn = Get("ISBN").Replace("-", "").ToUpperInvariant();
                var barcode = Get("Barcode");
                Book? book = null;
                if (isbn.Length > 0) byIsbn.TryGetValue(isbn, out book);
                if (book is null && barcode.Length > 0) byBarcode.TryGetValue(barcode, out book);
                var isNew = book is null;
                book ??= new Book { TenantId = _tenant.TenantId };

                book.Title = title;
                book.Isbn = isbn.Length > 0 ? isbn : book.Isbn;
                book.Barcode = barcode.Length > 0 ? barcode : book.Barcode;
                book.Edition = Get("Edition") is { Length: > 0 } ed ? ed : book.Edition;
                book.Language = Get("Language") is { Length: > 0 } lang ? lang : (book.Language ?? "Tamil");
                book.PurchasePrice = Dec("PurchasePrice", book.PurchasePrice);
                book.SellingPrice = Dec("SellingPrice", book.SellingPrice);
                book.Mrp = Dec("MRP", book.Mrp == 0 ? book.SellingPrice : book.Mrp);
                book.HsnCode = Get("HSN") is { Length: > 0 } hsn ? hsn : (book.HsnCode ?? "4901");
                book.GstRate = Dec("GstRate", book.GstRate);
                book.ReorderLevel = Int("ReorderLevel", book.ReorderLevel == 0 ? 2 : book.ReorderLevel);
                book.IsActive = true;

                var authorName = Get("Author");
                if (authorName.Length > 0)
                {
                    if (!authors.TryGetValue(authorName.ToLowerInvariant(), out var a)) { a = new Author { TenantId = _tenant.TenantId, Name = authorName }; _db.Authors.Add(a); authors[authorName.ToLowerInvariant()] = a; }
                    book.AuthorId = a.Id;
                }
                var pubName = Get("Publisher");
                if (pubName.Length > 0)
                {
                    if (!publishers.TryGetValue(pubName.ToLowerInvariant(), out var p)) { p = new Publisher { TenantId = _tenant.TenantId, Name = pubName }; _db.Publishers.Add(p); publishers[pubName.ToLowerInvariant()] = p; }
                    book.PublisherId = p.Id;
                }
                var catName = Get("Category");
                if (catName.Length > 0)
                {
                    if (!categories.TryGetValue(catName.ToLowerInvariant(), out var c)) { c = new Category { TenantId = _tenant.TenantId, Name = catName }; _db.Categories.Add(c); categories[catName.ToLowerInvariant()] = c; }
                    book.CategoryId = c.Id;
                }

                if (isNew)
                {
                    _db.Books.Add(book);
                    if (book.Isbn != null) byIsbn[book.Isbn] = book;
                    if (book.Barcode != null) byBarcode[book.Barcode] = book;
                    created++;
                }
                else updated++;

                var opening = Int("OpeningStock", -1);
                if (opening > 0 && (isNew || overwriteStock)) openingDeltas.Add((book, opening, isNew));
            }
            catch (Exception ex)
            {
                skipped++;
                result.Errors.Add($"Row {line}: {ex.Message}");
            }
        }

        await _db.SaveChangesAsync(ct);

        foreach (var (book, qty, isNew) in openingDeltas)
        {
            if (isNew)
            {
                await _inventory.ReceiveAsync(storeId, new[] { new StockDelta(book.Id, qty, book.PurchasePrice) },
                    new MovementRef(MovementType.OpeningStock, "Import", book.Id, null, "Opening stock from Excel import"), ct);
            }
            else
            {
                var current = await _db.StockBalances.Where(s => s.BookId == book.Id && s.StoreId == storeId).Select(s => (int?)s.QuantityOnHand).FirstOrDefaultAsync(ct) ?? 0;
                var delta = qty - current;
                if (delta > 0)
                    await _inventory.ReceiveAsync(storeId, new[] { new StockDelta(book.Id, delta, book.PurchasePrice) },
                        new MovementRef(MovementType.Adjustment, "Import", book.Id, null, "Stock overwrite from Excel import"), ct);
                else if (delta < 0)
                    await _inventory.IssueAsync(storeId, new[] { new StockDelta(book.Id, -delta, book.PurchasePrice) },
                        new MovementRef(MovementType.Adjustment, "Import", book.Id, null, "Stock overwrite from Excel import"), ct);
            }
        }
        await tx.CommitAsync(ct);
        return result with { Created = created, Updated = updated, Skipped = skipped };
    }

    // ---------- named lists ----------

    private async Task<Author> GetOrCreateAuthorAsync(string name, CancellationToken ct)
    {
        name = name.Trim();
        var a = await _db.Authors.FirstOrDefaultAsync(x => x.Name == name, ct);
        if (a is null) { a = new Author { TenantId = _tenant.TenantId, Name = name }; _db.Authors.Add(a); }
        return a;
    }
    private async Task<Publisher> GetOrCreatePublisherAsync(string name, CancellationToken ct)
    {
        name = name.Trim();
        var p = await _db.Publishers.FirstOrDefaultAsync(x => x.Name == name, ct);
        if (p is null) { p = new Publisher { TenantId = _tenant.TenantId, Name = name }; _db.Publishers.Add(p); }
        return p;
    }
    private async Task<Category> GetOrCreateCategoryAsync(string name, CancellationToken ct)
    {
        name = name.Trim();
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Name == name, ct);
        if (c is null) { c = new Category { TenantId = _tenant.TenantId, Name = name }; _db.Categories.Add(c); }
        return c;
    }

    public async Task<IReadOnlyList<NamedDto>> ListAuthorsAsync(string? search, CancellationToken ct)
    {
        var q = _db.Authors.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(a => a.Name.Contains(search));
        return await q.OrderBy(a => a.Name).Take(500).Select(a => new NamedDto(a.Id, a.Name, null)).ToListAsync(ct);
    }
    public async Task<NamedDto> SaveAuthorAsync(Guid? id, SaveNamedRequest r, CancellationToken ct)
    {
        await _namedValidator.ValidateAndThrowAppAsync(r, ct);
        var name = r.Name.Trim();
        if (await _db.Authors.AnyAsync(a => a.Name == name && a.Id != id, ct)) throw new ConflictException("duplicate", "Author already exists.");
        Author a;
        if (id.HasValue) { a = await _db.Authors.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Author", id); a.Name = name; }
        else { a = new Author { TenantId = _tenant.TenantId, Name = name }; _db.Authors.Add(a); }
        await _db.SaveChangesAsync(ct);
        return new NamedDto(a.Id, a.Name);
    }
    public async Task DeleteAuthorAsync(Guid id, CancellationToken ct)
    {
        var a = await _db.Authors.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Author", id);
        if (await _db.Books.AnyAsync(b => b.AuthorId == id, ct)) throw new ConflictException("in-use", "Author is used by books.");
        _db.Authors.Remove(a);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NamedDto>> ListPublishersAsync(string? search, CancellationToken ct)
    {
        var q = _db.Publishers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(a => a.Name.Contains(search));
        return await q.OrderBy(a => a.Name).Take(500).Select(a => new NamedDto(a.Id, a.Name, null)).ToListAsync(ct);
    }
    public async Task<NamedDto> SavePublisherAsync(Guid? id, SaveNamedRequest r, CancellationToken ct)
    {
        await _namedValidator.ValidateAndThrowAppAsync(r, ct);
        var name = r.Name.Trim();
        if (await _db.Publishers.AnyAsync(a => a.Name == name && a.Id != id, ct)) throw new ConflictException("duplicate", "Publisher already exists.");
        Publisher p;
        if (id.HasValue) { p = await _db.Publishers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Publisher", id); p.Name = name; }
        else { p = new Publisher { TenantId = _tenant.TenantId, Name = name }; _db.Publishers.Add(p); }
        await _db.SaveChangesAsync(ct);
        return new NamedDto(p.Id, p.Name);
    }
    public async Task DeletePublisherAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.Publishers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Publisher", id);
        if (await _db.Books.AnyAsync(b => b.PublisherId == id, ct)) throw new ConflictException("in-use", "Publisher is used by books.");
        _db.Publishers.Remove(p);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NamedDto>> ListCategoriesAsync(string? search, CancellationToken ct)
    {
        var q = _db.Categories.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(a => a.Name.Contains(search));
        return await q.OrderBy(a => a.Name).Take(500).Select(a => new NamedDto(a.Id, a.Name, a.ParentId)).ToListAsync(ct);
    }
    public async Task<NamedDto> SaveCategoryAsync(Guid? id, SaveNamedRequest r, CancellationToken ct)
    {
        await _namedValidator.ValidateAndThrowAppAsync(r, ct);
        var name = r.Name.Trim();
        if (await _db.Categories.AnyAsync(a => a.Name == name && a.Id != id, ct)) throw new ConflictException("duplicate", "Category already exists.");
        Category c;
        if (id.HasValue) { c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Category", id); c.Name = name; c.ParentId = r.ParentId; }
        else { c = new Category { TenantId = _tenant.TenantId, Name = name, ParentId = r.ParentId }; _db.Categories.Add(c); }
        await _db.SaveChangesAsync(ct);
        return new NamedDto(c.Id, c.Name, c.ParentId);
    }
    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Category", id);
        if (await _db.Books.AnyAsync(b => b.CategoryId == id, ct)) throw new ConflictException("in-use", "Category is used by books.");
        _db.Categories.Remove(c);
        await _db.SaveChangesAsync(ct);
    }
}
