using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookFlow.Application.Features.Auth;
using BookFlow.Application.Features.Catalog;
using BookFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookFlow.IntegrationTests;

/// <summary>Runs only when BOOKFLOW_TEST_CONNECTION points at a SQL Server (CI starts one as a service container).</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BOOKFLOW_TEST_CONNECTION")))
            Skip = "Set BOOKFLOW_TEST_CONNECTION to run integration tests against SQL Server.";
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("BOOKFLOW_TEST_CONNECTION") ?? "Server=localhost;Database=BookFlowTests;Integrated Security=true";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:Key", "integration-tests-key-0123456789-0123456789-abcdef");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        // Every test registers its own tenant from the same in-memory client, so the production auth limit (10/min) would throttle the suite.
        builder.UseSetting("RateLimiting:AuthPermitLimit", "1000");
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await DatabaseInitializer.InitializeAsync(db, scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("Tests"));
    }
}

public sealed class TenantSession
{
    public HttpClient Client { get; init; } = default!;
    public AuthResponse Auth { get; init; } = default!;
    public Guid TenantId => Auth.Tenant.Id;
}

public static class TestHelpers
{
    private static int _counter;

    public static async Task<TenantSession> RegisterTenantAsync(this ApiFactory factory, string? name = null)
    {
        var n = Interlocked.Increment(ref _counter);
        var client = factory.CreateClient();
        var req = new RegisterTenantRequest(name ?? $"Shop {n} {Guid.NewGuid():N}", "Owner", $"owner{n}-{Guid.NewGuid():N}@test.local", "Password123!", "9876543210", null, "Chennai", "33");
        var res = await client.PostAsJsonAsync("/api/v1/auth/register-tenant", req, ApiFactory.Json);
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new TenantSession { Client = client, Auth = auth };
    }

    public static async Task<BookDto> CreateBookAsync(this TenantSession s, string title, decimal price, int openingStock, decimal gst = 0)
    {
        var req = new SaveBookRequest(null, null, title, null, null, "Kalki", null, "Vanathi", null, "Novel", null, "Tamil", null, price * 0.7m, price, price, "4901", gst, 2, true, openingStock, null);
        var res = await s.Client.PostAsJsonAsync("/api/v1/books", req, ApiFactory.Json);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BookDto>(ApiFactory.Json))!;
    }

    public static HttpRequestMessage Json(HttpMethod method, string url, object body, string? idempotencyKey = null)
    {
        var msg = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: ApiFactory.Json) };
        if (idempotencyKey is not null) msg.Headers.Add("Idempotency-Key", idempotencyKey);
        return msg;
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>, ICollectionFixture<TestDatabaseReset> { }

/// <summary>Resets the database once per test run.</summary>
public sealed class TestDatabaseReset : IAsyncLifetime 
{ 
    private readonly ApiFactory _factory = new();
    public Task InitializeAsync() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BOOKFLOW_TEST_CONNECTION")) ? Task.CompletedTask : _factory.ResetDatabaseAsync();
    public Task DisposeAsync() { _factory.Dispose(); return Task.CompletedTask; }
}
