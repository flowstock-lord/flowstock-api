using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Notifications;
using FlowStock.Domain.Production;
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

    DbSet<Stock> Stocks { get; }

    DbSet<Batch> Batches { get; }

    DbSet<StockMovement> StockMovements { get; }

    DbSet<StockMovementLine> StockMovementLines { get; }

    DbSet<BillOfMaterial> BillsOfMaterial { get; }

    DbSet<BillOfMaterialItem> BillOfMaterialItems { get; }

    DbSet<ProductionOrder> ProductionOrders { get; }

    DbSet<ProductionOrderMaterial> ProductionOrderMaterials { get; }

    DbSet<Notification> Notifications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a database transaction so a whole inventory operation succeeds or rolls back
    /// together (docs/PLAN.md, section 3.5).
    ///
    /// If a transaction is already open, the returned handle joins it instead of starting a
    /// second one: a production order posts its movements inside its own transaction, and the
    /// whole operation must still commit or roll back as one unit.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the stock balance of every requested product/location pair, creating the rows that
    /// do not exist yet and holding a write lock on all of them until the current transaction
    /// ends. This is what makes concurrent inventory operations safe: a second operation touching
    /// the same balance waits, then reads the already-updated quantity instead of the stale one
    /// (docs/PLAN.md, section 28).
    /// </summary>
    Task<IReadOnlyList<Stock>> LockStockAsync(
        IReadOnlyCollection<StockKey> keys,
        CancellationToken cancellationToken);

    /// <summary>
    /// The next stock movement document number. Numbers come from a database sequence, so they
    /// stay unique and ascending even when several users create movements at once.
    /// </summary>
    Task<long> NextMovementNumberAsync(CancellationToken cancellationToken);

    /// <summary>The next production order number, from its own database sequence.</summary>
    Task<long> NextProductionOrderNumberAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A database transaction spanning several SaveChanges calls. Rolls back unless committed.
/// A handle that joined an already-open transaction commits nothing of its own — the operation
/// that opened the transaction owns it.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
