using BookFlow.Application.Common.Interfaces;
using BookFlow.Application.Common.Models;
using BookFlow.Domain.Common;
using BookFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.Application.Features.Reports;

public sealed record DashboardDto(
    DateOnly From, DateOnly To,
    decimal TodaySales, int TodayInvoices, decimal MonthSales, int MonthInvoices,
    decimal GrossSales, decimal Discounts, decimal Taxes, decimal NetSales, decimal CostOfGoods, decimal GrossProfit, decimal GrossMarginPercent,
    decimal Purchases, decimal SalesReturns, decimal InventoryValuation, int LowStockCount, decimal CustomerOutstanding, decimal SupplierOutstanding,
    int InvoiceCount, decimal AverageInvoice, List<DailyPoint> Trend, List<TopBookRow> TopBooks, List<PaymentMixRow> PaymentMix);

public sealed record DailyPoint(DateOnly Date, decimal Sales, int Invoices);
public sealed record TopBookRow(Guid BookId, string Title, int Quantity, decimal Revenue);
public sealed record PaymentMixRow(PaymentMethod Method, decimal Amount, int Count);

public sealed record DailySalesRow(DateOnly Date, int Invoices, decimal Gross, decimal Discount, decimal Taxable, decimal Tax, decimal Net, decimal Cost, decimal Profit);
public sealed record MonthlySalesRow(int Year, int Month, int Invoices, decimal Net, decimal Profit);
public sealed record BookSalesRow(Guid BookId, string Title, string? Isbn, string? Category, int Quantity, decimal Revenue, decimal Cost, decimal Profit, int Returned);
public sealed record CategorySalesRow(string Category, int Quantity, decimal Revenue, decimal Profit);
public sealed record SupplierPurchaseRow(Guid SupplierId, string Supplier, int Receipts, int Units, decimal Amount, decimal Paid, decimal Outstanding);
public sealed record StockMovementRow(Guid BookId, string Title, int Opening, int In, int Out, int Closing);
public sealed record InventoryAgingRow(Guid BookId, string Title, int QuantityOnHand, DateTime? LastSoldAt, DateTime? LastReceivedAt, int DaysSinceLastSale, decimal StockValue);
public sealed record ReturnsRow(DateOnly Date, string ReturnNumber, string InvoiceNumber, string Customer, RefundMode RefundMode, decimal Total, string? Reason);
public sealed record ProfitabilityRow(string Group, decimal Revenue, decimal Cost, decimal Profit, decimal MarginPercent);

public interface IReportService
{
    Task<DashboardDto> DashboardAsync(DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<IReadOnlyList<DailySalesRow>> DailySalesAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<MonthlySalesRow>> MonthlySalesAsync(int year, CancellationToken ct);
    Task<IReadOnlyList<BookSalesRow>> SalesByBookAsync(DateOnly from, DateOnly to, int top, CancellationToken ct);
    Task<IReadOnlyList<CategorySalesRow>> SalesByCategoryAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<SupplierPurchaseRow>> PurchasesBySupplierAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<StockMovementRow>> StockMovementAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<InventoryAgingRow>> InventoryAgingAsync(int minDays, CancellationToken ct);
    Task<IReadOnlyList<ReturnsRow>> ReturnsAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<ProfitabilityRow>> ProfitabilityAsync(DateOnly from, DateOnly to, string groupBy, CancellationToken ct);
}

public sealed class ReportService : IReportService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public ReportService(IAppDbContext db, IClock clock) { _db = db; _clock = clock; }

