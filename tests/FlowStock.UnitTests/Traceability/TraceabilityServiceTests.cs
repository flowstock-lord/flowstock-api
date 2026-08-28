using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using FlowStock.Application.Production;
using FlowStock.Application.Traceability;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;
using FlowStock.Domain.Users;
using FlowStock.UnitTests.Inventory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Traceability;

/// <summary>
/// The traceability questions of docs/PLAN.md, section 39, asked of a finished production run:
/// where the flour came from, what the cookies were made of, where the flour ended up, and who
/// did each of those things.
/// </summary>
public class TraceabilityServiceTests
{
    private readonly InventoryFixture _fixture = new();
    private readonly BillOfMaterialService _boms;
    private readonly ProductionOrderService _orders;
    private readonly TraceabilityService _traceability;

    public TraceabilityServiceTests()
    {
        _fixture.Db.Users.Add(new User
        {
            Id = _fixture.UserId,
            FirstName = "Dana",
            LastName = "Baker",
            Email = "dana.baker@flowstock.local",
            PasswordHash = "not-a-real-hash"
        });
        _fixture.Db.SaveChanges();

        _boms = new BillOfMaterialService(_fixture.Db, NullLogger<BillOfMaterialService>.Instance);
        _orders = new ProductionOrderService(
            _fixture.Db,
            _fixture.Movements,
            _fixture.CurrentUser,
            TimeProvider.System,
            NullLogger<ProductionOrderService>.Instance);
        _traceability = new TraceabilityService(_fixture.Db);
    }

    /// <summary>
    /// Flour and sugar are received into the main warehouse, transferred to the line, and turned
    /// into 1,000 cookies — the whole chain of docs/PLAN.md, section 40.
    /// </summary>
    private async Task<ProductionOrderResponse> RunProductionAsync()
    {
        await _boms.CreateAsync(
            new CreateBillOfMaterialRequest(_fixture.Cookies.Id, 100m, "Cookie", null,
            [
                new CreateBillOfMaterialItemRequest(_fixture.Flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(_fixture.Sugar.Id, 4m)
            ]),
            default);

        await ConfirmAsync(_fixture.Movements.CreateAsync(
            _fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 500m), (_fixture.Sugar, 200m)), default));

        await ConfirmAsync(_fixture.Movements.CreateAsync(
            _fixture.Transfer(_fixture.MainLocation, _fixture.ProductionLocation,
                (_fixture.Flour, 300m), (_fixture.Sugar, 150m)), default));

        var order = await _orders.CreateAsync(
            new CreateProductionOrderRequest(
                _fixture.Cookies.Id, 1000m, _fixture.ProductionLocation.Id, _fixture.MainLocation.Id,
                null, null, null),
            default);

        await _orders.PlanAsync(order.Id, default);
        await _orders.StartAsync(order.Id, default);

