using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FlowStock.IntegrationTests;

/// <summary>
/// Phase 0 smoke test: the API host boots and reports liveness.
/// Readiness (/health/ready) requires PostgreSQL and is covered by database-backed tests.
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ConnectionStrings:FlowStockDb",
                "Host=localhost;Port=5432;Database=flowstock;Username=flowstock;Password=flowstock")
                .UseSetting("Database:MigrateOnStartup", "false"));
    }

    [Fact]
    public async Task Live_endpoint_returns_healthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body);
    }
}
