using FlowStock.Application.Inventory;
using FlowStock.Application.Production;
using FlowStock.Application.Reporting;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;
using FlowStock.Domain.Warehouses;
using FlowStock.UnitTests.Inventory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Reporting;

/// <summary>
/// The seven reports of docs/PLAN.md, section 30, read off a warehouse that has actually done
/// something: flour and sugar received, moved to the line, counted, and turned into cookies.
/// </summary>
public class ReportingServiceTests
{
    private readonly InventoryFixture _fixture = new();
    private readonly BillOfMaterialService _boms;
    private readonly ProductionOrderService _orders;
    private readonly ReportingService _reports;

    public ReportingServiceTests()
    {
        _boms = new BillOfMaterialService(_fixture.Db, NullLogger<BillOfMaterialService>.Instance);
        _orders = new ProductionOrderService(
            _fixture.Db,
            _fixture.Movements,
            _fixture.CurrentUser,
            TimeProvider.System,
            NullLogger<ProductionOrderService>.Instance);
        _reports = new ReportingService(_fixture.Db);
    }

    private StockMovementService Movements => _fixture.Movements;

    private async Task ConfirmAsync(CreateStockMovementRequest request)
    {
        var draft = await Movements.CreateAsync(request, default);

        await Movements.ConfirmAsync(draft.Id, default);
    }

    /// <summary>500 kg of flour and 200 kg of sugar in, 300 and 150 of them moved to the line.</summary>
    private async Task ReceiveAndTransferAsync()
    {
        await ConfirmAsync(_fixture.Receipt(_fixture.MainLocation,
            (_fixture.Flour, 500m), (_fixture.Sugar, 200m)));

        await ConfirmAsync(_fixture.Transfer(_fixture.MainLocation, _fixture.ProductionLocation,
            (_fixture.Flour, 300m), (_fixture.Sugar, 150m)));
    }

