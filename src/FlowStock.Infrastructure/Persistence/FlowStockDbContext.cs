using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Common;
using FlowStock.Domain.Users;
using FlowStock.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace FlowStock.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the FlowStock database. Entity sets are added phase by phase;
/// every schema change must ship with a migration.
/// </summary>
public class FlowStockDbContext(
    DbContextOptions<FlowStockDbContext> options,
    ICurrentUser? currentUser = null,
    TimeProvider? timeProvider = null) : DbContext(options), IFlowStockDbContext
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity configurations live next to this context and are picked up automatically.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowStockDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditStamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditStamps();
        return base.SaveChanges();
    }

    /// <summary>Stamps CreatedAt/CreatedBy and UpdatedAt/UpdatedBy in UTC. See docs/PLAN.md, section 29.</summary>
    private void ApplyAuditStamps()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var userId = currentUser?.UserId;

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= userId;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;
            }
        }
    }
}
