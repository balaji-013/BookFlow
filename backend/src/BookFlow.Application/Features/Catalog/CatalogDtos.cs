using FluentValidation;

namespace BookFlow.Application.Features.Catalog;

public sealed record BookDto(
    Guid Id, string? Isbn, string? Barcode, string Title, string? Subtitle, Guid? AuthorId, string? AuthorName,
    Guid? PublisherId, string? PublisherName, Guid? CategoryId, string? CategoryName, string? Edition, string Language, int? Pages,
    decimal PurchasePrice, decimal SellingPrice, decimal Mrp, string HsnCode, decimal GstRate, int ReorderLevel, bool IsActive,
    int QuantityOnHand, byte[] RowVersion);

public sealed record BookLookupDto(Guid Id, string Title, string? Isbn, string? Barcode, string? AuthorName, decimal SellingPrice, decimal Mrp,
    string HsnCode, decimal GstRate, int QuantityOnHand);

public sealed record SaveBookRequest(
    string? Isbn, string? Barcode, string Title, string? Subtitle, Guid? AuthorId, string? AuthorName, Guid? PublisherId, string? PublisherName,
    Guid? CategoryId, string? CategoryName, string? Edition, string Language, int? Pages, decimal PurchasePrice, decimal SellingPrice, decimal Mrp,
    string HsnCode, decimal GstRate, int ReorderLevel, bool IsActive, int? OpeningStock, byte[]? RowVersion);

public sealed record NamedDto(Guid Id, string Name, Guid? ParentId = null);
public sealed record SaveNamedRequest(string Name, Guid? ParentId = null);

public sealed record ImportResult(int Created, int Updated, int Skipped, List<string> Errors);

public sealed class SaveBookValidator : AbstractValidator<SaveBookRequest>
{
    public SaveBookValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Isbn).Matches("^[0-9Xx-]{10,17}$").When(x => !string.IsNullOrWhiteSpace(x.Isbn)).WithMessage("ISBN must be 10 or 13 digits.");
        RuleFor(x => x.Barcode).MaximumLength(64);
        RuleFor(x => x.Language).NotEmpty().MaximumLength(40);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Mrp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.HsnCode).NotEmpty().MaximumLength(8).Matches("^[0-9]{4,8}$");
        RuleFor(x => x.GstRate).InclusiveBetween(0, 28);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OpeningStock).GreaterThanOrEqualTo(0).When(x => x.OpeningStock.HasValue);
    }
}

public sealed class SaveNamedValidator : AbstractValidator<SaveNamedRequest>
{
    public SaveNamedValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
    }
}
