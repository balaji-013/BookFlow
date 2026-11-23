using BookFlow.API.Auth;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Features.Audit;
using BookFlow.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookFlow.API.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = Policies.Manager)]
public sealed class ReportsController : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly IReportService _reports;
    private readonly IExcelService _excel;
    private readonly IClock _clock;

    public ReportsController(IReportService reports, IExcelService excel, IClock clock) { _reports = reports; _excel = excel; _clock = clock; }

    private (DateOnly from, DateOnly to) Range(DateOnly? from, DateOnly? to)
    {
        var t = to ?? _clock.TodayIst;
        var f = from ?? t.AddDays(-29);
        if (f > t) (f, t) = (t, f);
        if (t.DayNumber - f.DayNumber > 366 * 3) throw new Application.Common.Models.AppValidationException("from", "Date range cannot exceed 3 years.");
        return (f, t);
    }

    private IActionResult Out<T>(IReadOnlyList<T> rows, string? format, string name) =>
        string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase)
            ? File(_excel.Export(name, rows), Xlsx, $"bookflow-{name}-{_clock.TodayIst:yyyyMMdd}.xlsx")
            : Ok(rows);

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> Dashboard([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await _reports.DashboardAsync(from, to, ct));

    [HttpGet("sales/daily")]
    public async Task<IActionResult> Daily([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? format, CancellationToken ct)
    { var (f, t) = Range(from, to); return Out(await _reports.DailySalesAsync(f, t, ct), format, "daily-sales"); }

    [HttpGet("sales/monthly")]
    public async Task<IActionResult> Monthly([FromQuery] int? year, [FromQuery] string? format, CancellationToken ct)
        => Out(await _reports.MonthlySalesAsync(year ?? _clock.TodayIst.Year, ct), format, "monthly-sales");

    [HttpGet("sales/by-book")]
    public async Task<IActionResult> ByBook([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int top = 100, [FromQuery] string? format = null, CancellationToken ct = default)
    { var (f, t) = Range(from, to); return Out(await _reports.SalesByBookAsync(f, t, top, ct), format, "sales-by-book"); }

    [HttpGet("sales/by-category")]
    public async Task<IActionResult> ByCategory([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? format, CancellationToken ct)
    { var (f, t) = Range(from, to); return Out(await _reports.SalesByCategoryAsync(f, t, ct), format, "sales-by-category"); }

    [HttpGet("purchases/by-supplier")]
    public async Task<IActionResult> BySupplier([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? format, CancellationToken ct)
    { var (f, t) = Range(from, to); return Out(await _reports.PurchasesBySupplierAsync(f, t, ct), format, "purchases-by-supplier"); }

    [HttpGet("inventory/movement")]
    public async Task<IActionResult> Movement([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? format, CancellationToken ct)
    { var (f, t) = Range(from, to); return Out(await _reports.StockMovementAsync(f, t, ct), format, "stock-movement"); }

    [HttpGet("inventory/aging")]
    public async Task<IActionResult> Aging([FromQuery] int minDays = 90, [FromQuery] string? format = null, CancellationToken ct = default)
        => Out(await _reports.InventoryAgingAsync(minDays, ct), format, "inventory-aging");

    [HttpGet("returns")]
    public async Task<IActionResult> Returns([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? format, CancellationToken ct)
    { var (f, t) = Range(from, to); return Out(await _reports.ReturnsAsync(f, t, ct), format, "sales-returns"); }

    [HttpGet("profitability")]
    public async Task<IActionResult> Profitability([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string groupBy = "category", [FromQuery] string? format = null, CancellationToken ct = default)
    { var (f, t) = Range(from, to); return Out(await _reports.ProfitabilityAsync(f, t, groupBy, ct), format, "profitability"); }
}

[ApiController]
[Route("api/v1/audit")]
[Authorize(Policy = Policies.Owner)]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditQueryService _audit;
    public AuditController(IAuditQueryService audit) => _audit = audit;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? entity, [FromQuery] string? entityId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _audit.ListAsync(entity, entityId, from, to, page, pageSize, ct));
}
