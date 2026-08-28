using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using FlowStock.Application.Production;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;
using FlowStock.Domain.Warehouses;
using FlowStock.UnitTests.Inventory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Production;

/// <summary>
/// The production workflow over the inventory of <see cref="InventoryFixture"/>: materials live on
/// the production line, finished cookies are booked into the main warehouse.
/// </summary>
public class ProductionOrderServiceTests
{
    private readonly InventoryFixture _fixture = new();
    private readonly BillOfMaterialService _boms;
    private readonly ProductionOrderService _orders;

    public ProductionOrderServiceTests()
    {
        _boms = new BillOfMaterialService(_fixture.Db, NullLogger<BillOfMaterialService>.Instance);
        _orders = new ProductionOrderService(
            _fixture.Db,
            _fixture.Movements,
            _fixture.Notifications,
            _fixture.CurrentUser,
            TimeProvider.System,
            NullLogger<ProductionOrderService>.Instance);
    }

    /// <summary>The recipe of docs/PLAN.md, section 14: 100 cookies from 10 kg flour and 4 kg sugar.</summary>
    private Task<BillOfMaterialResponse> CookieRecipeAsync(decimal flour = 10m, decimal sugar = 4m) =>
        _boms.CreateAsync(
            new CreateBillOfMaterialRequest(_fixture.Cookies.Id, 100m, "Cookie", null,
            [
                new CreateBillOfMaterialItemRequest(_fixture.Flour.Id, flour),
                new CreateBillOfMaterialItemRequest(_fixture.Sugar.Id, sugar)
            ]),
            default);

    /// <summary>Puts materials on the production line, the way a transfer from the warehouse would.</summary>
    private async Task StockTheLineAsync(decimal flour = 500m, decimal sugar = 200m)
    {
        var receipt = await _fixture.Movements.CreateAsync(
            _fixture.Receipt(_fixture.ProductionLocation, (_fixture.Flour, flour), (_fixture.Sugar, sugar)),
            default);

        await _fixture.Movements.ConfirmAsync(receipt.Id, default);
    }

    private Task<ProductionOrderResponse> OrderAsync(decimal quantity = 1000m, Guid? bomId = null) =>
        _orders.CreateAsync(
            new CreateProductionOrderRequest(
                _fixture.Cookies.Id,
                quantity,
                _fixture.ProductionLocation.Id,
                _fixture.MainLocation.Id,
                bomId,
                PlannedStartAt: null,
                Notes: null),
            default);

    /// <summary>Reads a reservation from a fresh context, so it reflects saved state only.</summary>
    private decimal ReservedOf(Product product, StorageLocation location)
    {
        using var db = _fixture.NewContext();

        return db.Stocks
            .Where(s => s.ProductId == product.Id && s.LocationId == location.Id)
            .Select(s => s.ReservedQuantity)
            .SingleOrDefault();
    }

    private static decimal Required(ProductionOrderResponse order, Product component) =>
        order.Materials.Single(m => m.ComponentProductId == component.Id).RequiredQuantity;

    [Fact]
    public async Task An_order_scales_its_materials_from_the_recipe_and_reserves_nothing_yet()
    {
        await CookieRecipeAsync();
        await StockTheLineAsync();

        var order = await OrderAsync(1000m);

        Assert.Equal(ProductionOrderStatus.Draft, order.Status);
        Assert.StartsWith("PRD-", order.Number);
        Assert.Equal(100m, Required(order, _fixture.Flour));
        Assert.Equal(40m, Required(order, _fixture.Sugar));
        Assert.All(order.Materials, material => Assert.Equal(0m, material.ConsumedQuantity));
        Assert.Equal(0m, ReservedOf(_fixture.Flour, _fixture.ProductionLocation));
    }

