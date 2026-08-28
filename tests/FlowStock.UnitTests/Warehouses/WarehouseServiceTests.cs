using FlowStock.Application.Warehouses;
using FlowStock.Domain.Warehouses;
using FlowStock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Warehouses;

public class WarehouseServiceTests
{
    private readonly WarehouseService _service;

    public WarehouseServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowStockDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _service = new WarehouseService(
            new FlowStockDbContext(options),
            NullLogger<WarehouseService>.Instance);
    }

    private Task<WarehouseResponse> CreateAsync(string code, WarehouseType type = WarehouseType.General) =>
        _service.CreateAsync(new CreateWarehouseRequest(code, code, null, type), default);

    [Fact]
    public async Task Create_normalizes_the_code()
    {
        var warehouse = await _service.CreateAsync(
            new CreateWarehouseRequest(" main ", " Main Warehouse ", null, WarehouseType.RawMaterials), default);

        Assert.Equal("MAIN", warehouse.Code);
        Assert.Equal("Main Warehouse", warehouse.Name);
        Assert.Equal(WarehouseType.RawMaterials, warehouse.WarehouseType);
        Assert.True(warehouse.IsActive);
        Assert.Equal(0, warehouse.LocationCount);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_code_regardless_of_case()
    {
        await CreateAsync("MAIN");

        var exception = await Assert.ThrowsAsync<WarehouseCodeAlreadyExistsException>(() => CreateAsync("main"));

        Assert.Equal("WAREHOUSE_CODE_EXISTS", exception.Code);
    }

    [Fact]
    public async Task Update_keeps_the_code_immutable()
    {
        var created = await CreateAsync("MAIN");

        var updated = await _service.UpdateAsync(
            created.Id,
            new UpdateWarehouseRequest("Central warehouse", "Building 1", WarehouseType.FinishedGoods),
            default);

        Assert.Equal("MAIN", updated.Code);
        Assert.Equal("Central warehouse", updated.Name);
        Assert.Equal(WarehouseType.FinishedGoods, updated.WarehouseType);
    }

    [Fact]
    public async Task Get_reports_an_unknown_warehouse()
    {
        var exception = await Assert.ThrowsAsync<WarehouseNotFoundException>(
            () => _service.GetAsync(Guid.NewGuid(), default));

        Assert.Equal("WAREHOUSE_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task List_filters_by_search_type_and_active_flag()
    {
        await CreateAsync("MAIN", WarehouseType.RawMaterials);
        await CreateAsync("PROD", WarehouseType.Production);
        var closed = await CreateAsync("OLD", WarehouseType.General);
        await _service.SetActiveAsync(closed.Id, isActive: false, default);

        var bySearch = await _service.ListAsync(new WarehouseQuery { Search = "pro" }, default);
        Assert.Equal("PROD", Assert.Single(bySearch.Items).Code);

        var byType = await _service.ListAsync(
            new WarehouseQuery { WarehouseType = WarehouseType.RawMaterials }, default);
        Assert.Equal("MAIN", Assert.Single(byType.Items).Code);

        var active = await _service.ListAsync(new WarehouseQuery { IsActive = true }, default);
        Assert.Equal(2, active.TotalCount);
    }

    [Fact]
    public async Task List_paginates_and_sorts()
    {
        foreach (var code in new[] { "C", "A", "B" })
        {
            await CreateAsync(code);
        }

        var firstPage = await _service.ListAsync(new WarehouseQuery { PageSize = 2 }, default);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(["A", "B"], firstPage.Items.Select(w => w.Code));

        var descending = await _service.ListAsync(new WarehouseQuery { Sort = "-code" }, default);
        Assert.Equal(["C", "B", "A"], descending.Items.Select(w => w.Code));
    }
}
