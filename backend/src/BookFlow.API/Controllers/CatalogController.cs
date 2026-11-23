using BookFlow.API.Auth;
using BookFlow.Application.Features.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookFlow.API.Controllers;

[ApiController]
[Route("api/v1/books")]
[Authorize(Policy = Policies.AnyStaff)]
public sealed class BooksController : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly ICatalogService _catalog;
    public BooksController(ICatalogService catalog) => _catalog = catalog;

    [HttpGet, EnableRateLimiting("search")]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] Guid? categoryId, [FromQuery] bool? lowStock, [FromQuery] bool includeInactive = false,
        [FromQuery] int? page = null, [FromQuery] int? pageSize = null, CancellationToken ct = default)
        => Ok(await _catalog.ListBooksAsync(search, categoryId, lowStock, includeInactive, page, pageSize, ct));

    [HttpGet("lookup"), EnableRateLimiting("search")]
    public async Task<ActionResult<BookLookupDto>> Lookup([FromQuery] string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return BadRequest();
        var dto = await _catalog.LookupAsync(code, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpGet("quick-search"), EnableRateLimiting("search")]
    public async Task<IActionResult> QuickSearch([FromQuery] string term, [FromQuery] int take = 10, CancellationToken ct = default)
        => Ok(await _catalog.QuickSearchAsync(term ?? string.Empty, take, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookDto>> Get(Guid id, CancellationToken ct) => Ok(await _catalog.GetBookAsync(id, ct));

    [HttpPost, Authorize(Policy = Policies.Inventory)]
    public async Task<ActionResult<BookDto>> Create(SaveBookRequest request, CancellationToken ct)
    {
        var dto = await _catalog.CreateBookAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Inventory)]
    public async Task<ActionResult<BookDto>> Update(Guid id, SaveBookRequest request, CancellationToken ct) => Ok(await _catalog.UpdateBookAsync(id, request, ct));

    [HttpDelete("{id:guid}"), Authorize(Policy = Policies.Inventory)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) { await _catalog.DeactivateBookAsync(id, ct); return NoContent(); }

    [HttpGet("import/template")]
    public IActionResult Template() => File(_catalog.ImportTemplate(), Xlsx, "bookflow-books-template.xlsx");

    [HttpPost("import"), Authorize(Policy = Policies.Inventory), RequestSizeLimit(10 * 1024 * 1024)]
    [Microsoft.AspNetCore.Http.Timeouts.RequestTimeout(120_000)]
    public async Task<ActionResult<ImportResult>> Import(IFormFile file, [FromQuery] bool overwriteStock = false, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return BadRequest(new ProblemDetails { Title = "Upload an .xlsx file.", Status = 400 });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return BadRequest(new ProblemDetails { Title = "Only .xlsx files are supported.", Status = 400 });
        await using var stream = file.OpenReadStream();
        return Ok(await _catalog.ImportBooksAsync(stream, overwriteStock, ct));
    }
}

[ApiController]
[Route("api/v1")]
[Authorize(Policy = Policies.AnyStaff)]
public sealed class CatalogListsController : ControllerBase
{
    private readonly ICatalogService _catalog;
    public CatalogListsController(ICatalogService catalog) => _catalog = catalog;

    [HttpGet("authors")] public async Task<IActionResult> Authors([FromQuery] string? search, CancellationToken ct) => Ok(await _catalog.ListAuthorsAsync(search, ct));
    [HttpPost("authors"), Authorize(Policy = Policies.Inventory)] public async Task<IActionResult> CreateAuthor(SaveNamedRequest r, CancellationToken ct) => StatusCode(201, await _catalog.SaveAuthorAsync(null, r, ct));
    [HttpPut("authors/{id:guid}"), Authorize(Policy = Policies.Inventory)] public async Task<IActionResult> UpdateAuthor(Guid id, SaveNamedRequest r, CancellationToken ct) => Ok(await _catalog.SaveAuthorAsync(id, r, ct));
    [HttpDelete("authors/{id:guid}"), Authorize(Policy = Policies.Inventory)] public async Task<IActionResult> DeleteAuthor(Guid id, CancellationToken ct) { await _catalog.DeleteAuthorAsync(id, ct); return NoContent(); }

    [HttpGet("publishers")] public async Task<IActionResult> Publishers([FromQuery] string? search, CancellationToken ct) => Ok(await _catalog.ListPublishersAsync(search, ct));
    [HttpPost("publishers"), Authorize(Policy = Policies.Inventory)] public async Task<IActionResult> CreatePublisher(SaveNamedRequest r, CancellationToken ct) => StatusCode(201, await _catalog.SavePublisherAsync(null, r, ct));
    [HttpPut("publishers/{id:guid}"), Authorize(Policy = Policies.Inventory)] public async Task<IActionResult> UpdatePublisher(Guid id, SaveNamedRequest r, CancellationToken ct) => Ok(await _catalog.SavePublisherAsync(id, r, ct));
    [HttpDelete("publishers/{id:guid}"), Authorize(Policy = Policies.Inventory)] public async Task<IActionResult> DeletePublisher(Guid id, CancellationToken ct) { await _catalog.DeletePublisherAsync(id, ct); return NoContent(); }

    [HttpGet("categories")] public async Task<IActionResult> Categories([FromQuery] string? search, CancellationToken ct) => Ok(await _catalog.ListCategoriesAsync(search, ct));
    [HttpPost("categories"), Authorize(Policy = Policies.Inventory)] public async Task<IActionResult> CreateCategory(SaveNamedRequest r, CancellationToken ct) => StatusCode(201, await _catalog.SaveCategoryAsync(null, r, ct));
    [HttpPut("categories/{id:guid}"), Authorize(Policy = Policies.Inventory)] public async Task<IActionResult> UpdateCategory(Guid id, SaveNamedRequest r, CancellationToken ct) => Ok(await _catalog.SaveCategoryAsync(id, r, ct));
    [HttpDelete("categories/{id:guid}"), Authorize(Policy = Policies.Inventory)] public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct) { await _catalog.DeleteCategoryAsync(id, ct); return NoContent(); }
}
