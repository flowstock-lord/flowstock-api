using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Common;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Users;
using FlowStock.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

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
    public const string MovementNumberSequence = "StockMovementNumbers";

    /// <summary>Backs the document numbers of the non-PostgreSQL (unit test) provider only.</summary>
    private static long _fallbackMovementNumber;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();

    public DbSet<Stock> Stocks => Set<Stock>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<StockMovementLine> StockMovementLines => Set<StockMovementLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Document numbers come from a sequence rather than a counted query: a sequence hands out
        // distinct values to concurrent callers and does not give them back on rollback.
        modelBuilder.HasSequence<long>(MovementNumberSequence).StartsAt(1).IncrementsBy(1);

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

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => new UnitOfWorkTransaction(await Database.BeginTransactionAsync(cancellationToken));

    public async Task<IReadOnlyList<Stock>> LockStockAsync(
        IReadOnlyCollection<StockKey> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        if (!Database.IsNpgsql())
        {
            return await LockStockInMemoryAsync(keys, cancellationToken);
        }

        var productIds = keys.Select(k => k.ProductId).ToArray();
        var locationIds = keys.Select(k => k.LocationId).ToArray();

        var locked = await LockAsync();

        if (locked.Count == keys.Count)
        {
            return locked;
        }

        // Some balance does not exist yet. ON CONFLICT DO NOTHING makes creating it safe when two
        // operations reach the same new balance at the same instant: one inserts, the other finds
        // the row, and the second lock below then covers them all.
        await Database.ExecuteSqlRawAsync(
            InsertMissingBalancesSql,
            [Products(productIds), Locations(locationIds)],
            cancellationToken);

        return await LockAsync();

        // FOR UPDATE holds the rows until this transaction ends, so a competing operation blocks
        // here and then reads the balance we leave behind rather than the one we started from.
        // The ordering is fixed so two operations touching the same rows cannot deadlock.
        Task<List<Stock>> LockAsync() => Stocks
            .FromSqlRaw(LockBalancesSql, Products(productIds), Locations(locationIds))
            .ToListAsync(cancellationToken);

        static NpgsqlParameter Products(Guid[] ids) => new("products", ids);
        static NpgsqlParameter Locations(Guid[] ids) => new("locations", ids);
    }

    public async Task<long> NextMovementNumberAsync(CancellationToken cancellationToken)
    {
        if (!Database.IsNpgsql())
        {
            return Interlocked.Increment(ref _fallbackMovementNumber);
        }

        return await Database
            .SqlQueryRaw<long>(NextMovementNumberSql)
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// The unit test provider has neither row locks nor upserts. It runs single-threaded, so
    /// fetching and creating the same balances is enough to exercise the service's own logic;
    /// the concurrency guarantee itself is covered by an integration test against PostgreSQL.
    /// </summary>
    private async Task<IReadOnlyList<Stock>> LockStockInMemoryAsync(
        IReadOnlyCollection<StockKey> keys,
        CancellationToken cancellationToken)
    {
        var productIds = keys.Select(k => k.ProductId).ToList();
        var locationIds = keys.Select(k => k.LocationId).ToList();

        var existing = await Stocks
            .Where(s => productIds.Contains(s.ProductId) && locationIds.Contains(s.LocationId))
            .ToListAsync(cancellationToken);

        var stocks = new List<Stock>();

        foreach (var key in keys)
        {
            var stock = existing.FirstOrDefault(
                s => s.ProductId == key.ProductId && s.LocationId == key.LocationId);

            if (stock is null)
            {
                stock = new Stock { ProductId = key.ProductId, LocationId = key.LocationId };
                Stocks.Add(stock);
            }

            stocks.Add(stock);
        }

        return stocks;
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

    private const string InsertMissingBalancesSql =
        """
        INSERT INTO "Stocks" ("Id", "ProductId", "LocationId", "Quantity", "ReservedQuantity", "CreatedAt")
        SELECT gen_random_uuid(), k.product_id, k.location_id, 0, 0, now()
        FROM unnest(@products, @locations) AS k(product_id, location_id)
        ON CONFLICT ("ProductId", "LocationId") DO NOTHING
        """;

    private const string LockBalancesSql =
        """
        SELECT s.* FROM "Stocks" AS s
        JOIN unnest(@products, @locations) AS k(product_id, location_id)
          ON s."ProductId" = k.product_id AND s."LocationId" = k.location_id
        ORDER BY s."ProductId", s."LocationId"
        FOR UPDATE OF s
        """;

    private const string NextMovementNumberSql =
        $"""SELECT nextval('"{MovementNumberSequence}"') AS "Value" """;

    /// <summary>Rolls back unless committed, so a failed inventory operation leaves nothing behind.</summary>
    private sealed class UnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default)
            => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