    /// <summary>
    /// The Phase 6 Definition of Done: materials on the line, a run that consumes them, finished
    /// goods in the warehouse, and every stock change traceable to the order that caused it.
    /// </summary>
    [Fact]
    public async Task A_run_consumes_its_materials_and_produces_finished_goods()
    {
        await CookieRecipeAsync();
        await StockTheLineAsync(flour: 500m, sugar: 200m);

        var order = await OrderAsync(1000m);

        var planned = await _orders.PlanAsync(order.Id, default);
        Assert.Equal(ProductionOrderStatus.Planned, planned.Status);
        Assert.Equal(100m, ReservedOf(_fixture.Flour, _fixture.ProductionLocation));
        // Reserving takes nothing out of stock; it only spoken-for what is there.
        Assert.Equal(500m, _fixture.QuantityOf(_fixture.Flour, _fixture.ProductionLocation));

        var started = await _orders.StartAsync(order.Id, default);
        Assert.Equal(ProductionOrderStatus.InProgress, started.Status);
        Assert.NotNull(started.ActualStartAt);
        Assert.Equal(100m, Assert.Single(started.Materials, m => m.ComponentProductId == _fixture.Flour.Id)
            .ConsumedQuantity);
        Assert.Equal(400m, _fixture.QuantityOf(_fixture.Flour, _fixture.ProductionLocation));
        Assert.Equal(160m, _fixture.QuantityOf(_fixture.Sugar, _fixture.ProductionLocation));
        // The reservation is gone: it became a consumption, it was not left standing.
        Assert.Equal(0m, ReservedOf(_fixture.Flour, _fixture.ProductionLocation));

        var completed = await _orders.CompleteAsync(order.Id, new CompleteProductionOrderRequest(null, null), default);
        Assert.Equal(ProductionOrderStatus.Completed, completed.Status);
        Assert.Equal(1000m, completed.ProducedQuantity);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(1000m, _fixture.QuantityOf(_fixture.Cookies, _fixture.MainLocation));

        // Both stock changes are confirmed movements that name the order (docs/PLAN.md, section 19).
        var movements = await _fixture.Movements.ListAsync(
            new StockMovementQuery { ProductionOrderId = order.Id }, default);

        Assert.All(movements.Items, movement =>
        {
            Assert.Equal(MovementStatus.Confirmed, movement.Status);
            Assert.Equal(order.Id, movement.ProductionOrderId);
        });
        Assert.Equal(
            [MovementType.Consumption, MovementType.ProductionOutput],
            movements.Items.Select(m => m.MovementType).OrderBy(t => t.ToString()));
    }

    [Fact]
    public async Task A_run_may_yield_less_than_it_planned()
    {
        await CookieRecipeAsync();
        await StockTheLineAsync();

        var order = await OrderAsync(1000m);
        await _orders.PlanAsync(order.Id, default);
        await _orders.StartAsync(order.Id, default);

        var completed = await _orders.CompleteAsync(
            order.Id, new CompleteProductionOrderRequest(940m, "60 broke in the oven"), default);

        Assert.Equal(1000m, completed.PlannedQuantity);
        Assert.Equal(940m, completed.ProducedQuantity);
        // The materials the run actually took do not shrink with the yield.
        Assert.Equal(100m, Required(completed, _fixture.Flour));
        Assert.Equal(940m, _fixture.QuantityOf(_fixture.Cookies, _fixture.MainLocation));
    }

    [Fact]
    public async Task Planning_a_run_the_line_cannot_feed_is_rejected_and_reserves_nothing()
    {
        await CookieRecipeAsync();
        await StockTheLineAsync(flour: 50m, sugar: 200m);

        var order = await OrderAsync(1000m);

        var exception = await Assert.ThrowsAsync<InsufficientStockException>(
            () => _orders.PlanAsync(order.Id, default));

        Assert.Equal("INSUFFICIENT_STOCK", exception.Code);
        Assert.Equal(100m, exception.Details!["requested"]);
        Assert.Equal(50m, exception.Details["available"]);
        Assert.Equal(ProductionOrderStatus.Draft, (await _orders.GetAsync(order.Id, default)).Status);
    }

    /// <summary>
    /// A reservation is what makes planning worth anything: the material is spoken for, so a
    /// warehouse transfer can no longer take it out from under the run (CLAUDE.md, rule 6).
    /// </summary>
    [Fact]
    public async Task Reserved_material_cannot_be_transferred_away()
    {
        await CookieRecipeAsync();
        await StockTheLineAsync(flour: 120m, sugar: 200m);

        var order = await OrderAsync(1000m);
        await _orders.PlanAsync(order.Id, default);

        var transfer = await _fixture.Movements.CreateAsync(
            _fixture.Transfer(_fixture.ProductionLocation, _fixture.MainLocation, (_fixture.Flour, 30m)), default);

        var exception = await Assert.ThrowsAsync<InsufficientStockException>(
            () => _fixture.Movements.ConfirmAsync(transfer.Id, default));

        Assert.Equal(20m, exception.Details!["available"]);
        Assert.Equal(120m, _fixture.QuantityOf(_fixture.Flour, _fixture.ProductionLocation));
    }