    /// <summary>1,000 cookies from the flour and sugar on the line.</summary>
    private async Task<ProductionOrderResponse> ProduceAsync(decimal produced = 1000m)
    {
        await _boms.CreateAsync(
            new CreateBillOfMaterialRequest(_fixture.Cookies.Id, 100m, "Cookie", null,
            [
                new CreateBillOfMaterialItemRequest(_fixture.Flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(_fixture.Sugar.Id, 4m)
            ]),
            default);

        var order = await _orders.CreateAsync(
            new CreateProductionOrderRequest(
                _fixture.Cookies.Id, 1000m, _fixture.ProductionLocation.Id, _fixture.MainLocation.Id,
                null, null, null),
            default);

        await _orders.PlanAsync(order.Id, default);
        await _orders.StartAsync(order.Id, default);

        return await _orders.CompleteAsync(
            order.Id, new CompleteProductionOrderRequest(produced, null), default);
    }

    private Task AdjustAsync(StorageLocation location, Product product, decimal quantity, bool surplus) =>
        ConfirmAsync(new CreateStockMovementRequest(
            MovementType.Adjustment,
            surplus ? null : location.Id,
            surplus ? location.Id : null,
            surplus ? "Found during count" : "Spillage",
            [new CreateStockMovementLineRequest(product.Id, quantity)]));

    [Fact]
    public async Task Current_stock_totals_a_product_across_its_locations()
    {
        await ReceiveAndTransferAsync();

        var report = await _reports.CurrentStockAsync(new CurrentStockQuery(), default);

        var flour = report.Items.Single(r => r.Sku == "FLOUR-001");
        Assert.Equal(500m, flour.Quantity);
        Assert.Equal(500m, flour.AvailableQuantity);
        Assert.Equal(2, flour.LocationCount);
        Assert.Equal("kg", flour.UnitOfMeasureCode);
        Assert.Equal(ProductType.RawMaterial, flour.ProductType);

        // Nothing was ever produced, so the cookies have no balance at all: the report reads
        // balances, not the catalogue.
        Assert.DoesNotContain(report.Items, r => r.Sku == "COOKIE-001");

        // A balance that has fallen to zero is hidden by default and shown on request.
        await AdjustAsync(_fixture.MainLocation, _fixture.Sugar, 50m, surplus: false);
        await AdjustAsync(_fixture.ProductionLocation, _fixture.Sugar, 150m, surplus: false);

        var inStock = await _reports.CurrentStockAsync(new CurrentStockQuery(), default);
        Assert.DoesNotContain(inStock.Items, r => r.Sku == "SUGAR-001");

        var everything = await _reports.CurrentStockAsync(new CurrentStockQuery { OnlyInStock = false }, default);
        Assert.Contains(everything.Items, r => r.Sku == "SUGAR-001" && r.Quantity == 0m);
    }

    [Fact]
    public async Task Current_stock_shows_what_a_reservation_has_taken_out_of_reach()
    {
        await ReceiveAndTransferAsync();

        await _boms.CreateAsync(
            new CreateBillOfMaterialRequest(_fixture.Cookies.Id, 100m, "Cookie", null,
                [new CreateBillOfMaterialItemRequest(_fixture.Flour.Id, 10m)]),
            default);

        var order = await _orders.CreateAsync(
            new CreateProductionOrderRequest(
                _fixture.Cookies.Id, 1000m, _fixture.ProductionLocation.Id, _fixture.MainLocation.Id,
                null, null, null),
            default);

        await _orders.PlanAsync(order.Id, default);

        var flour = (await _reports.CurrentStockAsync(new CurrentStockQuery(), default))
            .Items.Single(r => r.Sku == "FLOUR-001");

        Assert.Equal(500m, flour.Quantity);
        Assert.Equal(100m, flour.ReservedQuantity);
        Assert.Equal(400m, flour.AvailableQuantity);
    }

    [Fact]
    public async Task Stock_by_warehouse_splits_the_same_balances_by_who_holds_them()
    {
        await ReceiveAndTransferAsync();

        var report = await _reports.StockByWarehouseAsync(new WarehouseStockQuery(), default);

        Assert.Equal(
            [("MAIN", "FLOUR-001", 200m), ("MAIN", "SUGAR-001", 50m),
             ("PROD", "FLOUR-001", 300m), ("PROD", "SUGAR-001", 150m)],
            report.Items.Select(r => (r.WarehouseCode, r.Sku, r.Quantity)));

        var production = await _reports.StockByWarehouseAsync(
            new WarehouseStockQuery { ProductId = _fixture.Flour.Id }, default);

        Assert.Equal([200m, 300m], production.Items.Select(r => r.Quantity));
    }

    [Fact]
    public async Task The_movement_history_reads_the_journal_line_by_line()
    {
        await ReceiveAndTransferAsync();
        await ProduceAsync();

        var flour = await _reports.MovementHistoryAsync(
            new MovementHistoryQuery { ProductId = _fixture.Flour.Id, Sort = "occurredAt" }, default);

        Assert.Equal(
            [MovementType.Receipt, MovementType.Transfer, MovementType.Consumption],
            flour.Items.Select(r => r.MovementType));
        Assert.Equal([500m, 300m, 100m], flour.Items.Select(r => r.Quantity));
        Assert.All(flour.Items, row => Assert.Equal("kg", row.UnitOfMeasureCode));
        Assert.All(flour.Items, row => Assert.NotNull(row.ConfirmedBy));

        // The consumption knows the run behind it; the receipt has none.
        Assert.NotNull(flour.Items.Last().ProductionOrderId);
        Assert.Null(flour.Items.First().ProductionOrderId);

        var onTheLine = await _reports.MovementHistoryAsync(
            new MovementHistoryQuery { LocationId = _fixture.ProductionLocation.Id }, default);

        Assert.All(onTheLine.Items, row => Assert.Contains(
            _fixture.ProductionLocation.Id,
            new[] { row.SourceLocationId, row.DestinationLocationId }));
    }

    [Fact]
    public async Task A_draft_movement_is_in_no_report()
    {
        await ReceiveAndTransferAsync();

        await Movements.CreateAsync(_fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 999m)), default);

        var history = await _reports.MovementHistoryAsync(new MovementHistoryQuery(), default);
        Assert.DoesNotContain(history.Items, row => row.Quantity == 999m);

