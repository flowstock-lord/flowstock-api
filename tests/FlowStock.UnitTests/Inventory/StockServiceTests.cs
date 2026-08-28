using FlowStock.Application.Inventory;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Warehouses;

namespace FlowStock.UnitTests.Inventory;

public class StockServiceTests
{
    private readonly InventoryFixture _fixture = new();

    public StockServiceTests()
    {
        Receive(_fixture.MainLocation, _fixture.Flour, 500m);
        Receive(_fixture.MainLocation, _fixture.Sugar, 40m);
        Receive(_fixture.ProductionLocation, _fixture.Flour, 100m);
    }

    private void Receive(StorageLocation destination, Product product, decimal quantity)
    {
        var movement = _fixture.Movements
            .CreateAsync(_fixture.Receipt(destination, (product, quantity)), default).GetAwaiter().GetResult();

        _fixture.Movements.ConfirmAsync(movement.Id, default).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task A_balance_describes_its_product_and_its_place()
    {
        var page = await _fixture.StockReader.ListAsync(
            new StockQuery { ProductId = _fixture.Sugar.Id }, default);

        var balance = Assert.Single(page.Items);

        Assert.Equal("SUGAR-001", balance.Sku);
        Assert.Equal("kg", balance.UnitOfMeasureCode);
        Assert.Equal("A-01", balance.LocationCode);
        Assert.Equal("MAIN", balance.WarehouseCode);
        Assert.Equal(40m, balance.Quantity);
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(40m, balance.AvailableQuantity);
    }

    [Fact]
    public async Task List_filters_by_product_location_and_warehouse()
    {
        var byProduct = await _fixture.StockReader.ListAsync(
            new StockQuery { ProductId = _fixture.Flour.Id }, default);
        Assert.Equal(2, byProduct.TotalCount);

        var byLocation = await _fixture.StockReader.ListAsync(
            new StockQuery { LocationId = _fixture.MainLocation.Id }, default);
        Assert.Equal(2, byLocation.TotalCount);

        var byWarehouse = await _fixture.StockReader.ListAsync(
            new StockQuery { WarehouseId = _fixture.ProductionLocation.WarehouseId }, default);
        Assert.Equal(100m, Assert.Single(byWarehouse.Items).Quantity);

        var bySearch = await _fixture.StockReader.ListAsync(new StockQuery { Search = "sug" }, default);
        Assert.Equal("SUGAR-001", Assert.Single(bySearch.Items).Sku);
    }

    [Fact]
    public async Task OnlyInStock_hides_balances_that_have_run_out()
    {
        var emptied = await _fixture.Movements.CreateAsync(
            _fixture.Transfer(_fixture.ProductionLocation, _fixture.MainLocation, (_fixture.Flour, 100m)), default);
        await _fixture.Movements.ConfirmAsync(emptied.Id, default);

        var all = await _fixture.StockReader.ListAsync(new StockQuery(), default);
        Assert.Equal(3, all.TotalCount);

        var inStock = await _fixture.StockReader.ListAsync(new StockQuery { OnlyInStock = true }, default);
        Assert.Equal(2, inStock.TotalCount);
    }

    [Fact]
    public async Task List_sorts_and_paginates()
    {
        var bySku = await _fixture.StockReader.ListAsync(new StockQuery(), default);
        Assert.Equal(["FLOUR-001", "FLOUR-001", "SUGAR-001"], bySku.Items.Select(s => s.Sku));

        var byQuantity = await _fixture.StockReader.ListAsync(new StockQuery { Sort = "-quantity" }, default);
        Assert.Equal([500m, 100m, 40m], byQuantity.Items.Select(s => s.Quantity));

        var firstPage = await _fixture.StockReader.ListAsync(new StockQuery { PageSize = 2 }, default);
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(2, firstPage.Items.Count);
    }
}
