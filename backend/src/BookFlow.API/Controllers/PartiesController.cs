using BookFlow.API.Auth;
using BookFlow.Application.Features.Parties;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookFlow.API.Controllers;

[ApiController]
[Route("api/v1/customers")]
[Authorize(Policy = Policies.AnyStaff)]
public sealed class CustomersController : ControllerBase
{
    private readonly IPartiesService _parties;
    public CustomersController(IPartiesService parties) => _parties = parties;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] bool includeInactive = false, [FromQuery] int? page = null, [FromQuery] int? pageSize = null, CancellationToken ct = default)
        => Ok(await _parties.ListCustomersAsync(search, includeInactive, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> Get(Guid id, CancellationToken ct) => Ok(await _parties.GetCustomerAsync(id, ct));

    [HttpPost, Authorize(Policy = Policies.Billing)]
    public async Task<ActionResult<CustomerDto>> Create(SaveCustomerRequest request, CancellationToken ct)
    {
        var dto = await _parties.CreateCustomerAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Billing)]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, SaveCustomerRequest request, CancellationToken ct) => Ok(await _parties.UpdateCustomerAsync(id, request, ct));

    [HttpGet("{id:guid}/statement")]
    public async Task<ActionResult<StatementDto>> Statement(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await _parties.CustomerStatementAsync(id, from, to, ct));
}

[ApiController]
[Route("api/v1/suppliers")]
[Authorize(Policy = Policies.Inventory)]
public sealed class SuppliersController : ControllerBase
{
    private readonly IPartiesService _parties;
    public SuppliersController(IPartiesService parties) => _parties = parties;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] bool includeInactive = false, [FromQuery] int? page = null, [FromQuery] int? pageSize = null, CancellationToken ct = default)
        => Ok(await _parties.ListSuppliersAsync(search, includeInactive, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> Get(Guid id, CancellationToken ct) => Ok(await _parties.GetSupplierAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create(SaveSupplierRequest request, CancellationToken ct)
    {
        var dto = await _parties.CreateSupplierAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, SaveSupplierRequest request, CancellationToken ct) => Ok(await _parties.UpdateSupplierAsync(id, request, ct));

    [HttpGet("{id:guid}/statement")]
    public async Task<ActionResult<StatementDto>> Statement(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await _parties.SupplierStatementAsync(id, from, to, ct));

    [HttpPost("{id:guid}/payments"), Authorize(Policy = Policies.Manager)]
    public async Task<IActionResult> Pay(Guid id, SupplierPaymentRequest request, CancellationToken ct)
        => StatusCode(201, new { id = await _parties.RecordSupplierPaymentAsync(id, request, ct) });
}
