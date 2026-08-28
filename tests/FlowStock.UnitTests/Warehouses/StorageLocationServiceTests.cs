using FlowStock.Application.Warehouses;
using FlowStock.Domain.Warehouses;
using FlowStock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Warehouses;

public class StorageLocationServiceTests
{
    private readonly FlowStockDbContext _db;
    private readonly StorageLocationService _service;
    private readonly Warehouse _main;
    private readonly Warehouse _production;

    public StorageLocationServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowStockDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new FlowStockDbContext(options);

        _main = new Warehouse { Code = "MAIN", Name = "Main Warehouse", WarehouseType = WarehouseType.RawMaterials };
        _production = new Warehouse { Code = "PROD", Name = "Production", WarehouseType = WarehouseType.Production };
        _db.Warehouses.AddRange(_main, _production);
        _db.SaveChanges();

        _service = new StorageLocationService(_db, NullLogger<StorageLocationService>.Instance);
    }

    private Task<StorageLocationResponse> CreateAsync(Warehouse warehouse, string code) =>
        _service.CreateAsync(new CreateStorageLocationRequest(warehouse.Id, code, code, null), default);

    [Fact]
    public async Task Create_normalizes_the_code_and_reports_its_warehouse()
    {
        var location = await _service.CreateAsync(
            new CreateStorageLocationRequest(_main.Id, " a-01 ", " Rack A-01 ", null), default);

        Assert.Equal("A-01", location.Code);
        Assert.Equal("Rack A-01", location.Name);
        Assert.Equal(_main.Id, location.WarehouseId);
        Assert.Equal("MAIN", location.WarehouseCode);
        Assert.True(location.IsActive);
    }

    [Fact]
    public async Task The_same_code_may_exist_in_different_warehouses()
    {
        await CreateAsync(_main, "A-01");
        var second = await CreateAsync(_production, "A-01");

        Assert.Equal("PROD", second.WarehouseCode);
    }

    [Fact]
    public async Task A_code_cannot_repeat_inside_one_warehouse()
    {
        await CreateAsync(_main, "A-01");

        var exception = await Assert.ThrowsAsync<LocationCodeAlreadyExistsException>(
            () => CreateAsync(_main, "a-01"));

        Assert.Equal("LOCATION_CODE_EXISTS", exception.Code);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_warehouse()
    {
        var exception = await Assert.ThrowsAsync<WarehouseNotFoundException>(
            () => _service.CreateAsync(
                new CreateStorageLocationRequest(Guid.NewGuid(), "A-01", "A-01", null), default));

        Assert.Equal("WAREHOUSE_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task Create_rejects_a_deactivated_warehouse()
    {
        _main.IsActive = false;
        await _db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<WarehouseInactiveException>(() => CreateAsync(_main, "A-01"));

        Assert.Equal("WAREHOUSE_INACTIVE", exception.Code);
    }

    [Fact]
    public async Task Update_keeps_the_code_and_the_warehouse_immutable()
    {
        var created = await CreateAsync(_main, "A-01");

        var updated = await _service.UpdateAsync(
            created.Id, new UpdateStorageLocationRequest("Rack A-01, shelf 2", "Near the door"), default);

        Assert.Equal("A-01", updated.Code);
        Assert.Equal(_main.Id, updated.WarehouseId);
        Assert.Equal("Rack A-01, shelf 2", updated.Name);
    }

    [Fact]
    public async Task Get_reports_an_unknown_location()
    {
        var exception = await Assert.ThrowsAsync<LocationNotFoundException>(
            () => _service.GetAsync(Guid.NewGuid(), default));

        Assert.Equal("LOCATION_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task List_filters_by_warehouse_and_active_flag()
    {
        await CreateAsync(_main, "A-01");
        await CreateAsync(_main, "A-02");
        await CreateAsync(_production, "LINE-01");
        var retired = await CreateAsync(_production, "LINE-02");
        await _service.SetActiveAsync(retired.Id, isActive: false, default);

        var mainLocations = await _service.ListAsync(
            new StorageLocationQuery { WarehouseId = _main.Id }, default);
        Assert.Equal(["A-01", "A-02"], mainLocations.Items.Select(l => l.Code));

        var activeProduction = await _service.ListAsync(
            new StorageLocationQuery { WarehouseId = _production.Id, IsActive = true }, default);
        Assert.Equal("LINE-01", Assert.Single(activeProduction.Items).Code);

        var all = await _service.ListAsync(new StorageLocationQuery(), default);
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(["A-01", "A-02", "LINE-01", "LINE-02"], all.Items.Select(l => l.Code));
    }
}
