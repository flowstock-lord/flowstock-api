using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Warehouses;
using FlowStock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Inventory;

/// <summary>
/// A small warehouse to move stock around in: flour and sugar, a main location and a production
/// line. Every test gets its own database, and <see cref="NewContext"/> opens a fresh context over
/// it so assertions read what was actually saved rather than what a service left tracked.
/// </summary>
public class InventoryFixture
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public InventoryFixture()
    {
        Db = NewContext();

        var kilogram = new UnitOfMeasure { Code = "kg", Name = "Kilogram" };
        var piece = new UnitOfMeasure { Code = "pcs", Name = "Piece" };

        Flour = new Product
        {
            Sku = "FLOUR-001", Name = "Flour", ProductType = ProductType.RawMaterial, UnitOfMeasure = kilogram
        };
        Sugar = new Product
        {
            Sku = "SUGAR-001", Name = "Sugar", ProductType = ProductType.RawMaterial, UnitOfMeasure = kilogram
        };
        Cookies = new Product
        {
            Sku = "COOKIE-001", Name = "Cookies", ProductType = ProductType.FinishedProduct, UnitOfMeasure = piece
        };

        var main = new Warehouse { Code = "MAIN", Name = "Main Warehouse", WarehouseType = WarehouseType.RawMaterials };
        var production = new Warehouse
        {
            Code = "PROD", Name = "Production", WarehouseType = WarehouseType.Production
        };

        MainLocation = new StorageLocation { Warehouse = main, Code = "A-01", Name = "Rack A-01" };
        ProductionLocation = new StorageLocation { Warehouse = production, Code = "LINE-01", Name = "Line 1" };
        ClosedLocation = new StorageLocation
        {
            Warehouse = main, Code = "A-99", Name = "Retired rack", IsActive = false
        };

        Db.UnitsOfMeasure.AddRange(kilogram, piece);
        Db.Products.AddRange(Flour, Sugar, Cookies);
        Db.Warehouses.AddRange(main, production);
        Db.StorageLocations.AddRange(MainLocation, ProductionLocation, ClosedLocation);
        Db.SaveChanges();
    }

    public FlowStockDbContext Db { get; }

    public Product Flour { get; }

    public Product Sugar { get; }

    public Product Cookies { get; }

    public StorageLocation MainLocation { get; }

    public StorageLocation ProductionLocation { get; }

    public StorageLocation ClosedLocation { get; }

    public Guid UserId { get; } = Guid.NewGuid();

    /// <summary>The person behind every operation in a test, as the API's auth would supply one.</summary>
    public ICurrentUser CurrentUser => new TestCurrentUser(UserId);

    public StockMovementService Movements => new(
        Db, CurrentUser, TimeProvider.System, NullLogger<StockMovementService>.Instance);

    public StockService StockReader => new(Db);

    public FlowStockDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<FlowStockDbContext>()
            .UseInMemoryDatabase(_databaseName)
            // The in-memory provider has no transactions. The service opens one anyway, because
            // against PostgreSQL it must; here it is a no-op and the warning is expected.
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        // The context is given the current user so audit stamps land the way they do in the API.
        return new FlowStockDbContext(options, CurrentUser);
    }

    /// <summary>Reads a balance from a fresh context, so it reflects saved state only.</summary>
    public decimal QuantityOf(Product product, StorageLocation location)
    {
        using var db = NewContext();

        return db.Stocks
            .Where(s => s.ProductId == product.Id && s.LocationId == location.Id)
            .Select(s => s.Quantity)
            .SingleOrDefault();
    }

    public CreateStockMovementRequest Receipt(StorageLocation destination, params (Product, decimal)[] lines) =>
        new(MovementType.Receipt, null, destination.Id, "Supplier delivery", Lines(lines));

    public CreateStockMovementRequest Transfer(
        StorageLocation source,
        StorageLocation destination,
        params (Product, decimal)[] lines)
        => new(MovementType.Transfer, source.Id, destination.Id, null, Lines(lines));

    private static List<CreateStockMovementLineRequest> Lines((Product Product, decimal Quantity)[] lines) =>
        lines.Select(line => new CreateStockMovementLineRequest(line.Product.Id, line.Quantity)).ToList();

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
    }
}
