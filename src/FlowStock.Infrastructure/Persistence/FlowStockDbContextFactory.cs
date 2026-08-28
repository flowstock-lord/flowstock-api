using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlowStock.Infrastructure.Persistence;

/// <summary>
/// Used by "dotnet ef" at design time. Override the connection with the
/// FLOWSTOCK_CONNECTIONSTRING environment variable when needed.
/// </summary>
public class FlowStockDbContextFactory : IDesignTimeDbContextFactory<FlowStockDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=flowstock;Username=flowstock;Password=flowstock";

    public FlowStockDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("FLOWSTOCK_CONNECTIONSTRING")
            ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<FlowStockDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new FlowStockDbContext(options);
    }
}
