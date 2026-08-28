using Microsoft.EntityFrameworkCore;

namespace FlowStock.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the FlowStock database. Entity sets are added phase by phase;
/// every schema change must ship with a migration.
/// </summary>
public class FlowStockDbContext(DbContextOptions<FlowStockDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity configurations live next to this context and are picked up automatically.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowStockDbContext).Assembly);
    }
}
