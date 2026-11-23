using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookFlow.Application.Features.Auth;
using BookFlow.Application.Features.Catalog;
using BookFlow.Application.Features.Parties;
using BookFlow.Application.Features.Purchasing;
using BookFlow.Application.Features.Sales;
using BookFlow.Application.Features.Users;
using BookFlow.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace BookFlow.IntegrationTests;

[Collection("api")]
public class TenantIsolationTests
{
    private readonly ApiFactory _factory;
    public TenantIsolationTests(ApiFactory factory) => _factory = factory;

    [SqlServerFact]
    public async Task Tenant_B_cannot_read_update_or_sell_tenant_A_data()
    {
        var a = await _factory.RegisterTenantAsync();
        var b = await _factory.RegisterTenantAsync();
        var bookA = await a.CreateBookAsync("Secret A", 100m, 5);
        var custRes = await a.Client.PostAsJsonAsync("/api/v1/customers", new SaveCustomerRequest("Cust A", "9111111111", null, null, null, CustomerType.Retail, 0, null, true, null), ApiFactory.Json);
        var custA = (await custRes.Content.ReadFromJsonAsync<CustomerDto>(ApiFactory.Json))!;
        var invRes = await a.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices",
            new CreateInvoiceRequest(null, null, null, null, DiscountType.None, 0, null, new() { new(bookA.Id, 1, 100m, 0) }, new(PaymentMethod.Cash, 100m, null)), Guid.NewGuid().ToString()));
        var invA = (await invRes.Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json))!;

        (await b.Client.GetAsync($"/api/v1/books/{bookA.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.Client.GetAsync($"/api/v1/customers/{custA.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.Client.GetAsync($"/api/v1/sales/invoices/{invA.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.Client.GetAsync($"/api/v1/sales/invoices/{invA.Id}/print")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // B's lists never include A's rows
        var bBooks = await b.Client.GetFromJsonAsync<PagedResultDto<BookDto>>("/api/v1/books?search=Secret", ApiFactory.Json);
        bBooks!.TotalCount.Should().Be(0);

        // B cannot sell A's book even with a valid id
        var sell = await b.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices",
            new CreateInvoiceRequest(null, null, null, null, DiscountType.None, 0, null, new() { new(bookA.Id, 1, 100m, 0) }, new(PaymentMethod.Cash, 100m, null)), Guid.NewGuid().ToString()));
        sell.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // B cannot update A's book
        var upd = await b.Client.PutAsJsonAsync($"/api/v1/books/{bookA.Id}",
            new SaveBookRequest(null, null, "Hacked", null, null, null, null, null, null, null, null, "Tamil", null, 1, 1, 1, "4901", 0, 1, true, null, null), ApiFactory.Json);
        upd.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A still sees its own data intact
        var still = await a.Client.GetFromJsonAsync<BookDto>($"/api/v1/books/{bookA.Id}", ApiFactory.Json);
        still!.Title.Should().Be("Secret A");
        still.QuantityOnHand.Should().Be(4);
    }

    [SqlServerFact]
    public async Task Invoice_numbers_are_independent_per_tenant()
    {
        var a = await _factory.RegisterTenantAsync();
        var b = await _factory.RegisterTenantAsync();
        var ba = await a.CreateBookAsync("A1", 10m, 10);
        var bb = await b.CreateBookAsync("B1", 10m, 10);
        var ia = await (await a.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices",
            new CreateInvoiceRequest(null, null, null, null, DiscountType.None, 0, null, new() { new(ba.Id, 1, 10m, 0) }, new(PaymentMethod.Cash, 10m, null)), Guid.NewGuid().ToString()))).Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json);
        var ib = await (await b.Client.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices",
            new CreateInvoiceRequest(null, null, null, null, DiscountType.None, 0, null, new() { new(bb.Id, 1, 10m, 0) }, new(PaymentMethod.Cash, 10m, null)), Guid.NewGuid().ToString()))).Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json);
        ia!.InvoiceNumber.Should().EndWith("/000001");
        ib!.InvoiceNumber.Should().EndWith("/000001");
    }
}

[Collection("api")]
public class AuthTests
{
    private readonly ApiFactory _factory;
    public AuthTests(ApiFactory factory) => _factory = factory;

