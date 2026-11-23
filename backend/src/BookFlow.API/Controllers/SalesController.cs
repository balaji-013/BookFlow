using System.Net;
using System.Text;
using BookFlow.API.Auth;
using BookFlow.Application.Features.Finance;
using BookFlow.Application.Features.Sales;
using BookFlow.Application.Features.Tenants;
using BookFlow.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookFlow.API.Controllers;

[ApiController]
[Route("api/v1/sales")]
[Authorize(Policy = Policies.AnyStaff)]
public sealed class SalesController : ControllerBase
{
    private readonly ISalesService _sales;
    private readonly ITenantService _tenant;
    public SalesController(ISalesService sales, ITenantService tenant) { _sales = sales; _tenant = tenant; }

    [HttpPost("invoices"), Authorize(Policy = Policies.Billing)]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<InvoiceDto>> Create(CreateInvoiceRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var (invoice, replayed) = await _sales.CreateInvoiceAsync(request, idempotencyKey ?? string.Empty, ct);
        if (replayed) Response.Headers["Idempotent-Replay"] = "true";
        return replayed ? Ok(invoice) : CreatedAtAction(nameof(Get), new { id = invoice.Id }, invoice);
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? customerId, [FromQuery] InvoiceStatus? status,
        [FromQuery] PaymentStatus? paymentStatus, [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _sales.ListInvoicesAsync(from, to, customerId, status, paymentStatus, search, page, pageSize, ct));

    [HttpGet("invoices/{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> Get(Guid id, CancellationToken ct) => Ok(await _sales.GetInvoiceAsync(id, ct));

    [HttpGet("invoices/{id:guid}/print")]
    [Produces("text/html")]
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        var inv = await _sales.GetInvoiceAsync(id, ct);
        var tenant = await _tenant.GetAsync(ct);
        return Content(InvoiceHtmlRenderer.Render(inv, tenant), "text/html", Encoding.UTF8);
    }

    [HttpPost("invoices/{id:guid}/cancel"), Authorize(Policy = Policies.Manager)]
    public async Task<ActionResult<InvoiceDto>> Cancel(Guid id, CancelInvoiceRequest request, CancellationToken ct) => Ok(await _sales.CancelInvoiceAsync(id, request, ct));

    [HttpPost("returns"), Authorize(Policy = Policies.Billing)]
    public async Task<ActionResult<SalesReturnDto>> CreateReturn(CreateSalesReturnRequest request, CancellationToken ct)
    {
        var dto = await _sales.CreateReturnAsync(request, ct);
        return CreatedAtAction(nameof(GetReturn), new { id = dto.Id }, dto);
    }

    [HttpGet("returns")]
    public async Task<IActionResult> ListReturns([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _sales.ListReturnsAsync(from, to, page, pageSize, ct));

    [HttpGet("returns/{id:guid}")]
    public async Task<ActionResult<SalesReturnDto>> GetReturn(Guid id, CancellationToken ct) => Ok(await _sales.GetReturnAsync(id, ct));
}

[ApiController]
[Route("api/v1")]
[Authorize(Policy = Policies.AnyStaff)]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payments;
    public PaymentsController(IPaymentService payments) => _payments = payments;

    [HttpPost("payments"), Authorize(Policy = Policies.Billing)]
    public async Task<ActionResult<PaymentDto>> Create(CreatePaymentRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var dto = await _payments.CreateAsync(request, idempotencyKey, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpGet("payments")]
    public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? customerId, [FromQuery] PaymentDirection? direction,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _payments.ListAsync(from, to, customerId, direction, page, pageSize, ct));

    [HttpGet("payments/{id:guid}")]
    public async Task<ActionResult<PaymentDto>> Get(Guid id, CancellationToken ct) => Ok(await _payments.GetAsync(id, ct));

    [HttpGet("customers/{customerId:guid}/outstanding")]
    public async Task<IActionResult> Outstanding(Guid customerId, CancellationToken ct) => Ok(await _payments.OutstandingAsync(customerId, ct));

    [HttpGet("credit-notes")]
    public async Task<IActionResult> CreditNotes([FromQuery] Guid? customerId, [FromQuery] bool openOnly = false, [FromQuery] int? page = null, [FromQuery] int? pageSize = null, CancellationToken ct = default)
        => Ok(await _payments.CreditNotesAsync(customerId, openOnly, page, pageSize, ct));
}

/// <summary>Server-side, HTML-encoded invoice for printing (thermal 80 mm friendly and A4 via CSS).</summary>
internal static class InvoiceHtmlRenderer
{
    private static string E(object? v) => WebUtility.HtmlEncode(v?.ToString() ?? string.Empty);
    private static string M(decimal v) => v.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));

