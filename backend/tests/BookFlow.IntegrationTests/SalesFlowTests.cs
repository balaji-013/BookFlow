using System.Net;
using System.Net.Http.Json;
using BookFlow.Application.Features.Auth;
using BookFlow.Application.Features.Catalog;
using BookFlow.Application.Features.Finance;
using BookFlow.Application.Features.Inventory;
using BookFlow.Application.Features.Parties;
using BookFlow.Application.Features.Sales;
using BookFlow.Application.Features.Users;
using BookFlow.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace BookFlow.IntegrationTests;

[Collection("api")]
public class SalesFlowTests
{
    private readonly ApiFactory _factory;
    public SalesFlowTests(ApiFactory factory) => _factory = factory;

    private static CreateInvoiceRequest Invoice(Guid bookId, int qty, decimal unitPrice, decimal pay, Guid? customerId = null) =>
        new(customerId, null, null, null, DiscountType.None, 0, null, new() { new(bookId, qty, unitPrice, 0) },
            pay > 0 ? new InvoicePaymentRequest(PaymentMethod.Cash, pay, null) : null);

    [SqlServerFact]
    public async Task Full_sale_updates_stock_numbering_and_payment()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("Ponniyin Selvan 1", 250m, 10);
        book.QuantityOnHand.Should().Be(10);

