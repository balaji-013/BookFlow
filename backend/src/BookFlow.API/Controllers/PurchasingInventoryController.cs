using BookFlow.API.Auth;
using BookFlow.Application.Features.Inventory;
using BookFlow.Application.Features.Purchasing;
using BookFlow.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookFlow.API.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize(Policy = Policies.Inventory)]
public sealed class PurchasingController : ControllerBase
{
    private readonly IPurchasingService _purchasing;
    public PurchasingController(IPurchasingService purchasing) => _purchasing = purchasing;

    [HttpGet("purchase-orders")]
    public async Task<IActionResult> ListOrders([FromQuery] Guid? supplierId, [FromQuery] PurchaseOrderStatus? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _purchasing.ListOrdersAsync(supplierId, status, page, pageSize, ct));

    [HttpGet("purchase-orders/{id:guid}")]
    public async Task<ActionResult<PurchaseOrderDto>> GetOrder(Guid id, CancellationToken ct) => Ok(await _purchasing.GetOrderAsync(id, ct));

    [HttpPost("purchase-orders")]
    public async Task<ActionResult<PurchaseOrderDto>> CreateOrder(CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        var dto = await _purchasing.CreateOrderAsync(request, ct);
        return CreatedAtAction(nameof(GetOrder), new { id = dto.Id }, dto);
    }

    [HttpPost("purchase-orders/{id:guid}/submit")]
    public async Task<ActionResult<PurchaseOrderDto>> Submit(Guid id, CancellationToken ct) => Ok(await _purchasing.SubmitOrderAsync(id, ct));

    [HttpPost("purchase-orders/{id:guid}/cancel")]
    public async Task<ActionResult<PurchaseOrderDto>> Cancel(Guid id, CancellationToken ct) => Ok(await _purchasing.CancelOrderAsync(id, ct));

    [HttpPost("purchase-orders/{id:guid}/receipts")]
    public async Task<ActionResult<GoodsReceiptDto>> Receive(Guid id, ReceiveGoodsRequest request, CancellationToken ct)
    {
        var dto = await _purchasing.ReceiveAsync(id, request, ct);
        return CreatedAtAction(nameof(GetReceipt), new { id = dto.Id }, dto);
    }

    [HttpPost("goods-receipts")]
    public async Task<ActionResult<GoodsReceiptDto>> DirectReceipt(DirectReceiptRequest request, CancellationToken ct)
    {
        var dto = await _purchasing.DirectReceiptAsync(request, ct);
        return CreatedAtAction(nameof(GetReceipt), new { id = dto.Id }, dto);
    }

    [HttpGet("goods-receipts")]
    public async Task<IActionResult> ListReceipts([FromQuery] Guid? supplierId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _purchasing.ListReceiptsAsync(supplierId, from, to, page, pageSize, ct));

    [HttpGet("goods-receipts/{id:guid}")]
    public async Task<ActionResult<GoodsReceiptDto>> GetReceipt(Guid id, CancellationToken ct) => Ok(await _purchasing.GetReceiptAsync(id, ct));

    [HttpPost("purchase-returns")]
    public async Task<ActionResult<PurchaseReturnDto>> CreateReturn(PurchaseReturnRequest request, CancellationToken ct)
        => StatusCode(201, await _purchasing.CreateReturnAsync(request, ct));

    [HttpGet("purchase-returns")]
    public async Task<IActionResult> ListReturns([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) => Ok(await _purchasing.ListReturnsAsync(page, pageSize, ct));
}

[ApiController]
[Route("api/v1/inventory")]
[Authorize(Policy = Policies.AnyStaff)]
public sealed class InventoryController : ControllerBase
{
    private readonly IInventoryQueryService _inventory;
    public InventoryController(IInventoryQueryService inventory) => _inventory = inventory;

    [HttpGet("stock")]
    public async Task<IActionResult> Stock([FromQuery] string? search, [FromQuery] bool lowStock = false, [FromQuery] int? page = null, [FromQuery] int? pageSize = null, CancellationToken ct = default)
        => Ok(await _inventory.StockAsync(search, lowStock, page, pageSize, ct));

    [HttpGet("movements")]
    public async Task<IActionResult> Movements([FromQuery] Guid? bookId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] MovementType? type, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _inventory.MovementsAsync(bookId, from, to, type, page, pageSize, ct));

    [HttpGet("low-stock")]
    public async Task<IActionResult> LowStock(CancellationToken ct) => Ok(await _inventory.LowStockAsync(ct));

    [HttpGet("valuation"), Authorize(Policy = Policies.Manager)]
    public async Task<ActionResult<ValuationDto>> Valuation(CancellationToken ct) => Ok(await _inventory.ValuationAsync(ct));

    [HttpPost("adjustments"), Authorize(Policy = Policies.Inventory)]
    public async Task<ActionResult<StockAdjustmentDto>> Adjust(StockAdjustmentRequest request, CancellationToken ct) => StatusCode(201, await _inventory.AdjustAsync(request, ct));

    [HttpGet("adjustments"), Authorize(Policy = Policies.Inventory)]
    public async Task<IActionResult> Adjustments([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) => Ok(await _inventory.AdjustmentsAsync(page, pageSize, ct));
}
