using FlowStock.Domain.Catalog;
using FlowStock.Domain.Users;
using FlowStock.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace FlowStock.Application.Common;

/// <summary>
/// The database seen by application services. Keeps EF Core configuration in Infrastructure
/// while application logic stays testable and free of persistence details.
/// </summary>
public interface IFlowStockDbContext
{
    DbSet<User> Users { get; }

    DbSet<Role> Roles { get; }

    DbSet<UserRole> UserRoles { get; }

    DbSet<Product> Products { get; }

    DbSet<UnitOfMeasure> UnitsOfMeasure { get; }

    DbSet<Warehouse> Warehouses { get; }

    DbSet<StorageLocation> StorageLocations { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
