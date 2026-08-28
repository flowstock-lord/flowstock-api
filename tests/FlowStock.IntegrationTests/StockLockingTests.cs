using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Warehouses;
using FlowStock.Infrastructure.Persistence;
using FlowStock.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FlowStock.IntegrationTests;

/// <summary>
/// Proves the mechanism the whole of Phase 4 rests on: while one transaction holds a stock
/// balance, a second one that wants the same balance waits for it instead of reading the value
/// it is about to overwrite (docs/PLAN.md, section 28).
///
/// The end-to-end confirmation tests cannot show this on their own — a confirmation is fast
/// enough that two HTTP requests may simply not overlap — so the contention is created here
/// directly, on two real connections.
/// </summary>
[Collection(ApiCollection.Name)]
public class StockLockingTests(FlowStockApiFactory factory)
{
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    /// <summary>A scope of its own, so each context gets its own database connection.</summary>
    private (IServiceScope Scope, FlowStockDbContext Db) NewContext()
    {
        var scope = factory.Services.CreateScope();

        return (scope, scope.ServiceProvider.GetRequiredService<FlowStockDbContext>());
    }

    private async Task<StockKey> ArrangeBalanceAsync()
    {
        var (scope, db) = NewContext();
        using var _ = scope;

        var unit = new UnitOfMeasure { Code = Unique("u"), Name = "Kilogram" };
        var product = new Product
        {
            Sku = Unique("SKU"), Name = "Flour", ProductType = ProductType.RawMaterial, UnitOfMeasure = unit
        };
        var warehouse = new Warehouse
        {
            Code = Unique("LOCK"), Name = "Lock test", WarehouseType = WarehouseType.General
        };
        var location = new StorageLocation { Warehouse = warehouse, Code = "A-01", Name = "Rack A-01" };

        db.UnitsOfMeasure.Add(unit);
        db.Products.Add(product);
        db.Warehouses.Add(warehouse);
        db.StorageLocations.Add(location);

        // The balance is committed up front on purpose. If it were created inside the test, the
        // second reader would block on the first one's uncommitted insert instead of on the row
        // lock, and the test would pass whether or not FOR UPDATE is there.
        db.Stocks.Add(new Stock { ProductId = product.Id, LocationId = location.Id });

        await db.SaveChangesAsync();

        return new StockKey(product.Id, location.Id);
    }

    [Fact]
    public async Task A_locked_balance_makes_the_second_reader_wait_and_then_see_the_new_quantity()
    {
        var key = await ArrangeBalanceAsync();
        var keys = new[] { key };

        var (holderScope, holder) = NewContext();
        using var _ = holderScope;

        await using var holderTransaction = await holder.BeginTransactionAsync();

        var held = await holder.LockStockAsync(keys, default);
        held.Single().Quantity = 100m;
        await holder.SaveChangesAsync();

        var (waiterScope, waiter) = NewContext();
        using var __ = waiterScope;

        await using var waiterTransaction = await waiter.BeginTransactionAsync();

        var waiting = Task.Run(async () => (await waiter.LockStockAsync(keys, default)).Single().Quantity);

        // The lock is held, so the second reader must not get an answer yet. Without FOR UPDATE it
        // would return here immediately — with the stale quantity of zero.
        var finishedEarly = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.NotSame(waiting, finishedEarly);

        await holderTransaction.CommitAsync();

        // Released: the waiter now reads what the first transaction actually left behind.
        Assert.Equal(100m, await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
