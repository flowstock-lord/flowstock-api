using FlowStock.Application.Inventory;
using FlowStock.Application.Production;
using FlowStock.Application.Traceability;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;
using FlowStock.UnitTests.Inventory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Production;

/// <summary>
/// A production run of batch-tracked goods: the run names the lot of flour it will take, and the
/// cookies it makes get a lot of their own, so the chain from delivery to finished goods is exact
/// rather than inferred (docs/PLAN.md, sections 19 and 20).
/// </summary>
public class BatchProductionTests
{
    private readonly InventoryFixture _fixture = new();
    private readonly BatchService _batches;
    private readonly BillOfMaterialService _boms;
    private readonly ProductionOrderService _orders;
    private readonly TraceabilityService _traceability;

    public BatchProductionTests()
    {
        _fixture.Flour.IsBatchTracked = true;
        _fixture.Cookies.IsBatchTracked = true;
        _fixture.Db.SaveChanges();

        _batches = new BatchService(_fixture.Db, TimeProvider.System, NullLogger<BatchService>.Instance);
        _boms = new BillOfMaterialService(_fixture.Db, NullLogger<BillOfMaterialService>.Instance);
        _orders = new ProductionOrderService(
            _fixture.Db,
            _fixture.Movements,
            _fixture.CurrentUser,
            TimeProvider.System,
            NullLogger<ProductionOrderService>.Instance);
        _traceability = new TraceabilityService(_fixture.Db, TimeProvider.System);
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Cookies from flour and sugar, where only the flour is kept lot by lot.</summary>
    private Task<BillOfMaterialResponse> CookieRecipeAsync() =>
        _boms.CreateAsync(
            new CreateBillOfMaterialRequest(_fixture.Cookies.Id, 100m, "Cookie", null,
            [
                new CreateBillOfMaterialItemRequest(_fixture.Flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(_fixture.Sugar.Id, 4m)
            ]),
            default);

    private async Task<BatchResponse> StockTheLineAsync(string number, decimal flour = 500m, decimal sugar = 200m)
    {
        var batch = await _batches.CreateAsync(
            new CreateBatchRequest(_fixture.Flour.Id, number, "Supplier A", Today.AddDays(-2),
                Today.AddDays(180), null),
            default);

        var receipt = await _fixture.Movements.CreateAsync(
            new CreateStockMovementRequest(
                MovementType.Receipt, null, _fixture.ProductionLocation.Id, "Supplier delivery",
                [
                    new CreateStockMovementLineRequest(_fixture.Flour.Id, flour, batch.Id),
                    new CreateStockMovementLineRequest(_fixture.Sugar.Id, sugar)
                ]),
            default);

        await _fixture.Movements.ConfirmAsync(receipt.Id, default);

        return batch;
    }

    private Task<ProductionOrderResponse> OrderAsync(Guid? flourBatchId, decimal quantity = 1000m) =>
        _orders.CreateAsync(
            new CreateProductionOrderRequest(
                _fixture.Cookies.Id,
                quantity,
                _fixture.ProductionLocation.Id,
                _fixture.MainLocation.Id,
                null,
                null,
                null,
                flourBatchId is { } batchId
                    ? [new ProductionOrderMaterialBatchRequest(_fixture.Flour.Id, batchId)]
                    : null),
            default);

    [Fact]
    public async Task An_order_must_name_the_lot_of_every_tracked_component()
    {
        await CookieRecipeAsync();
        await StockTheLineAsync("FL-0828");

        var missing = await Assert.ThrowsAsync<BatchRequiredException>(() => OrderAsync(null));

        Assert.Equal("BATCH_REQUIRED", missing.Code);
        Assert.Equal(_fixture.Flour.Id, missing.Details!["productId"]);
    }

    [Fact]
    public async Task An_order_cannot_name_a_lot_for_a_component_that_is_not_tracked()
    {
        await CookieRecipeAsync();
        var batch = await StockTheLineAsync("FL-0828");

        var exception = await Assert.ThrowsAsync<BatchNotAllowedException>(
            () => _orders.CreateAsync(
                new CreateProductionOrderRequest(
                    _fixture.Cookies.Id, 1000m, _fixture.ProductionLocation.Id, _fixture.MainLocation.Id,
                    null, null, null,
                    [
                        new ProductionOrderMaterialBatchRequest(_fixture.Flour.Id, batch.Id),
                        new ProductionOrderMaterialBatchRequest(_fixture.Sugar.Id, batch.Id)
                    ]),
                default));

        Assert.Equal("BATCH_NOT_ALLOWED", exception.Code);
        Assert.Equal(_fixture.Sugar.Id, exception.Details!["productId"]);
    }

    /// <summary>
    /// The Phase 8 Definition of Done: a run consumes one named lot and produces another, and the
    /// two are linked by the movements between them.
    /// </summary>
    [Fact]
    public async Task A_run_consumes_a_named_lot_and_produces_a_lot_of_its_own()
    {
        await CookieRecipeAsync();
        var flourBatch = await StockTheLineAsync("FL-0828");
        var otherBatch = await _batches.CreateAsync(
            new CreateBatchRequest(_fixture.Flour.Id, "FL-0901", "Supplier B", null, null, null), default);

        var order = await OrderAsync(flourBatch.Id);

        var flour = order.Materials.Single(m => m.ComponentProductId == _fixture.Flour.Id);
        Assert.Equal(flourBatch.Id, flour.BatchId);
        Assert.Equal("FL-0828", flour.BatchNumber);

        // The sugar is not tracked, so it names no lot at all.
        Assert.Null(order.Materials.Single(m => m.ComponentProductId == _fixture.Sugar.Id).BatchId);

        await _orders.PlanAsync(order.Id, default);
        await _orders.StartAsync(order.Id, default);

        // The reservation and the consumption were of the named lot, not of the other one.
        using (var db = _fixture.NewContext())
        {
            var balances = db.Stocks
                .Where(s => s.ProductId == _fixture.Flour.Id && s.LocationId == _fixture.ProductionLocation.Id)
                .ToList();

            Assert.Equal(400m, balances.Single(s => s.BatchId == flourBatch.Id).Quantity);
            Assert.DoesNotContain(balances, s => s.BatchId == otherBatch.Id && s.Quantity > 0);
        }

        var completed = await _orders.CompleteAsync(
            order.Id, new CompleteProductionOrderRequest(null, null, "CK-2026-001", Today.AddDays(90)), default);

        Assert.Equal("CK-2026-001", completed.OutputBatchNumber);

        var cookieBatch = await _batches.GetAsync(completed.OutputBatchId!.Value, default);
        Assert.Equal(order.Id, cookieBatch.ProductionOrderId);
        Assert.Equal(Today, cookieBatch.ProductionDate);
        Assert.Equal(Today.AddDays(90), cookieBatch.ExpiryDate);

        // The finished goods sit in their own lot in the warehouse.
        using (var db = _fixture.NewContext())
        {
            var stock = db.Stocks.Single(s =>
                s.ProductId == _fixture.Cookies.Id && s.LocationId == _fixture.MainLocation.Id);

            Assert.Equal(cookieBatch.Id, stock.BatchId);
            Assert.Equal(1000m, stock.Quantity);
        }
    }

    [Fact]
    public async Task A_run_of_a_tracked_product_numbers_its_output_after_itself_by_default()
    {
        await CookieRecipeAsync();
        var flourBatch = await StockTheLineAsync("FL-0828");

        var order = await OrderAsync(flourBatch.Id);
        await _orders.PlanAsync(order.Id, default);
        await _orders.StartAsync(order.Id, default);

        var completed = await _orders.CompleteAsync(
            order.Id, new CompleteProductionOrderRequest(null, null), default);

        Assert.Equal(order.Number, completed.OutputBatchNumber);
    }

    /// <summary>"Where was this material used?", asked of one lot and answered exactly.</summary>
    [Fact]
    public async Task A_lot_can_be_traced_from_delivery_to_the_goods_it_became()
    {
        await CookieRecipeAsync();
        var flourBatch = await StockTheLineAsync("FL-0828");

        var order = await OrderAsync(flourBatch.Id);
        await _orders.PlanAsync(order.Id, default);
        await _orders.StartAsync(order.Id, default);
        await _orders.CompleteAsync(order.Id, new CompleteProductionOrderRequest(null, null, "CK-001"), default);

        var trace = await _traceability.BatchTraceAsync(flourBatch.Id, default);

        Assert.Equal("FL-0828", trace.Number);
        Assert.Equal("Supplier A", trace.Supplier);
        Assert.False(trace.IsExpired);

        // What is left of the lot, and where it sits.
        Assert.Equal(400m, trace.QuantityOnHand);
        Assert.Equal("LINE-01", Assert.Single(trace.Locations).LocationCode);

        // Everything that moved it: the delivery in, the run out.
        Assert.Equal([MovementType.Receipt, MovementType.Consumption], trace.History.Select(e => e.MovementType));
        Assert.All(trace.History, entry => Assert.Equal("FL-0828", entry.BatchNumber));

        // And the goods it became — the "Flour batch #123 → Production Order #10042" of section 19.
        var consumer = Assert.Single(trace.ConsumedBy);
        Assert.Equal(order.Id, consumer.ProductionOrderId);
        Assert.Equal(ProductionOrderStatus.Completed, consumer.Status);
        Assert.Equal(100m, consumer.ConsumedQuantity);
        Assert.NotNull(consumer.ConsumedAt);
        Assert.Equal("COOKIE-001", consumer.ProducedSku);
        Assert.Equal(1000m, consumer.ProducedQuantity);
        Assert.Equal("CK-001", consumer.ProducedBatchNumber);
    }

    /// <summary>Backward: given the cookies, which lot of flour went into them.</summary>
    [Fact]
    public async Task A_finished_lot_names_the_lots_it_was_made_of()
    {
        await CookieRecipeAsync();
        var flourBatch = await StockTheLineAsync("FL-0828");

        var order = await OrderAsync(flourBatch.Id);
        await _orders.PlanAsync(order.Id, default);
        await _orders.StartAsync(order.Id, default);
        await _orders.CompleteAsync(order.Id, new CompleteProductionOrderRequest(null, null, "CK-001"), default);

        var trace = await _traceability.ProductionTraceAsync(order.Id, default);

        var flour = trace.Materials.Single(m => m.ComponentProductId == _fixture.Flour.Id);
        Assert.Equal("FL-0828", flour.BatchNumber);

        // With a lot in hand the source is exact: the delivery of that very lot.
        var source = Assert.Single(flour.Sources);
        Assert.Equal(MovementType.Receipt, source.MovementType);
        Assert.Equal(500m, source.Quantity);

        Assert.NotNull(trace.Output);
        Assert.Equal("CK-001", trace.Output.BatchNumber);
    }
}