        return await _orders.CompleteAsync(order.Id, new CompleteProductionOrderRequest(null, null), default);
    }

    private async Task ConfirmAsync(Task<StockMovementResponse> draft)
        => await _fixture.Movements.ConfirmAsync((await draft).Id, default);

    /// <summary>"Where did this product come from?" and "Who moved it, and when?"</summary>
    [Fact]
    public async Task The_history_of_a_product_names_every_movement_that_touched_it()
    {
        await RunProductionAsync();

        var history = await _traceability.ProductHistoryAsync(
            _fixture.Flour.Id, new ProductHistoryQuery { Sort = "occurredAt" }, default);

        Assert.Equal(
            [MovementType.Receipt, MovementType.Transfer, MovementType.Consumption],
            history.Items.Select(e => e.MovementType));

        var receipt = history.Items.First();
        Assert.Equal(StockFlow.In, receipt.Flow);
        Assert.Equal(500m, receipt.Quantity);
        Assert.Equal("kg", receipt.UnitOfMeasureCode);
        Assert.Equal("A-01", receipt.DestinationLocationCode);
        Assert.Null(receipt.SourceLocationCode);

        // Who moved it: resolved to a person, not left as an id.
        Assert.Equal(_fixture.UserId, receipt.PerformedBy.UserId);
        Assert.Equal("Dana Baker", receipt.PerformedBy.Name);
        Assert.All(history.Items, entry => Assert.NotEqual(default, entry.OccurredAt));

        // And the consumption says which run took it.
        var consumption = history.Items.Last();
        Assert.Equal(StockFlow.Out, consumption.Flow);
        Assert.Equal(100m, consumption.Quantity);
        Assert.NotNull(consumption.ProductionOrderId);
        Assert.StartsWith("PRD-", consumption.ProductionOrderNumber);
    }

    /// <summary>Asked about a location, the history reads in and out relative to that location.</summary>
    [Fact]
    public async Task A_location_history_reads_directions_from_that_location()
    {
        await RunProductionAsync();

        var line = await _traceability.ProductHistoryAsync(
            _fixture.Flour.Id,
            new ProductHistoryQuery { LocationId = _fixture.ProductionLocation.Id, Sort = "occurredAt" },
            default);

        Assert.Equal([StockFlow.In, StockFlow.Out], line.Items.Select(e => e.Flow));
        Assert.Equal([300m, 100m], line.Items.Select(e => e.Quantity));

        var main = await _traceability.ProductHistoryAsync(
            _fixture.Flour.Id,
            new ProductHistoryQuery { LocationId = _fixture.MainLocation.Id, Sort = "occurredAt" },
            default);

        // The same transfer, seen from the other end.
        Assert.Equal([StockFlow.In, StockFlow.Out], main.Items.Select(e => e.Flow));
    }

    [Fact]
    public async Task A_draft_movement_is_not_history()
    {
        await RunProductionAsync();

        await _fixture.Movements.CreateAsync(
            _fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 999m)), default);

        var history = await _traceability.ProductHistoryAsync(_fixture.Flour.Id, new ProductHistoryQuery(), default);

        Assert.DoesNotContain(history.Items, entry => entry.Quantity == 999m);
    }

    /// <summary>"What materials were used to produce this product?"</summary>
    [Fact]
    public async Task A_finished_run_can_show_everything_that_went_into_it()
    {
        var order = await RunProductionAsync();

        var trace = await _traceability.ProductionTraceAsync(order.Id, default);

        Assert.Equal(ProductionOrderStatus.Completed, trace.Status);
        Assert.Equal("COOKIE-001", trace.Sku);
        Assert.Equal(1000m, trace.ProducedQuantity);
        Assert.Equal(1, trace.BillOfMaterialVersion);
        Assert.Equal("Dana Baker", trace.CreatedBy.Name);
        Assert.NotNull(trace.StartedAt);
        Assert.NotNull(trace.CompletedAt);

        Assert.Equal(["FLOUR-001", "SUGAR-001"], trace.Materials.Select(m => m.ComponentSku));

        var flour = trace.Materials.First();
        Assert.Equal(100m, flour.ConsumedQuantity);
        Assert.Equal("kg", flour.UnitOfMeasureCode);
        Assert.NotNull(flour.MovementNumber);
        Assert.NotNull(flour.ConsumedAt);
        Assert.Equal("Dana Baker", flour.ConsumedBy!.Name);

        // Where that flour had come from: the transfer that fed the line, not the run's own document.
        var source = Assert.Single(flour.Sources);
        Assert.Equal(MovementType.Transfer, source.MovementType);
        Assert.Equal(300m, source.Quantity);
        Assert.Equal("A-01", source.SourceLocationCode);
        Assert.Equal("Dana Baker", source.PerformedBy.Name);

        // And where the cookies went.
        Assert.NotNull(trace.Output);
        Assert.Equal(1000m, trace.Output.Quantity);
        Assert.Equal("A-01", trace.Output.LocationCode);
    }

    [Fact]
    public async Task An_unstarted_run_traces_what_it_intends_to_consume()
    {
        await _boms.CreateAsync(
            new CreateBillOfMaterialRequest(_fixture.Cookies.Id, 100m, "Cookie", null,
                [new CreateBillOfMaterialItemRequest(_fixture.Flour.Id, 10m)]),
            default);

        var order = await _orders.CreateAsync(
            new CreateProductionOrderRequest(
                _fixture.Cookies.Id, 1000m, _fixture.ProductionLocation.Id, _fixture.MainLocation.Id,
                null, null, null),
            default);

        var trace = await _traceability.ProductionTraceAsync(order.Id, default);

        Assert.Equal(ProductionOrderStatus.Draft, trace.Status);
        Assert.Null(trace.Output);

        var flour = Assert.Single(trace.Materials);
        Assert.Equal(100m, flour.RequiredQuantity);
        Assert.Equal(0m, flour.ConsumedQuantity);
        Assert.Null(flour.MovementId);
        Assert.Null(flour.ConsumedBy);
    }

    /// <summary>"Where was this material used?"</summary>
    [Fact]
    public async Task A_material_can_name_the_runs_that_consumed_it_and_what_they_produced()
    {
        var order = await RunProductionAsync();

        var usage = await _traceability.MaterialUsageAsync(_fixture.Flour.Id, new MaterialUsageQuery(), default);

        var entry = Assert.Single(usage.Items);
        Assert.Equal(order.Id, entry.ProductionOrderId);
        Assert.Equal(order.Number, entry.Number);
        Assert.Equal(100m, entry.ConsumedQuantity);
        Assert.Equal("kg", entry.UnitOfMeasureCode);
        Assert.NotNull(entry.ConsumedAt);
        Assert.Equal("LINE-01", entry.ProductionLocationCode);

        // The finished goods that flour became.
        Assert.Equal("COOKIE-001", entry.ProducedSku);
        Assert.Equal(1000m, entry.ProducedQuantity);
        Assert.Equal("pcs", entry.ProducedUnitOfMeasureCode);
        Assert.Equal("A-01", entry.OutputLocationCode);
        Assert.Equal("Dana Baker", entry.PerformedBy.Name);
    }

    [Fact]
    public async Task A_material_no_run_ever_used_has_an_empty_usage_list()
    {
        await RunProductionAsync();

        var usage = await _traceability.MaterialUsageAsync(_fixture.Cookies.Id, new MaterialUsageQuery(), default);

        Assert.Empty(usage.Items);
        Assert.Equal(0, usage.TotalCount);
    }

    [Fact]
    public async Task Tracing_something_that_does_not_exist_says_so()
    {
        var product = await Assert.ThrowsAsync<ProductNotFoundException>(
            () => _traceability.ProductHistoryAsync(Guid.NewGuid(), new ProductHistoryQuery(), default));
        Assert.Equal("PRODUCT_NOT_FOUND", product.Code);

        var order = await Assert.ThrowsAsync<ProductionOrderNotFoundException>(
            () => _traceability.ProductionTraceAsync(Guid.NewGuid(), default));
        Assert.Equal("PRODUCTION_ORDER_NOT_FOUND", order.Code);
    }
}