        var stock = await _reports.CurrentStockAsync(new CurrentStockQuery(), default);
        Assert.Equal(500m, stock.Items.Single(r => r.Sku == "FLOUR-001").Quantity);
    }

    [Fact]
    public async Task The_production_history_reports_the_yield_of_a_finished_run()
    {
        await ReceiveAndTransferAsync();
        var order = await ProduceAsync(produced: 940m);

        var report = await _reports.ProductionHistoryAsync(new ProductionHistoryQuery(), default);

        var row = Assert.Single(report.Items);
        Assert.Equal(order.Number, row.Number);
        Assert.Equal(ProductionOrderStatus.Completed, row.Status);
        Assert.Equal(1000m, row.PlannedQuantity);
        Assert.Equal(940m, row.ProducedQuantity);
        Assert.Equal(94m, row.YieldPercent);
        Assert.Equal("pcs", row.UnitOfMeasureCode);
        Assert.NotNull(row.StartedAt);
        Assert.NotNull(row.CompletedAt);
    }

    [Fact]
    public async Task A_run_that_has_not_delivered_yet_reports_no_yield()
    {
        await ReceiveAndTransferAsync();

        await _boms.CreateAsync(
            new CreateBillOfMaterialRequest(_fixture.Cookies.Id, 100m, "Cookie", null,
                [new CreateBillOfMaterialItemRequest(_fixture.Flour.Id, 10m)]),
            default);

        await _orders.CreateAsync(
            new CreateProductionOrderRequest(
                _fixture.Cookies.Id, 1000m, _fixture.ProductionLocation.Id, _fixture.MainLocation.Id,
                null, null, null),
            default);

        var row = Assert.Single((await _reports.ProductionHistoryAsync(new ProductionHistoryQuery(), default)).Items);

        Assert.Equal(ProductionOrderStatus.Draft, row.Status);
        Assert.Equal(0m, row.ProducedQuantity);
        Assert.Null(row.YieldPercent);
    }

    [Fact]
    public async Task Material_consumption_and_finished_goods_total_what_production_actually_did()
    {
        await ReceiveAndTransferAsync();
        await ProduceAsync();

        var consumption = await _reports.MaterialConsumptionAsync(new ProductionTotalsQuery(), default);

        Assert.Equal(
            [("FLOUR-001", 100m), ("SUGAR-001", 40m)],
            consumption.Items.OrderBy(r => r.Sku).Select(r => (r.Sku, r.ConsumedQuantity)));
        Assert.All(consumption.Items, row =>
        {
            Assert.Equal(1, row.MovementCount);
            Assert.NotNull(row.FirstConsumedAt);
            Assert.NotNull(row.LastConsumedAt);
        });

        // Most consumed first: what a totals report is opened for.
        Assert.Equal("FLOUR-001", consumption.Items.First().Sku);

        var finished = await _reports.FinishedGoodsAsync(new ProductionTotalsQuery(), default);

        var cookies = Assert.Single(finished.Items);
        Assert.Equal("COOKIE-001", cookies.Sku);
        Assert.Equal(1000m, cookies.ProducedQuantity);
        Assert.Equal("pcs", cookies.UnitOfMeasureCode);
    }

    [Fact]
    public async Task A_period_that_ends_before_the_work_started_totals_nothing()
    {
        await ReceiveAndTransferAsync();
        await ProduceAsync();

        var before = await _reports.MaterialConsumptionAsync(
            new ProductionTotalsQuery { To = DateTime.UtcNow.AddDays(-1) }, default);

        Assert.Empty(before.Items);

        var since = await _reports.MaterialConsumptionAsync(
            new ProductionTotalsQuery { From = DateTime.UtcNow.AddDays(-1) }, default);

        Assert.Equal(2, since.Items.Count);
    }

    [Fact]
    public async Task The_adjustment_report_tells_a_surplus_from_a_shortage()
    {
        await ReceiveAndTransferAsync();

        await AdjustAsync(_fixture.MainLocation, _fixture.Flour, 12m, surplus: true);
        await AdjustAsync(_fixture.ProductionLocation, _fixture.Sugar, 3m, surplus: false);

        var report = await _reports.AdjustmentsAsync(new AdjustmentReportQuery { Sort = "occurredAt" }, default);

        Assert.Equal(2, report.Items.Count);

        var surplus = report.Items.Single(r => r.IsSurplus);
        Assert.Equal("FLOUR-001", surplus.Sku);
        Assert.Equal(12m, surplus.Quantity);
        Assert.Equal("A-01", surplus.LocationCode);
        Assert.Equal("MAIN", surplus.WarehouseCode);
        Assert.Equal("Found during count", surplus.Reason);
        Assert.NotNull(surplus.ConfirmedBy);

        var shortage = report.Items.Single(r => !r.IsSurplus);
        Assert.Equal("SUGAR-001", shortage.Sku);
        Assert.Equal("LINE-01", shortage.LocationCode);
        Assert.Equal("Spillage", shortage.Reason);

        // Nothing but adjustments: a transfer is not a correction.
        var shortagesOnly = await _reports.AdjustmentsAsync(
            new AdjustmentReportQuery { IsSurplus = false }, default);

        Assert.Equal("SUGAR-001", Assert.Single(shortagesOnly.Items).Sku);
    }
}
