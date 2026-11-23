using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BookFlow.Application.Common.Interfaces;
using BookFlow.Domain.Entities.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BookFlow.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "BookFlow";
    public string Audience { get; set; } = "BookFlow.Client";
    public int AccessTokenMinutes { get; set; } = 15;
}

public static class BookFlowClaims
{
    public const string TenantId = "tenant_id";
    public const string StoreId = "store_id";
    public const string Role = ClaimTypes.Role;
}

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;

    public JwtTokenService(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value; _clock = clock;
        if (string.IsNullOrWhiteSpace(_options.Key) || Encoding.UTF8.GetByteCount(_options.Key) < 32)
            throw new InvalidOperationException("Jwt:Key must be configured with at least 32 bytes (256 bits).");
    }

    public (string token, DateTime expiresAt) CreateAccessToken(User user, Tenant tenant)
    {
        var expires = _clock.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, user.FullName),
            new(BookFlowClaims.Role, user.Role.ToString()),
            new(BookFlowClaims.TenantId, tenant.Id.ToString()),
        };
        if (user.StoreId.HasValue) claims.Add(new Claim(BookFlowClaims.StoreId, user.StoreId.Value.ToString()));

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, notBefore: _clock.UtcNow, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class PasswordService : IPasswordService
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<object> _hasher = new();
    private static readonly object Dummy = new();

    public string Hash(string password) => _hasher.HashPassword(Dummy, password);

    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        var result = _hasher.VerifyHashedPassword(Dummy, hash, password);
        return result is Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success or Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded;
    }
}
