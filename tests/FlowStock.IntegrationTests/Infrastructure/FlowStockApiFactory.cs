using FlowStock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace FlowStock.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the API against a throwaway PostgreSQL container so tests exercise the real provider,
/// real migrations and the real seeded users.
/// </summary>
public class FlowStockApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("flowstock")
        .WithUsername("flowstock")
        .WithPassword("flowstock")
        .Build();

    public const string AdminEmail = "admin@flowstock.local";
    public const string AdminPassword = "Admin123!";
    public const string ViewerEmail = "viewer@flowstock.local";
    public const string ViewerPassword = "Viewer123!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:FlowStockDb", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:Key", "integration-test-signing-key-at-least-32-chars");
    }

    // Explicit implementation: xUnit's IAsyncLifetime returns Task, WebApplicationFactory's
    // own DisposeAsync returns ValueTask, so the two must not share a signature.
    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();

        // Touch the host so migrations and dev seeding run before the first test.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FlowStockDbContext>().Database.CanConnectAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>Shares one container and one host across every integration test class.</summary>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<FlowStockApiFactory>
{
    public const string Name = "api";
}