    [SqlServerFact]
    public async Task Anonymous_requests_are_rejected()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/api/v1/books")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SqlServerFact]
    public async Task Wrong_password_is_403_and_refresh_rotation_detects_reuse()
    {
        var s = await _factory.RegisterTenantAsync();
        var anon = _factory.CreateClient();
        var bad = await anon.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(s.Auth.User.Email, "wrong-password"), ApiFactory.Json);
        bad.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var r1 = await anon.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(s.Auth.RefreshToken), ApiFactory.Json);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await r1.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json))!;

        // Replaying the old token is reuse → whole family revoked.
        var replay = await anon.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(s.Auth.RefreshToken), ApiFactory.Json);
        replay.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var afterRevoke = await anon.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(rotated.RefreshToken), ApiFactory.Json);
        afterRevoke.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SqlServerFact]
    public async Task Billing_employee_cannot_manage_users_or_cancel_invoices_but_can_bill()
    {
        var owner = await _factory.RegisterTenantAsync();
        var email = $"cashier-{Guid.NewGuid():N}@test.local";
        var created = await owner.Client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest(email, "Cashier", "Password123!", UserRole.BillingEmployee, null), ApiFactory.Json);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var book = await owner.CreateBookAsync("Cashier book", 10m, 10);

        var anon = _factory.CreateClient();
        var login = await anon.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"), ApiFactory.Json);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json))!;
        var cashier = _factory.CreateClient();
        cashier.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        (await cashier.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cashier.PostAsJsonAsync("/api/v1/books", new SaveBookRequest(null, null, "X", null, null, null, null, null, null, null, null, "Tamil", null, 1, 1, 1, "4901", 0, 1, true, null, null), ApiFactory.Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cashier.GetAsync("/api/v1/reports/dashboard")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var sale = await cashier.SendAsync(TestHelpers.Json(HttpMethod.Post, "/api/v1/sales/invoices",
            new CreateInvoiceRequest(null, null, null, null, DiscountType.None, 0, null, new() { new(book.Id, 1, 10m, 0) }, new(PaymentMethod.Cash, 10m, null)), Guid.NewGuid().ToString()));
        sale.StatusCode.Should().Be(HttpStatusCode.Created);
        var inv = (await sale.Content.ReadFromJsonAsync<InvoiceDto>(ApiFactory.Json))!;
        (await cashier.PostAsJsonAsync($"/api/v1/sales/invoices/{inv.Id}/cancel", new CancelInvoiceRequest("x"), ApiFactory.Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SqlServerFact]
    public async Task Validation_errors_come_back_as_problem_details()
    {
        var s = await _factory.RegisterTenantAsync();
        var res = await s.Client.PostAsJsonAsync("/api/v1/books",
            new SaveBookRequest(null, null, "", null, null, null, null, null, null, null, null, "Tamil", null, -1, 1, 1, "4901", 99, 1, true, null, null), ApiFactory.Json);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("Title").And.Contain("GstRate").And.Contain("traceId");
    }
}

[Collection("api")]
public class PurchasingTests
{
    private readonly ApiFactory _factory;
    public PurchasingTests(ApiFactory factory) => _factory = factory;

    [SqlServerFact]
    public async Task Purchase_order_partial_receipt_updates_stock_status_and_supplier_balance()
    {
        var s = await _factory.RegisterTenantAsync();
        var book = await s.CreateBookAsync("PO book", 300m, 0);
        var supRes = await s.Client.PostAsJsonAsync("/api/v1/suppliers", new SaveSupplierRequest("Vanathi Pathippagam", "Mr. X", "9000000002", null, null, null, true, null), ApiFactory.Json);
        supRes.StatusCode.Should().Be(HttpStatusCode.Created, await supRes.Content.ReadAsStringAsync());
        var sup = (await supRes.Content.ReadFromJsonAsync<SupplierDto>(ApiFactory.Json))!;

        var poRes = await s.Client.PostAsJsonAsync("/api/v1/purchase-orders",
            new CreatePurchaseOrderRequest(sup.Id, null, null, "First order", new() { new(book.Id, 10, 200m) }, Submit: true), ApiFactory.Json);
        poRes.StatusCode.Should().Be(HttpStatusCode.Created, await poRes.Content.ReadAsStringAsync());
        var po = (await poRes.Content.ReadFromJsonAsync<PurchaseOrderDto>(ApiFactory.Json))!;
        po.PoNumber.Should().StartWith("PO/");
        po.Status.Should().Be(PurchaseOrderStatus.Submitted);
        po.Total.Should().Be(2000m);

        var grnRes = await s.Client.PostAsJsonAsync($"/api/v1/purchase-orders/{po.Id}/receipts",
            new ReceiveGoodsRequest(null, "SUP-INV-1", null, null, new() { new(book.Id, 4, null) }), ApiFactory.Json);
        grnRes.StatusCode.Should().Be(HttpStatusCode.Created, await grnRes.Content.ReadAsStringAsync());
        var grn = (await grnRes.Content.ReadFromJsonAsync<GoodsReceiptDto>(ApiFactory.Json))!;
        grn.Total.Should().Be(800m);

        var poAfter = await s.Client.GetFromJsonAsync<PurchaseOrderDto>($"/api/v1/purchase-orders/{po.Id}", ApiFactory.Json);
        poAfter!.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        poAfter.Items[0].QuantityReceived.Should().Be(4);

        var bookAfter = await s.Client.GetFromJsonAsync<BookDto>($"/api/v1/books/{book.Id}", ApiFactory.Json);
        bookAfter!.QuantityOnHand.Should().Be(4);
        bookAfter.PurchasePrice.Should().Be(200m);

        // Over-receipt is rejected
        var over = await s.Client.PostAsJsonAsync($"/api/v1/purchase-orders/{po.Id}/receipts", new ReceiveGoodsRequest(null, null, null, null, new() { new(book.Id, 7, null) }), ApiFactory.Json);
        over.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var supAfter = await s.Client.GetFromJsonAsync<SupplierDto>($"/api/v1/suppliers/{sup.Id}", ApiFactory.Json);
        supAfter!.Outstanding.Should().Be(800m);

        var pay = await s.Client.PostAsJsonAsync($"/api/v1/suppliers/{sup.Id}/payments", new SupplierPaymentRequest(500m, PaymentMethod.BankTransfer, null, "NEFT1", null, grn.Id), ApiFactory.Json);
        pay.StatusCode.Should().Be(HttpStatusCode.Created);
        var supPaid = await s.Client.GetFromJsonAsync<SupplierDto>($"/api/v1/suppliers/{sup.Id}", ApiFactory.Json);
        supPaid!.Outstanding.Should().Be(300m);
    }

    [SqlServerFact]
    public async Task Excel_import_creates_books_and_opening_stock()
    {
        var s = await _factory.RegisterTenantAsync();
        var template = await s.Client.GetByteArrayAsync("/api/v1/books/import/template");
        template.Length.Should().BeGreaterThan(1000);

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Books");
        var headers = Application.Features.Catalog.CatalogService.ImportHeaders;
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Cell(2, 1).Value = "Imported One"; ws.Cell(2, 2).Value = "9780000000001"; ws.Cell(2, 4).Value = "Author A"; ws.Cell(2, 6).Value = "Kids";
        ws.Cell(2, 9).Value = 50; ws.Cell(2, 10).Value = 80; ws.Cell(2, 14).Value = 7;
        ws.Cell(3, 1).Value = "Imported Two"; ws.Cell(3, 2).Value = "9780000000002"; ws.Cell(3, 9).Value = 10; ws.Cell(3, 10).Value = 20; ws.Cell(3, 14).Value = 0;
        ws.Cell(4, 1).Value = ""; // skipped
        using var ms = new MemoryStream(); wb.SaveAs(ms);

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ms.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "books.xlsx");
        var res = await s.Client.PostAsync("/api/v1/books/import", content);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>(ApiFactory.Json))!;
        result.Created.Should().Be(2);
        result.Skipped.Should().Be(0);

        var lookup = await s.Client.GetFromJsonAsync<BookLookupDto>("/api/v1/books/lookup?code=9780000000001", ApiFactory.Json);
        lookup!.Title.Should().Be("Imported One");
        lookup.QuantityOnHand.Should().Be(7);
        lookup.AuthorName.Should().Be("Author A");
    }
}
