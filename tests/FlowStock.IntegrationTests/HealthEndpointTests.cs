using System.Net;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

/// <summary>
/// Liveness says the process is up; readiness additionally proves the database is reachable.
/// </summary>
[Collection(ApiCollection.Name)]
public class HealthEndpointTests(FlowStockApiFactory factory)
{
    [Fact]
    public async Task Live_endpoint_returns_healthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_endpoint_reports_the_database()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
