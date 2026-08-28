using FlowStock.Application.Catalog;
using FlowStock.Domain.Catalog;
using FlowStock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Catalog;

public class UnitOfMeasureServiceTests
{
    private readonly UnitOfMeasureService _service;

    public UnitOfMeasureServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowStockDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _service = new UnitOfMeasureService(
            new FlowStockDbContext(options),
            NullLogger<UnitOfMeasureService>.Instance);
    }

    [Fact]
    public async Task Create_normalizes_the_code()
    {
        var unit = await _service.CreateAsync(new CreateUnitOfMeasureRequest(" KG ", " Kilogram "), default);

        Assert.Equal("kg", unit.Code);
        Assert.Equal("Kilogram", unit.Name);
        Assert.True(unit.IsActive);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_code_regardless_of_case()
    {
        await _service.CreateAsync(new CreateUnitOfMeasureRequest("kg", "Kilogram"), default);

        var exception = await Assert.ThrowsAsync<UnitOfMeasureCodeAlreadyExistsException>(
            () => _service.CreateAsync(new CreateUnitOfMeasureRequest("KG", "Kilo"), default));

        Assert.Equal("UNIT_OF_MEASURE_CODE_EXISTS", exception.Code);
    }

    [Fact]
    public async Task Update_renames_without_touching_the_code()
    {
        var created = await _service.CreateAsync(new CreateUnitOfMeasureRequest("l", "Liter"), default);

        var updated = await _service.UpdateAsync(created.Id, new UpdateUnitOfMeasureRequest("Litre"), default);

        Assert.Equal("l", updated.Code);
        Assert.Equal("Litre", updated.Name);
    }

    [Fact]
    public async Task Get_reports_an_unknown_unit()
    {
        var exception = await Assert.ThrowsAsync<UnitOfMeasureNotFoundException>(
            () => _service.GetAsync(Guid.NewGuid(), default));

        Assert.Equal("UNIT_OF_MEASURE_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task List_filters_by_search_and_active_flag()
    {
        await _service.CreateAsync(new CreateUnitOfMeasureRequest("kg", "Kilogram"), default);
        var piece = await _service.CreateAsync(new CreateUnitOfMeasureRequest("piece", "Piece"), default);
        await _service.SetActiveAsync(piece.Id, isActive: false, default);

        var bySearch = await _service.ListAsync(new UnitOfMeasureQuery { Search = "kilo" }, default);
        Assert.Equal("kg", Assert.Single(bySearch.Items).Code);

        var active = await _service.ListAsync(new UnitOfMeasureQuery { IsActive = true }, default);
        Assert.Equal("kg", Assert.Single(active.Items).Code);
    }
}
