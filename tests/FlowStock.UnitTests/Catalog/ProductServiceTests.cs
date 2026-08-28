using FlowStock.Application.Catalog;
using FlowStock.Domain.Catalog;
using FlowStock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Catalog;

public class ProductServiceTests
{
    private readonly FlowStockDbContext _db;
    private readonly ProductService _service;
    private readonly UnitOfMeasure _kilogram;

    public ProductServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowStockDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new FlowStockDbContext(options);

        _kilogram = new UnitOfMeasure { Code = "kg", Name = "Kilogram" };
        _db.UnitsOfMeasure.Add(_kilogram);
        _db.SaveChanges();

        _service = new ProductService(_db, NullLogger<ProductService>.Instance);
    }

    private CreateProductRequest Flour(string sku = "flour-001") =>
        new(sku, " Flour ", null, ProductType.RawMaterial, _kilogram.Id);

    [Fact]
    public async Task Create_normalizes_the_sku_and_trims_the_name()
    {
        var product = await _service.CreateAsync(Flour(" flour-001 "), default);

        Assert.Equal("FLOUR-001", product.Sku);
        Assert.Equal("Flour", product.Name);
        Assert.Equal("kg", product.UnitOfMeasureCode);
        Assert.True(product.IsActive);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_sku_regardless_of_case()
    {
        await _service.CreateAsync(Flour(), default);

        var exception = await Assert.ThrowsAsync<SkuAlreadyExistsException>(
            () => _service.CreateAsync(Flour("FLOUR-001"), default));

        Assert.Equal("SKU_ALREADY_EXISTS", exception.Code);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_unit_of_measure()
    {
        var request = Flour() with { UnitOfMeasureId = Guid.NewGuid() };

        await Assert.ThrowsAsync<UnitOfMeasureNotFoundException>(
            () => _service.CreateAsync(request, default));
    }

    [Fact]
    public async Task Create_rejects_a_deactivated_unit_of_measure()
    {
        _kilogram.IsActive = false;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnitOfMeasureInactiveException>(
            () => _service.CreateAsync(Flour(), default));
    }

    [Fact]
    public async Task Update_keeps_the_sku_immutable()
    {
        var created = await _service.CreateAsync(Flour(), default);

        var updated = await _service.UpdateAsync(
            created.Id,
            new UpdateProductRequest("Wheat flour", "Type 550", ProductType.RawMaterial, _kilogram.Id),
            default);

        Assert.Equal("FLOUR-001", updated.Sku);
        Assert.Equal("Wheat flour", updated.Name);
        Assert.Equal("Type 550", updated.Description);
    }

    [Fact]
    public async Task Get_reports_an_unknown_product()
    {
        var exception = await Assert.ThrowsAsync<ProductNotFoundException>(
            () => _service.GetAsync(Guid.NewGuid(), default));

        Assert.Equal("PRODUCT_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task Deactivating_a_product_keeps_it_readable()
    {
        var created = await _service.CreateAsync(Flour(), default);

        var deactivated = await _service.SetActiveAsync(created.Id, isActive: false, default);

        Assert.False(deactivated.IsActive);
        Assert.False((await _service.GetAsync(created.Id, default)).IsActive);
    }

    [Fact]
    public async Task List_filters_by_search_type_and_active_flag()
    {
        await _service.CreateAsync(Flour(), default);
        await _service.CreateAsync(new CreateProductRequest(
            "COOKIE-001", "Cookie", null, ProductType.FinishedProduct, _kilogram.Id), default);
        var sugar = await _service.CreateAsync(new CreateProductRequest(
            "SUGAR-001", "Sugar", null, ProductType.RawMaterial, _kilogram.Id), default);
        await _service.SetActiveAsync(sugar.Id, isActive: false, default);

        var byName = await _service.ListAsync(new ProductQuery { Search = "coo" }, default);
        Assert.Equal("COOKIE-001", Assert.Single(byName.Items).Sku);

        var bySku = await _service.ListAsync(new ProductQuery { Search = "flour-0" }, default);
        Assert.Equal("FLOUR-001", Assert.Single(bySku.Items).Sku);

        var rawMaterials = await _service.ListAsync(
            new ProductQuery { ProductType = ProductType.RawMaterial, IsActive = true }, default);
        Assert.Equal("FLOUR-001", Assert.Single(rawMaterials.Items).Sku);
    }

    [Fact]
    public async Task List_paginates_and_sorts()
    {
        foreach (var sku in new[] { "C-003", "A-001", "B-002" })
        {
            await _service.CreateAsync(new CreateProductRequest(
                sku, sku, null, ProductType.Other, _kilogram.Id), default);
        }

        var firstPage = await _service.ListAsync(new ProductQuery { PageSize = 2 }, default);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(["A-001", "B-002"], firstPage.Items.Select(p => p.Sku));

        var descending = await _service.ListAsync(new ProductQuery { Sort = "-sku" }, default);
        Assert.Equal(["C-003", "B-002", "A-001"], descending.Items.Select(p => p.Sku));
    }
}