    [Fact]
    public async Task Cancelling_a_planned_run_releases_its_reservations()
    {
        await CookieRecipeAsync();
        await StockTheLineAsync();

        var order = await OrderAsync(1000m);
        await _orders.PlanAsync(order.Id, default);

        var cancelled = await _orders.CancelAsync(
            order.Id, new CancelProductionOrderRequest("Line broke down"), default);

        Assert.Equal(ProductionOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(_fixture.UserId, cancelled.CancelledBy);
        Assert.Equal(0m, ReservedOf(_fixture.Flour, _fixture.ProductionLocation));
        Assert.Equal(500m, _fixture.QuantityOf(_fixture.Flour, _fixture.ProductionLocation));
    }

    /// <summary>
    /// A started run has confirmed movements behind it, and those are history: it is corrected
    /// with compensating movements, not by cancelling the order (CLAUDE.md, rule 2).
    /// </summary>
    [Fact]
    public async Task A_started_run_cannot_be_cancelled()
    {
        await CookieRecipeAsync();
        await StockTheLineAsync();

        var order = await OrderAsync(1000m);
        await _orders.PlanAsync(order.Id, default);
        await _orders.StartAsync(order.Id, default);

        var exception = await Assert.ThrowsAsync<ProductionOrderInvalidException>(
            () => _orders.CancelAsync(order.Id, new CancelProductionOrderRequest(null), default));

        Assert.Equal("PRODUCTION_ORDER_INVALID", exception.Code);
        Assert.Equal(400m, _fixture.QuantityOf(_fixture.Flour, _fixture.ProductionLocation));
    }

    [Fact]
    public async Task The_workflow_runs_in_one_direction_only()
    {
        await CookieRecipeAsync();
        await StockTheLineAsync();

        var order = await OrderAsync(1000m);

        // Draft → InProgress skips the reservation.
        var tooEarly = await Assert.ThrowsAsync<ProductionOrderInvalidException>(
            () => _orders.StartAsync(order.Id, default));
        Assert.Equal("PRODUCTION_ORDER_INVALID", tooEarly.Code);

        await _orders.PlanAsync(order.Id, default);

        var notStarted = await Assert.ThrowsAsync<ProductionOrderInvalidException>(
            () => _orders.CompleteAsync(order.Id, new CompleteProductionOrderRequest(null, null), default));
        Assert.Equal("PRODUCTION_ORDER_INVALID", notStarted.Code);

        await _orders.StartAsync(order.Id, default);
        await _orders.CompleteAsync(order.Id, new CompleteProductionOrderRequest(null, null), default);

        var finished = await Assert.ThrowsAsync<ProductionOrderAlreadyCompletedException>(
            () => _orders.CompleteAsync(order.Id, new CompleteProductionOrderRequest(null, null), default));
        Assert.Equal("PRODUCTION_ORDER_ALREADY_COMPLETED", finished.Code);

        // And a completed run is not cancelled either — its output is stock somebody now holds.
        await Assert.ThrowsAsync<ProductionOrderAlreadyCompletedException>(
            () => _orders.CancelAsync(order.Id, new CancelProductionOrderRequest(null), default));

        Assert.Equal(1000m, _fixture.QuantityOf(_fixture.Cookies, _fixture.MainLocation));
    }

    /// <summary>
    /// The order records the recipe version it was built from, so publishing a new version cannot
    /// change what an open run undertook to consume (docs/PLAN.md, section 14).
    /// </summary>
    [Fact]
    public async Task A_new_recipe_version_does_not_change_an_open_order()
    {
        var first = await CookieRecipeAsync();
        await StockTheLineAsync();

        var order = await OrderAsync(1000m);

        var second = await CookieRecipeAsync(flour: 12m);
        Assert.Equal(2, second.Version);

        var unchanged = await _orders.GetAsync(order.Id, default);

        Assert.Equal(first.Id, unchanged.BillOfMaterialId);
        Assert.Equal(1, unchanged.BillOfMaterialVersion);
        Assert.Equal(100m, Required(unchanged, _fixture.Flour));
    }

    [Fact]
    public async Task A_product_with_no_active_recipe_cannot_be_produced()
    {
        await StockTheLineAsync();

        var exception = await Assert.ThrowsAsync<ProductionOrderInvalidException>(() => OrderAsync(1000m));

        Assert.Equal("PRODUCTION_ORDER_INVALID", exception.Code);
        Assert.Equal(_fixture.Cookies.Id, exception.Details!["productId"]);
    }

    /// <summary>An older version can be repeated deliberately, by naming it on the order.</summary>
    [Fact]
    public async Task An_order_may_name_the_recipe_version_it_repeats()
    {
        var first = await CookieRecipeAsync();
        await CookieRecipeAsync(flour: 12m);
        await StockTheLineAsync();

        var order = await OrderAsync(1000m, first.Id);

        Assert.Equal(1, order.BillOfMaterialVersion);
        Assert.Equal(100m, Required(order, _fixture.Flour));
    }

    [Fact]
    public async Task A_run_too_small_to_need_a_component_is_rejected()
    {
        // 100 cookies need 10 kg of flour, so one cookie needs 0.1 kg — but 0.0001 of a cookie
        // rounds every component away, and an order that consumes nothing is not a run.
        await CookieRecipeAsync();
        await StockTheLineAsync();

        var exception = await Assert.ThrowsAsync<ProductionOrderInvalidException>(() => OrderAsync(0.0001m));

        Assert.Equal("PRODUCTION_ORDER_INVALID", exception.Code);
    }
}
