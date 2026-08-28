using FlowStock.Application.Production;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Production;
using FlowStock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Production;

public class BillOfMaterialServiceTests
{
    private readonly FlowStockDbContext _db;
    private readonly BillOfMaterialService _service;
    private readonly Product _cookie;
    private readonly Product _flour;
    private readonly Product _sugar;
    private readonly Product _butter;

    public BillOfMaterialServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowStockDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // The in-memory provider has no transactions; the service opens one because against
            // PostgreSQL it must.
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new FlowStockDbContext(options);

        var kilogram = new UnitOfMeasure { Code = "kg", Name = "Kilogram" };
        var piece = new UnitOfMeasure { Code = "pcs", Name = "Piece" };

        _cookie = new Product
        {
            Sku = "COOKIE-001", Name = "Cookie", ProductType = ProductType.FinishedProduct, UnitOfMeasure = piece
        };
        _flour = Material("FLOUR-001", "Flour", kilogram);
        _sugar = Material("SUGAR-001", "Sugar", kilogram);
        _butter = Material("BUTTER-001", "Butter", kilogram);

        _db.UnitsOfMeasure.AddRange(kilogram, piece);
        _db.Products.AddRange(_cookie, _flour, _sugar, _butter);
        _db.SaveChanges();

        _service = new BillOfMaterialService(_db, NullLogger<BillOfMaterialService>.Instance);
    }

    private static Product Material(string sku, string name, UnitOfMeasure unit) => new()
    {
        Sku = sku, Name = name, ProductType = ProductType.RawMaterial, UnitOfMeasure = unit
    };

    /// <summary>The recipe from docs/PLAN.md, section 14: 100 cookies from flour, sugar and butter.</summary>
    private Task<BillOfMaterialResponse> CookieRecipeAsync(decimal outputQuantity = 100m) =>
        _service.CreateAsync(
            new CreateBillOfMaterialRequest(_cookie.Id, outputQuantity, "Cookie", null,
            [
                new CreateBillOfMaterialItemRequest(_flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(_sugar.Id, 4m),
                new CreateBillOfMaterialItemRequest(_butter.Id, 2m)
            ]),
            default);

    [Fact]
    public async Task A_recipe_records_its_components_with_their_own_units()
    {
        var bom = await CookieRecipeAsync();

        Assert.Equal(1, bom.Version);
        Assert.True(bom.IsActive);
        Assert.Equal(100m, bom.OutputQuantity);
        Assert.Equal("pcs", bom.OutputUnitOfMeasureCode);
        Assert.Equal(["BUTTER-001", "FLOUR-001", "SUGAR-001"], bom.Items.Select(i => i.ComponentSku));
        Assert.All(bom.Items, item => Assert.Equal("kg", item.UnitOfMeasureCode));
        Assert.Equal(10m, bom.Items.Single(i => i.ComponentSku == "FLOUR-001").Quantity);
    }

    /// <summary>
    /// The Phase 5 Definition of Done: given Cookie × 100 needs flour 10, sugar 4 and butter 2,
    /// the API can calculate the materials a run needs.
    /// </summary>
    [Fact]
    public async Task Requirements_for_one_full_run_are_the_recipe_itself()
    {
        var bom = await CookieRecipeAsync();

        var requirements = await _service.CalculateRequirementsAsync(bom.Id, 100m, default);

        Assert.Equal(100m, requirements.Quantity);
        Assert.Equal(100m, requirements.OutputQuantityPerRun);
        Assert.Equal(
            [("BUTTER-001", 2m), ("FLOUR-001", 10m), ("SUGAR-001", 4m)],
            requirements.Requirements.Select(r => (r.ComponentSku, r.RequiredQuantity)));
        Assert.All(requirements.Requirements, r => Assert.Equal("kg", r.UnitOfMeasureCode));
    }

    [Theory]
    [InlineData(1000, 100, 40, 20)]
    [InlineData(50, 5, 2, 1)]
    [InlineData(250, 25, 10, 5)]
    public async Task Requirements_scale_with_the_quantity_produced(
        int quantity,
        int flour,
        int sugar,
        int butter)
    {
        var bom = await CookieRecipeAsync();

        var requirements = await _service.CalculateRequirementsAsync(bom.Id, quantity, default);

        decimal Required(string sku) => requirements.Requirements.Single(r => r.ComponentSku == sku).RequiredQuantity;

        Assert.Equal(flour, Required("FLOUR-001"));
        Assert.Equal(sugar, Required("SUGAR-001"));
        Assert.Equal(butter, Required("BUTTER-001"));
    }

    [Fact]
    public async Task Requirements_that_do_not_divide_evenly_are_rounded_to_four_decimals()
    {
        var bom = await CookieRecipeAsync(outputQuantity: 3m);

        var requirements = await _service.CalculateRequirementsAsync(bom.Id, 1m, default);

        // 10 / 3 = 3.333333..., stored and reported at the scale quantities use.
        Assert.Equal(3.3333m, requirements.Requirements.Single(r => r.ComponentSku == "FLOUR-001").RequiredQuantity);
    }

    [Fact]
    public async Task Requirements_keep_the_per_run_quantity_visible_next_to_the_scaled_one()
    {
        var bom = await CookieRecipeAsync();

        var requirements = await _service.CalculateRequirementsAsync(bom.Id, 250m, default);
        var flour = requirements.Requirements.Single(r => r.ComponentSku == "FLOUR-001");

        Assert.Equal(10m, flour.QuantityPerRun);
        Assert.Equal(25m, flour.RequiredQuantity);
    }

    [Fact]
    public async Task Publishing_a_new_recipe_versions_it_and_stands_the_previous_one_down()
    {
        var first = await CookieRecipeAsync();

        var second = await _service.CreateAsync(
            new CreateBillOfMaterialRequest(_cookie.Id, 100m, "Cookie, less sugar", null,
            [
                new CreateBillOfMaterialItemRequest(_flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(_sugar.Id, 3m)
            ]),
            default);

        Assert.Equal(2, second.Version);
        Assert.True(second.IsActive);

        var reloaded = await _service.GetAsync(first.Id, default);
        Assert.False(reloaded.IsActive);

        // The superseded version keeps its own components: it is what older orders were built from.
        Assert.Equal(3, reloaded.Items.Count);
        Assert.Equal(4m, reloaded.Items.Single(i => i.ComponentSku == "SUGAR-001").Quantity);
    }

    [Fact]
    public async Task Activating_an_older_version_puts_it_back_in_force()
    {
        var first = await CookieRecipeAsync();
        var second = await _service.CreateAsync(
            new CreateBillOfMaterialRequest(_cookie.Id, 100m, null, null,
                [new CreateBillOfMaterialItemRequest(_flour.Id, 11m)]),
            default);

        var restored = await _service.SetActiveAsync(first.Id, isActive: true, default);

        Assert.True(restored.IsActive);
        Assert.False((await _service.GetAsync(second.Id, default)).IsActive);

        var active = await _service.ListAsync(
            new BillOfMaterialQuery { ProductId = _cookie.Id, IsActive = true }, default);
        Assert.Equal(1, Assert.Single(active.Items).Version);
    }

    [Fact]
    public async Task An_older_version_can_still_answer_what_it_would_have_required()
    {
        var first = await CookieRecipeAsync();

        await _service.CreateAsync(
            new CreateBillOfMaterialRequest(_cookie.Id, 100m, null, null,
                [new CreateBillOfMaterialItemRequest(_flour.Id, 99m)]),
            default);

        var requirements = await _service.CalculateRequirementsAsync(first.Id, 100m, default);

        Assert.Equal(1, requirements.Version);
        Assert.Equal(10m, requirements.Requirements.Single(r => r.ComponentSku == "FLOUR-001").RequiredQuantity);
    }

    [Fact]
    public async Task Update_changes_only_the_labelling()
    {
        var bom = await CookieRecipeAsync();

        var updated = await _service.UpdateAsync(
            bom.Id, new UpdateBillOfMaterialRequest("Cookie, house recipe", "Approved by the chef"), default);

        Assert.Equal("Cookie, house recipe", updated.Name);
        Assert.Equal(100m, updated.OutputQuantity);
        Assert.Equal(1, updated.Version);
        Assert.Equal(3, updated.Items.Count);
        Assert.Equal(10m, updated.Items.Single(i => i.ComponentSku == "FLOUR-001").Quantity);
    }

    [Fact]
    public async Task A_component_cannot_appear_twice()
    {
        var exception = await Assert.ThrowsAsync<BomInvalidException>(() => _service.CreateAsync(
            new CreateBillOfMaterialRequest(_cookie.Id, 100m, null, null,
            [
                new CreateBillOfMaterialItemRequest(_flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(_flour.Id, 2m)
            ]),
            default));

        Assert.Equal("BOM_INVALID", exception.Code);
        Assert.Equal(_flour.Id, exception.Details!["componentProductId"]);
    }

    [Fact]
    public async Task A_product_cannot_be_a_component_of_itself()
    {
        var exception = await Assert.ThrowsAsync<BomInvalidException>(() => _service.CreateAsync(
            new CreateBillOfMaterialRequest(_cookie.Id, 100m, null, null,
            [
                new CreateBillOfMaterialItemRequest(_flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(_cookie.Id, 1m)
            ]),
            default));

        Assert.Equal("BOM_INVALID", exception.Code);
        Assert.Equal("COOKIE-001", exception.Details!["sku"]);
    }

    [Fact]
    public async Task An_unknown_product_or_component_is_rejected()
    {
        await Assert.ThrowsAsync<ProductNotFoundException>(() => _service.CreateAsync(
            new CreateBillOfMaterialRequest(Guid.NewGuid(), 100m, null, null,
                [new CreateBillOfMaterialItemRequest(_flour.Id, 1m)]),
            default));

        await Assert.ThrowsAsync<ProductNotFoundException>(() => _service.CreateAsync(
            new CreateBillOfMaterialRequest(_cookie.Id, 100m, null, null,
                [new CreateBillOfMaterialItemRequest(Guid.NewGuid(), 1m)]),
            default));
    }

    [Fact]
    public async Task Get_reports_an_unknown_recipe()
    {
        var exception = await Assert.ThrowsAsync<BomNotFoundException>(
            () => _service.GetAsync(Guid.NewGuid(), default));

        Assert.Equal("BOM_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task List_filters_by_product_and_active_flag_and_sorts_newest_version_first()
    {
        await CookieRecipeAsync();
        await _service.CreateAsync(
            new CreateBillOfMaterialRequest(_cookie.Id, 100m, null, null,
                [new CreateBillOfMaterialItemRequest(_flour.Id, 12m)]),
            default);
        await _service.CreateAsync(
            new CreateBillOfMaterialRequest(_butter.Id, 1m, "Butter blend", null,
                [new CreateBillOfMaterialItemRequest(_flour.Id, 1m)]),
            default);

        var forCookie = await _service.ListAsync(new BillOfMaterialQuery { ProductId = _cookie.Id }, default);
        Assert.Equal([2, 1], forCookie.Items.Select(b => b.Version));

        var active = await _service.ListAsync(new BillOfMaterialQuery { IsActive = true }, default);
        Assert.Equal(2, active.TotalCount);

        var bySearch = await _service.ListAsync(new BillOfMaterialQuery { Search = "cook" }, default);
        Assert.Equal(2, bySearch.TotalCount);

        var all = await _service.ListAsync(new BillOfMaterialQuery(), default);
        Assert.Equal(3, all.TotalCount);
    }
}