    public static string Render(InvoiceDto inv, TenantDto t)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(E(inv.InvoiceNumber)).Append("</title><style>")
          .Append("body{font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#111;margin:0;padding:16px}")
          .Append(".inv{max-width:720px;margin:auto}.hdr{display:flex;justify-content:space-between;border-bottom:2px solid #111;padding-bottom:8px;margin-bottom:8px}")
          .Append("h1{font-size:18px;margin:0}table{width:100%;border-collapse:collapse}th,td{padding:4px 6px;border-bottom:1px solid #ddd;text-align:left}")
          .Append("td.n,th.n{text-align:right;white-space:nowrap}.tot td{border:none;padding:2px 6px}.tot tr.g td{font-weight:bold;border-top:2px solid #111;font-size:14px}")
          .Append(".meta{display:flex;justify-content:space-between;margin-bottom:8px}.muted{color:#555}.cancel{color:#b00020;font-weight:bold;font-size:16px}")
          .Append("@media print{body{padding:0}.noprint{display:none}}@page{margin:10mm}")
          .Append("</style></head><body><div class=\"inv\">");
        sb.Append("<div class=\"hdr\"><div><h1>").Append(E(t.BusinessName)).Append("</h1><div class=\"muted\">")
          .Append(E(string.Join(", ", new[] { t.AddressLine1, t.AddressLine2, t.City, t.State, t.Pincode }.Where(s => !string.IsNullOrWhiteSpace(s)))))
          .Append("</div><div class=\"muted\">").Append(t.Phone is null ? "" : "Ph: " + E(t.Phone)).Append(t.Gstin is null ? "" : " &nbsp; GSTIN: " + E(t.Gstin)).Append("</div></div>")
          .Append("<div style=\"text-align:right\"><div><strong>TAX INVOICE</strong></div><div>").Append(E(inv.InvoiceNumber)).Append("</div><div>").Append(inv.InvoiceDate.ToString("dd-MMM-yyyy")).Append("</div></div></div>");
        if (inv.Status == InvoiceStatus.Cancelled) sb.Append("<div class=\"cancel\">CANCELLED — ").Append(E(inv.CancelReason)).Append("</div>");
        sb.Append("<div class=\"meta\"><div><div class=\"muted\">Bill to</div><div><strong>").Append(E(inv.CustomerName)).Append("</strong></div>")
          .Append(inv.CustomerPhone is null ? "" : "<div>" + E(inv.CustomerPhone) + "</div>").Append(inv.CustomerGstin is null ? "" : "<div>GSTIN: " + E(inv.CustomerGstin) + "</div>")
          .Append("</div><div class=\"muted\" style=\"text-align:right\">").Append(inv.IsInterState ? "Inter-state supply (IGST)" : "Intra-state supply (CGST+SGST)").Append("</div></div>");
        sb.Append("<table><thead><tr><th>#</th><th>Item</th><th>HSN</th><th class=\"n\">Qty</th><th class=\"n\">Rate</th><th class=\"n\">Disc</th><th class=\"n\">Taxable</th><th class=\"n\">GST</th><th class=\"n\">Total</th></tr></thead><tbody>");
        var i = 1;
        foreach (var it in inv.Items)
        {
            sb.Append("<tr><td>").Append(i++).Append("</td><td>").Append(E(it.Title)).Append(it.Isbn is null ? "" : "<br><span class=\"muted\">" + E(it.Isbn) + "</span>")
              .Append("</td><td>").Append(E(it.HsnCode)).Append("</td><td class=\"n\">").Append(it.Quantity).Append("</td><td class=\"n\">").Append(M(it.UnitPrice))
              .Append("</td><td class=\"n\">").Append(M(it.DiscountAmount)).Append("</td><td class=\"n\">").Append(M(it.TaxableAmount))
              .Append("</td><td class=\"n\">").Append(it.GstRate.ToString("0.##")).Append("%</td><td class=\"n\">").Append(M(it.LineTotal)).Append("</td></tr>");
        }
        sb.Append("</tbody></table><table class=\"tot\" style=\"width:320px;margin-left:auto;margin-top:8px\">");
        Row(sb, "Sub total", inv.SubTotal);
        if (inv.DiscountTotal > 0) Row(sb, "Discount", -inv.DiscountTotal);
        Row(sb, "Taxable value", inv.TaxableTotal);
        if (inv.CgstTotal > 0) Row(sb, "CGST", inv.CgstTotal);
        if (inv.SgstTotal > 0) Row(sb, "SGST", inv.SgstTotal);
        if (inv.IgstTotal > 0) Row(sb, "IGST", inv.IgstTotal);
        if (inv.RoundOff != 0) Row(sb, "Round off", inv.RoundOff);
        sb.Append("<tr class=\"g\"><td>Grand total</td><td class=\"n\">₹ ").Append(M(inv.GrandTotal)).Append("</td></tr>");
        Row(sb, "Paid", inv.AmountPaid);
        if (inv.AmountDue > 0) Row(sb, "Balance due", inv.AmountDue);
        sb.Append("</table>");
        if (inv.Payments.Count > 0)
        {
            sb.Append("<div class=\"muted\" style=\"margin-top:8px\">Payments: ");
            sb.Append(E(string.Join("; ", inv.Payments.Select(p => $"{p.Method} ₹{M(p.Amount)}{(p.Reference is null ? "" : " (" + p.Reference + ")")}"))));
            sb.Append("</div>");
        }
        if (!string.IsNullOrWhiteSpace(t.InvoiceFooter)) sb.Append("<p class=\"muted\" style=\"margin-top:16px;white-space:pre-line\">").Append(E(t.InvoiceFooter)).Append("</p>");
        sb.Append("<p class=\"muted\" style=\"text-align:center;margin-top:16px\">Thank you for shopping with us • நன்றி</p>");
        sb.Append("<div class=\"noprint\" style=\"text-align:center;margin-top:12px\"><button onclick=\"window.print()\">Print</button></div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string label, decimal value) =>
        sb.Append("<tr><td>").Append(E(label)).Append("</td><td class=\"n\">").Append(M(value)).Append("</td></tr>");
}
