using BookFlow.Domain.Common;

namespace BookFlow.Domain.Entities.Catalog;

public class Author : TenantEntity
{
    public string Name { get; set; } = default!;
}

public class Publisher : TenantEntity
{
    public string Name { get; set; } = default!;
}

public class Category : TenantEntity
{
    public string Name { get; set; } = default!;
    public Guid? ParentId { get; set; }
}

public class Book : TenantEntity, IHasRowVersion
{
    public string? Isbn { get; set; }
    public string? Barcode { get; set; }
    public string Title { get; set; } = default!;
    public string? Subtitle { get; set; }
    public Guid? AuthorId { get; set; }
    public Author? Author { get; set; }
    public Guid? PublisherId { get; set; }
    public Publisher? Publisher { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? Edition { get; set; }
    public string Language { get; set; } = "Tamil";
    public int? Pages { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal Mrp { get; set; }
    public string HsnCode { get; set; } = "4901";
    public decimal GstRate { get; set; } // percent, 0 for printed books
    public int ReorderLevel { get; set; } = 2;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
