using BookFlow.API.Auth;
using BookFlow.Application.Features.Auth;
using BookFlow.Application.Features.Tenants;
using BookFlow.Application.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookFlow.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    public AuthController(IAuthService auth) => _auth = auth;

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost("register-tenant"), AllowAnonymous, EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<AuthResponse>> RegisterTenant(RegisterTenantRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _auth.RegisterTenantAsync(request, Ip, ct));

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) => Ok(await _auth.LoginAsync(request, Ip, ct));

    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct) => Ok(await _auth.RefreshAsync(request, Ip, ct));

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<AuthResponse>> Me(CancellationToken ct) => Ok(await _auth.MeAsync(ct));

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await _auth.ChangePasswordAsync(request, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = Policies.Owner)]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _users;
    public UsersController(IUserService users) => _users = users;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
        => Ok(await _users.ListAsync(search, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken ct) => Ok(await _users.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var dto = await _users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct) => Ok(await _users.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) { await _users.SetActiveAsync(id, true, ct); return NoContent(); }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) { await _users.SetActiveAsync(id, false, ct); return NoContent(); }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct) { await _users.ResetPasswordAsync(id, request, ct); return NoContent(); }
}

[ApiController]
[Route("api/v1")]
public sealed class TenantController : ControllerBase
{
    private readonly ITenantService _tenant;
    public TenantController(ITenantService tenant) => _tenant = tenant;

    [HttpGet("tenant"), Authorize(Policy = Policies.AnyStaff)]
    public async Task<ActionResult<TenantDto>> Get(CancellationToken ct) => Ok(await _tenant.GetAsync(ct));

    [HttpPut("tenant"), Authorize(Policy = Policies.Owner)]
    public async Task<ActionResult<TenantDto>> Update(UpdateTenantRequest request, CancellationToken ct) => Ok(await _tenant.UpdateAsync(request, ct));

    [HttpGet("stores"), Authorize(Policy = Policies.AnyStaff)]
    public async Task<IActionResult> Stores(CancellationToken ct) => Ok(await _tenant.ListStoresAsync(ct));
}