    private IQueryable<Domain.Entities.Sales.SalesInvoice> Sales(DateOnly from, DateOnly to) =>
        _db.SalesInvoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Cancelled && i.InvoiceDate >= from && i.InvoiceDate <= to);

    public async Task<DashboardDto> DashboardAsync(DateOnly? fromArg, DateOnly? toArg, CancellationToken ct)
    {
        var today = _clock.TodayIst;
        var to = toArg ?? today;
        var from = fromArg ?? to.AddDays(-29);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var todayAgg = await Sales(today, today).GroupBy(_ => 1).Select(g => new { Sales = g.Sum(i => i.GrandTotal), Count = g.Count() }).FirstOrDefaultAsync(ct);
        var monthAgg = await Sales(monthStart, today).GroupBy(_ => 1).Select(g => new { Sales = g.Sum(i => i.GrandTotal), Count = g.Count() }).FirstOrDefaultAsync(ct);

        var period = Sales(from, to);
        var agg = await period.GroupBy(_ => 1).Select(g => new
        {
            Gross = g.Sum(i => i.SubTotal),
            Disc = g.Sum(i => i.DiscountTotal),
            Tax = g.Sum(i => i.CgstTotal + i.SgstTotal + i.IgstTotal),
            Net = g.Sum(i => i.GrandTotal),
            Count = g.Count()
        }).FirstOrDefaultAsync(ct);
        var cogs = await _db.SalesInvoiceItems.AsNoTracking()
            .Where(x => period.Select(i => i.Id).Contains(x.SalesInvoiceId))
            .SumAsync(x => (decimal?)((x.Quantity - x.QuantityReturned) * x.UnitCost), ct) ?? 0m;
        var returns = await _db.SalesReturns.AsNoTracking().Where(r => r.ReturnDate >= from && r.ReturnDate <= to).SumAsync(r => (decimal?)r.Total, ct) ?? 0m;
        var purchases = await _db.GoodsReceipts.AsNoTracking().Where(g => g.ReceivedDate >= from && g.ReceivedDate <= to).SumAsync(g => (decimal?)g.Total, ct) ?? 0m;

        var valuation = await _db.StockBalances.AsNoTracking().SumAsync(s => (decimal?)(s.QuantityOnHand * s.AverageCost), ct) ?? 0m;
        var lowStock = await _db.Books.AsNoTracking().Where(b => b.IsActive)
            .CountAsync(b => (_db.StockBalances.Where(s => s.BookId == b.Id).Sum(s => (int?)s.QuantityOnHand) ?? 0) <= b.ReorderLevel, ct);
        var custOut = await _db.SalesInvoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Cancelled).SumAsync(i => (decimal?)i.AmountDue, ct) ?? 0m;
        var supOut = (await _db.GoodsReceipts.AsNoTracking().SumAsync(g => (decimal?)g.Total, ct) ?? 0m)
                     - (await _db.PurchaseReturns.AsNoTracking().SumAsync(p => (decimal?)p.Total, ct) ?? 0m)
                     - (await _db.SupplierPayments.AsNoTracking().SumAsync(p => (decimal?)p.Amount, ct) ?? 0m);

        var trendRaw = await period.GroupBy(i => i.InvoiceDate).Select(g => new { Date = g.Key, Sales = g.Sum(i => i.GrandTotal), Count = g.Count() }).ToListAsync(ct);
        var trend = new List<DailyPoint>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var t = trendRaw.FirstOrDefault(x => x.Date == d);
            trend.Add(new DailyPoint(d, t?.Sales ?? 0m, t?.Count ?? 0));
        }

        var top = (await _db.SalesInvoiceItems.AsNoTracking()
            .Where(x => period.Select(i => i.Id).Contains(x.SalesInvoiceId))
            .GroupBy(x => new { x.BookId, x.Title })
            .Select(g => new { g.Key.BookId, g.Key.Title, Quantity = g.Sum(x => x.Quantity - x.QuantityReturned), Revenue = g.Sum(x => x.LineTotal) })
            .OrderByDescending(x => x.Quantity).Take(10).ToListAsync(ct))
            .Select(x => new TopBookRow(x.BookId, x.Title, x.Quantity, x.Revenue)).ToList();

        var fromDt = from.ToDateTime(TimeOnly.MinValue); var toDt = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var mix = await _db.Payments.AsNoTracking().Where(p => p.Direction == PaymentDirection.In && p.PaidAt >= fromDt && p.PaidAt < toDt)
            .GroupBy(p => p.Method).Select(g => new PaymentMixRow(g.Key, g.Sum(p => p.Amount), g.Count())).ToListAsync(ct);

        var net = agg?.Net ?? 0m;
        var profit = Money.Round2((agg?.Gross ?? 0m) - (agg?.Disc ?? 0m) - cogs);
        var taxable = (agg?.Gross ?? 0m) - (agg?.Disc ?? 0m);
        return new DashboardDto(from, to,
            todayAgg?.Sales ?? 0m, todayAgg?.Count ?? 0, monthAgg?.Sales ?? 0m, monthAgg?.Count ?? 0,
            agg?.Gross ?? 0m, agg?.Disc ?? 0m, agg?.Tax ?? 0m, net, Money.Round2(cogs), profit, taxable == 0 ? 0 : Money.Round2(profit / taxable * 100m),
            purchases, returns, Money.Round2(valuation), lowStock, Money.Round2(custOut), Money.Round2(supOut),
            agg?.Count ?? 0, agg is null || agg.Count == 0 ? 0 : Money.Round2(net / agg.Count), trend, top, mix);
    }

    public async Task<IReadOnlyList<DailySalesRow>> DailySalesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var period = Sales(from, to);
        var rows = await period
            .GroupBy(i => i.InvoiceDate)
            .Select(g => new
            {
                Date = g.Key,
                Invoices = g.Count(),
                Gross = g.Sum(i => i.SubTotal),
                Discount = g.Sum(i => i.DiscountTotal),
                Taxable = g.Sum(i => i.TaxableTotal),
                Tax = g.Sum(i => i.CgstTotal + i.SgstTotal + i.IgstTotal),
                Net = g.Sum(i => i.GrandTotal)
            }).OrderBy(x => x.Date).ToListAsync(ct);
        var costs = await CostByDateAsync(period, ct);
        return rows.Select(r =>
        {
            var cost = costs.GetValueOrDefault(r.Date, 0m);
            return new DailySalesRow(r.Date, r.Invoices, r.Gross, r.Discount, r.Taxable, r.Tax, r.Net, Money.Round2(cost), Money.Round2(r.Taxable - cost));
        }).ToList();
    }

    public async Task<IReadOnlyList<MonthlySalesRow>> MonthlySalesAsync(int year, CancellationToken ct)
    {
        var from = new DateOnly(year, 1, 1); var to = new DateOnly(year, 12, 31);
        var period = Sales(from, to);
        var rows = await period
            .GroupBy(i => i.InvoiceDate.Month)
            .Select(g => new { Month = g.Key, Invoices = g.Count(), Net = g.Sum(i => i.GrandTotal), Taxable = g.Sum(i => i.TaxableTotal) })
            .ToListAsync(ct);
        var costs = await CostByDateAsync(period, ct);
        return Enumerable.Range(1, 12).Select(m =>
        {
            var r = rows.FirstOrDefault(x => x.Month == m);
            var cost = costs.Where(kv => kv.Key.Month == m).Sum(kv => kv.Value);
            return new MonthlySalesRow(year, m, r?.Invoices ?? 0, r?.Net ?? 0m, r is null ? 0m : Money.Round2(r.Taxable - cost));
        }).ToList();
    }

    private async Task<Dictionary<DateOnly, decimal>> CostByDateAsync(IQueryable<Domain.Entities.Sales.SalesInvoice> period, CancellationToken ct)
    {
        var rows = await (from x in _db.SalesInvoiceItems.AsNoTracking()
                          join i in period on x.SalesInvoiceId equals i.Id
                          group x by i.InvoiceDate into g
                          select new { Date = g.Key, Cost = g.Sum(y => (y.Quantity - y.QuantityReturned) * y.UnitCost) }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Date, r => r.Cost);
    }

    public async Task<IReadOnlyList<BookSalesRow>> SalesByBookAsync(DateOnly from, DateOnly to, int top, CancellationToken ct)
    {
        var invIds = Sales(from, to).Select(i => i.Id);
        var q = from x in _db.SalesInvoiceItems.AsNoTracking()
                where invIds.Contains(x.SalesInvoiceId)
                join b in _db.Books on x.BookId equals b.Id
                group new { x, b } by new { x.BookId, b.Title, b.Isbn, Category = b.Category != null ? b.Category.Name : null } into g
                select new
                {
                    g.Key.BookId,
                    g.Key.Title,
                    g.Key.Isbn,
                    g.Key.Category,
                    Quantity = g.Sum(y => y.x.Quantity - y.x.QuantityReturned),
                    Revenue = g.Sum(y => y.x.TaxableAmount),
                    Cost = g.Sum(y => (y.x.Quantity - y.x.QuantityReturned) * y.x.UnitCost),
                    Returned = g.Sum(y => y.x.QuantityReturned)
                };
        var rows = await q.OrderByDescending(r => r.Quantity).Take(Math.Clamp(top, 1, 1000)).ToListAsync(ct);
        return rows.Select(r => new BookSalesRow(r.BookId, r.Title, r.Isbn, r.Category, r.Quantity, r.Revenue, Money.Round2(r.Cost), Money.Round2(r.Revenue - r.Cost), r.Returned)).ToList();
    }

    public async Task<IReadOnlyList<CategorySalesRow>> SalesByCategoryAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var invIds = Sales(from, to).Select(i => i.Id);
        var q = from x in _db.SalesInvoiceItems.AsNoTracking()
                where invIds.Contains(x.SalesInvoiceId)
                join b in _db.Books on x.BookId equals b.Id
                group x by (b.Category != null ? b.Category.Name : "Uncategorised") into g
                select new
                {
                    Category = g.Key,
                    Quantity = g.Sum(y => y.Quantity - y.QuantityReturned),
                    Revenue = g.Sum(y => y.TaxableAmount),
                    Cost = g.Sum(y => (y.Quantity - y.QuantityReturned) * y.UnitCost)
                };
        var rows = await q.OrderByDescending(r => r.Revenue).ToListAsync(ct);
        return rows.Select(r => new CategorySalesRow(r.Category, r.Quantity, r.Revenue, Money.Round2(r.Revenue - r.Cost))).ToList();
    }

    public async Task<IReadOnlyList<SupplierPurchaseRow>> PurchasesBySupplierAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var grns = await _db.GoodsReceipts.AsNoTracking().Where(g => g.ReceivedDate >= from && g.ReceivedDate <= to)
            .GroupBy(g => g.SupplierId)
            .Select(g => new { SupplierId = g.Key, Receipts = g.Count(), Amount = g.Sum(x => x.Total), Paid = g.Sum(x => x.AmountPaid) })
            .ToListAsync(ct);
        var (f, t) = (from, to);
        var units = await (from it in _db.GoodsReceiptItems.AsNoTracking()
                           join g in _db.GoodsReceipts on it.GoodsReceiptId equals g.Id
                           where g.ReceivedDate >= f && g.ReceivedDate <= t
                           group it by g.SupplierId into grp
                           select new { SupplierId = grp.Key, Units = grp.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.SupplierId, x => x.Units, ct);
        var ids = grns.Select(g => g.SupplierId).ToList();
        var names = await _db.Suppliers.Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return grns.Select(g => new SupplierPurchaseRow(g.SupplierId, names.GetValueOrDefault(g.SupplierId, ""), g.Receipts, units.GetValueOrDefault(g.SupplierId, 0), g.Amount, g.Paid, g.Amount - g.Paid))
            .OrderByDescending(r => r.Amount).ToList();
    }

    public async Task<IReadOnlyList<StockMovementRow>> StockMovementAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue); var toDt = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var rows = await _db.InventoryMovements.AsNoTracking()
            .GroupBy(m => m.BookId)
            .Select(g => new
            {
                BookId = g.Key,
                Opening = g.Sum(m => m.OccurredAt < fromDt ? m.Quantity : 0),
                In = g.Sum(m => m.OccurredAt >= fromDt && m.OccurredAt < toDt && m.Quantity > 0 ? m.Quantity : 0),
                Out = g.Sum(m => m.OccurredAt >= fromDt && m.OccurredAt < toDt && m.Quantity < 0 ? m.Quantity : 0)
            }).ToListAsync(ct);
        var ids = rows.Select(r => r.BookId).ToList();
        var titles = await _db.Books.Where(b => ids.Contains(b.Id)).Select(b => new { b.Id, b.Title }).ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        return rows.Select(r => new StockMovementRow(r.BookId, titles.GetValueOrDefault(r.BookId, ""), r.Opening, r.In, -r.Out, r.Opening + r.In + r.Out))
            .Where(r => r.Opening != 0 || r.In != 0 || r.Out != 0).OrderBy(r => r.Title).ToList();
    }

    public async Task<IReadOnlyList<InventoryAgingRow>> InventoryAgingAsync(int minDays, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var rows = await (from sb in _db.StockBalances.AsNoTracking()
                          join b in _db.Books on sb.BookId equals b.Id
                          where sb.QuantityOnHand > 0
                          select new
                          {
                              sb.BookId,
                              b.Title,
                              sb.QuantityOnHand,
                              sb.AverageCost,
                              LastSold = _db.InventoryMovements.Where(m => m.BookId == sb.BookId && m.MovementType == MovementType.Sale).Max(m => (DateTime?)m.OccurredAt),
                              LastReceived = _db.InventoryMovements.Where(m => m.BookId == sb.BookId && (m.MovementType == MovementType.Purchase || m.MovementType == MovementType.OpeningStock)).Max(m => (DateTime?)m.OccurredAt)
                          }).ToListAsync(ct);
        return rows.Select(r =>
        {
            var anchor = r.LastSold ?? r.LastReceived ?? now;
            return new InventoryAgingRow(r.BookId, r.Title, r.QuantityOnHand, r.LastSold, r.LastReceived, (int)(now - anchor).TotalDays, Money.Round2(r.QuantityOnHand * r.AverageCost));
        })
            .Where(r => r.DaysSinceLastSale >= minDays).OrderByDescending(r => r.DaysSinceLastSale).ToList();
    }

    public async Task<IReadOnlyList<ReturnsRow>> ReturnsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var returns = _db.SalesReturns.AsNoTracking().Where(x => x.ReturnDate >= from && x.ReturnDate <= to);
        return await (from r in returns
                      join i in _db.SalesInvoices on r.SalesInvoiceId equals i.Id
                      orderby r.ReturnDate descending
                      select new ReturnsRow(r.ReturnDate, r.ReturnNumber, i.InvoiceNumber, i.CustomerName, r.RefundMode, r.Total, r.Reason)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProfitabilityRow>> ProfitabilityAsync(DateOnly from, DateOnly to, string groupBy, CancellationToken ct)
    {
        var invIds = Sales(from, to).Select(i => i.Id);
        var baseQ = from x in _db.SalesInvoiceItems.AsNoTracking()
                    where invIds.Contains(x.SalesInvoiceId)
                    join b in _db.Books on x.BookId equals b.Id
                    select new
                    {
                        Group = groupBy == "publisher" ? (b.Publisher != null ? b.Publisher.Name : "Unknown publisher")
                              : groupBy == "author" ? (b.Author != null ? b.Author.Name : "Unknown author")
                              : groupBy == "book" ? b.Title
                              : (b.Category != null ? b.Category.Name : "Uncategorised"),
                        Revenue = x.TaxableAmount,
                        Cost = (x.Quantity - x.QuantityReturned) * x.UnitCost
                    };
        var rows = await baseQ.GroupBy(x => x.Group).Select(g => new { Group = g.Key, Revenue = g.Sum(x => x.Revenue), Cost = g.Sum(x => x.Cost) }).ToListAsync(ct);
        return rows.Select(r => new ProfitabilityRow(r.Group, r.Revenue, Money.Round2(r.Cost), Money.Round2(r.Revenue - r.Cost),
                r.Revenue == 0 ? 0 : Money.Round2((r.Revenue - r.Cost) / r.Revenue * 100m)))
            .OrderByDescending(r => r.Profit).ToList();
    }
}