        var res = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 2, 250m, 500m), Guid.NewGuid().ToString()));
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var inv = (await res.Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json))!;
        inv.InvoiceNumber.Should().MatchRegex(@"^INV/\d{4}-\d{2}/000001$");
        inv.GrandTotal.Should().Be(500m);
        inv.PaymentStatus.Should().Be(PaymentStatus.Paid);
        inv.Payments.Should().HaveCount(1);

        var after = await s.Client.GetFromJsonAsync<BookDto>($"/api/v1/books/{book.Id}", ApiFactory.Json);
        after!.QuantityOnHand.Should().Be(8);

        var movements = await s.Client.GetFromJsonAsync<PagedResultDto<MovementDto>>($"/api/v1/inventory/movements?bookId={book.Id}", ApiFactory.Json);
        movements!.Items.Should().Contain(m => m.MovementType == MovementType.Sale && m.Quantity == -2);
        movements.Items.Should().Contain(m => m.MovementType == MovementType.OpeningStock && m.Quantity == 10);

        var print = await s.Client.GetAsync($"/api/v1/sales/invoices/{inv.Id}/print");
        print.StatusCode.Should().Be(HttpStatusCode.OK);
        (await print.Content.ReadAsStringAsync()).Should().Contain(inv.InvoiceNumber).And.Contain("Ponniyin Selvan 1");
    }

    [SqlServerFact]
    public async Task Second_invoice_gets_next_sequential_number()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("Numbering", 10m, 100);
        for (var i = 1; i <= 3; i++)
        {
            var res = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 1, 10m, 10m), Guid.NewGuid().ToString()));
            var inv = (await res.Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json))!;
            inv.InvoiceNumber.Should().EndWith($"/{i:000000}");
        }
    }

    [SqlServerFact]
    public async Task Concurrent_sales_of_last_copy_only_one_succeeds()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("Last copy", 100m, 1);

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 1, 100m, 100m), Guid.NewGuid().ToString()))).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);
        var after = await s.Client.GetFromJsonAsync<BookDto>($"/api/v1/books/{book.Id}", ApiFactory.Json);
        after!.QuantityOnHand.Should().Be(0);
    }

    [SqlServerFact]
    public async Task Oversell_is_rejected_with_409_and_stock_unchanged()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("Scarce", 100m, 3);
        var res = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 5, 100m, 500m), Guid.NewGuid().ToString()));
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await res.Content.ReadAsStringAsync()).Should().Contain("insufficient-stock");
        var after = await s.Client.GetFromJsonAsync<BookDto>($"/api/v1/books/{book.Id}", ApiFactory.Json);
        after!.QuantityOnHand.Should().Be(3);
        var invoices = await s.Client.GetFromJsonAsync<PagedResultDto<InvoiceListItemDto>>("/api/v1/sales/invoices", ApiFactory.Json);
        invoices!.TotalCount.Should().Be(0, "a failed sale must not leave a half-written invoice");
    }

    [SqlServerFact]
    public async Task Same_idempotency_key_returns_same_invoice_without_duplicate()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("Idem", 50m, 10);
        var key = Guid.NewGuid().ToString();
        var first = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 1, 50m, 50m), key));
        var second = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 1, 50m, 50m), key));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.Should().ContainKey("Idempotent-Replay");
        var a = await first.Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json);
        var b = await second.Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json);
        b!.Id.Should().Be(a!.Id);
        var after = await s.Client.GetFromJsonAsync<BookDto>($"/api/v1/books/{book.Id}", ApiFactory.Json);
        after!.QuantityOnHand.Should().Be(9);
    }

    [SqlServerFact]
    public async Task Missing_idempotency_key_is_rejected()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("NoKey", 50m, 10);
        var res = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 1, 50m, 50m)));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SqlServerFact]
    public async Task Walk_in_customer_must_pay_in_full_but_named_customer_can_buy_on_credit()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("Credit", 100m, 10);
        var walkIn = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 1, 100m, 40m), Guid.NewGuid().ToString()));
        walkIn.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var cRes = await s.Client.PostAsJsonAsync("/api/v1/customers", new SaveCustomerRequest("School", "9000000001", null, null, null, CustomerType.Institutional, 0, null, true, null), ApiFactory.Json);
        var customer = (await cRes.Content.ReadFromJsonAsync<CustomerDto>(ApiFactory.Json))!;
        var credit = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 1, 100m, 40m, customer.Id), Guid.NewGuid().ToString()));
        credit.StatusCode.Should().Be(HttpStatusCode.Created, await credit.Content.ReadAsStringAsync());
        var inv = (await credit.Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json))!;
        inv.AmountDue.Should().Be(60m);
        inv.PaymentStatus.Should().Be(PaymentStatus.Partial);

        // Pay the balance
        var pay = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/payments",
            new CreatePaymentRequest(customer.Id, PaymentMethod.Upi, 60m, null, "UPI-REF", null, new() { new(inv.Id, 60m) }, null), Guid.NewGuid().ToString()));
        pay.StatusCode.Should().Be(HttpStatusCode.Created, await pay.Content.ReadAsStringAsync());
        var paid = await s.Client.GetFromJsonAsync<InvoiceDto>($"/api/v1/sales/invoices/{inv.Id}", ApiFactory.Json);
        paid!.PaymentStatus.Should().Be(PaymentStatus.Paid);
        paid.AmountDue.Should().Be(0);

        var cust = await s.Client.GetFromJsonAsync<CustomerDto>($"/api/v1/customers/{customer.Id}", ApiFactory.Json);
        cust!.Outstanding.Should().Be(0);
    }

    [SqlServerFact]
    public async Task Sales_return_restores_resalable_stock_and_issues_cash_refund()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("Returnable", 100m, 5);
        var res = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 3, 100m, 300m), Guid.NewGuid().ToString()));
        var inv = (await res.Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json))!;

        var ret = await s.Client.PostAsJsonAsync("/api/v1/sales/returns",
            new CreateSalesReturnRequest(inv.Id, null, "Customer changed mind", RefundMode.Cash, new() { new(inv.Items[0].Id, 1, ReturnCondition.Resalable), new(inv.Items[0].Id, 1, ReturnCondition.Damaged) }), ApiFactory.Json);
        ret.StatusCode.Should().Be(HttpStatusCode.Created, await ret.Content.ReadAsStringAsync());
        var sr = (await ret.Content.ReadFromJsonAsync<SalesReturnDto>(ApiFactory.Json))!;
        sr.Total.Should().Be(200m);

        var stock = await s.Client.GetFromJsonAsync<PagedResultDto<StockRowDto>>($"/api/v1/inventory/stock?search=Returnable", ApiFactory.Json);
        var row = stock!.Items.Single();
        row.QuantityOnHand.Should().Be(3);   // 5 - 3 + 1 resalable
        row.QuantityDamaged.Should().Be(1);

        var after = await s.Client.GetFromJsonAsync<InvoiceDto>($"/api/v1/sales/invoices/{inv.Id}", ApiFactory.Json);
        after!.Status.Should().Be(InvoiceStatus.PartiallyReturned);
        after.GrandTotal.Should().Be(100m);
    }

    [SqlServerFact]
    public async Task Cancel_invoice_restores_stock_and_refunds()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("Cancelable", 100m, 5);
        var res = await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 2, 100m, 200m), Guid.NewGuid().ToString()));
        var inv = (await res.Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json))!;
        var cancel = await s.Client.PostAsJsonAsync($"/api/v1/sales/invoices/{inv.Id}/cancel", new CancelInvoiceRequest("Wrong customer"), ApiFactory.Json);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        var after = await s.Client.GetFromJsonAsync<BookDto>($"/api/v1/books/{book.Id}", ApiFactory.Json);
        after!.QuantityOnHand.Should().Be(5);
        var dash = await s.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/reports/dashboard", ApiFactory.Json);
        dash.GetProperty("todaySales").GetDecimal().Should().Be(0);
    }

    [SqlServerFact]
    public async Task Dashboard_and_reports_return_consistent_totals()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("Report book", 200m, 10, gst: 5m);
        await s.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices", Invoice(book.Id, 2, 200m, 420m), Guid.NewGuid().ToString()));
        var dash = await s.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/reports/dashboard", ApiFactory.Json);
        dash.GetProperty("todaySales").GetDecimal().Should().Be(420m);
        dash.GetProperty("taxes").GetDecimal().Should().Be(20m);
        dash.GetProperty("invoiceCount").GetInt32().Should().Be(1);
        var daily = await s.Client.GetAsync("/api/v1/reports/sales/daily?format=xlsx");
        daily.StatusCode.Should().Be(HttpStatusCode.OK);
        daily.Content.Headers.ContentType!.MediaType.Should().Contain("spreadsheet");
    }
}

public sealed class PagedResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
