using System.Net;
using System.Net.Http.Json;
using FlowStock.Application.Catalog;
using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using FlowStock.Application.Production;
using FlowStock.Application.Reporting;
using FlowStock.Application.Warehouses;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;
using FlowStock.Domain.Warehouses;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

/// <summary>
/// The seven reports of docs/PLAN.md, section 30, against a warehouse that has actually done a
/// day's work: a delivery, a transfer, a count correction and a production run.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReportEndpointTests(FlowStockApiFactory factory)
{
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body = null)
    {
        var response = await client.PostAsJsonAsync(url, body ?? new { }, ApiJson.Options);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(ApiJson.Options))!;
    }

    /// <summary>
    /// 500 kg of flour and 200 of sugar delivered, most of it moved to the line, 12 kg of flour
    /// found during a count, and 1,000 cookies produced from what was there.
    /// </summary>
    private async Task<Day> ArrangeAsync()
    {
        var admin = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.AdminEmail, FlowStockApiFactory.AdminPassword);
        var warehouse = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.WarehouseManagerEmail, FlowStockApiFactory.WarehouseManagerPassword);
        var production = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.ProductionManagerEmail, FlowStockApiFactory.ProductionManagerPassword);

        var kilogram = await PostAsync<UnitOfMeasureResponse>(admin, "/api/units-of-measure",
            new CreateUnitOfMeasureRequest(Unique("kg"), "Kilogram"));
        var piece = await PostAsync<UnitOfMeasureResponse>(admin, "/api/units-of-measure",
            new CreateUnitOfMeasureRequest(Unique("pc"), "Piece"));

        async Task<ProductResponse> ProductAsync(string sku, string name, ProductType type, Guid unitId) =>
            await PostAsync<ProductResponse>(admin, "/api/products",
                new CreateProductRequest(Unique(sku), name, null, type, unitId));

        var cookie = await ProductAsync("COOKIE", "Cookie", ProductType.FinishedProduct, piece.Id);
        var flour = await ProductAsync("FLOUR", "Flour", ProductType.RawMaterial, kilogram.Id);
        var sugar = await ProductAsync("SUGAR", "Sugar", ProductType.RawMaterial, kilogram.Id);

        async Task<(WarehouseResponse Warehouse, StorageLocationResponse Location)> PlaceAsync(
            string code, WarehouseType type, string locationCode)
        {
            var created = await PostAsync<WarehouseResponse>(admin, "/api/warehouses",
                new CreateWarehouseRequest(Unique(code), code, null, type));

            var location = await PostAsync<StorageLocationResponse>(admin, "/api/storage-locations",
                new CreateStorageLocationRequest(created.Id, locationCode, locationCode, null));

            return (created, location);
        }

        var (store, storeLocation) = await PlaceAsync("MAIN", WarehouseType.RawMaterials, "A-01");
        var (plant, line) = await PlaceAsync("PROD", WarehouseType.Production, "LINE-01");
        var (finishedGoods, finishedLocation) = await PlaceAsync("FIN", WarehouseType.FinishedGoods, "FG-01");

        async Task MoveAsync(
            MovementType type,
            Guid? source,
            Guid? destination,
            string? reason,
            params CreateStockMovementLineRequest[] lines)
        {
            var draft = await PostAsync<StockMovementResponse>(warehouse, "/api/stock-movements",
                new CreateStockMovementRequest(type, source, destination, reason, lines));

            await PostAsync<StockMovementResponse>(warehouse, $"/api/stock-movements/{draft.Id}/confirm");
        }

        await MoveAsync(MovementType.Receipt, null, storeLocation.Id, "Supplier delivery",
            new CreateStockMovementLineRequest(flour.Id, 500m),
            new CreateStockMovementLineRequest(sugar.Id, 200m));

        await MoveAsync(MovementType.Transfer, storeLocation.Id, line.Id, null,
            new CreateStockMovementLineRequest(flour.Id, 300m),
            new CreateStockMovementLineRequest(sugar.Id, 150m));

        await MoveAsync(MovementType.Adjustment, null, storeLocation.Id, "Found during count",
            new CreateStockMovementLineRequest(flour.Id, 12m));

        await PostAsync<BillOfMaterialResponse>(production, "/api/boms",
            new CreateBillOfMaterialRequest(cookie.Id, 100m, "Cookie", null,
            [
                new CreateBillOfMaterialItemRequest(flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(sugar.Id, 4m)
            ]));

        var order = await PostAsync<ProductionOrderResponse>(production, "/api/production-orders",
            new CreateProductionOrderRequest(
                cookie.Id, 1000m, line.Id, finishedLocation.Id, null, null, null));

        await PostAsync<ProductionOrderResponse>(production, $"/api/production-orders/{order.Id}/plan");
        await PostAsync<ProductionOrderResponse>(production, $"/api/production-orders/{order.Id}/start");

        var completed = await PostAsync<ProductionOrderResponse>(
            production, $"/api/production-orders/{order.Id}/complete",
            new CompleteProductionOrderRequest(940m, null));

        return new Day(
            warehouse, cookie, flour, sugar, store, plant, finishedGoods, storeLocation, line, completed);
    }

    private static Task<PagedResult<T>> ReportAsync<T>(HttpClient client, string url) =>
        client.GetFromJsonAsync<PagedResult<T>>(url, ApiJson.Options)!;

    [Fact]
    public async Task Current_stock_and_stock_by_warehouse_agree_on_what_is_there()
    {
        var day = await ArrangeAsync();

        var current = await ReportAsync<CurrentStockRow>(
            day.Client, $"/api/reports/current-stock?search={day.Flour.Sku}");

        var flour = Assert.Single(current.Items);
        // 500 received, 12 found during the count, 100 consumed by the run.
        Assert.Equal(412m, flour.Quantity);
        Assert.Equal(412m, flour.AvailableQuantity);
        Assert.Equal(2, flour.LocationCount);

        var byWarehouse = await ReportAsync<WarehouseStockRow>(
            day.Client, $"/api/reports/stock-by-warehouse?productId={day.Flour.Id}");

        Assert.Equal(
            [(day.Store.Code, 212m), (day.Plant.Code, 200m)],
            byWarehouse.Items.OrderByDescending(r => r.Quantity).Select(r => (r.WarehouseCode, r.Quantity)));

        // The two reports are two views of the same balances.
        Assert.Equal(flour.Quantity, byWarehouse.Items.Sum(r => r.Quantity));

        // And the cookies landed in the finished goods warehouse.
        var cookies = await ReportAsync<WarehouseStockRow>(
            day.Client, $"/api/reports/stock-by-warehouse?warehouseId={day.FinishedGoods.Id}");

        Assert.Equal(940m, Assert.Single(cookies.Items).Quantity);
    }

    [Fact]
    public async Task The_movement_history_reports_every_confirmed_line_of_the_day()
    {
        var day = await ArrangeAsync();

        var history = await ReportAsync<MovementHistoryRow>(
            day.Client, $"/api/reports/movement-history?productId={day.Flour.Id}&sort=occurredAt");

        Assert.Equal(
            [MovementType.Receipt, MovementType.Transfer, MovementType.Adjustment, MovementType.Consumption],
            history.Items.Select(r => r.MovementType));
        Assert.Equal([500m, 300m, 12m, 100m], history.Items.Select(r => r.Quantity));
        Assert.All(history.Items, row => Assert.NotNull(row.ConfirmedBy));

        // Only the consumption belongs to a production run.
        Assert.Equal(day.Order.Id, history.Items.Last().ProductionOrderId);

        var atTheStore = await ReportAsync<MovementHistoryRow>(
            day.Client, $"/api/reports/movement-history?warehouseId={day.Store.Id}&productId={day.Flour.Id}");

        Assert.Equal(3, atTheStore.Items.Count);
    }

    [Fact]
    public async Task The_production_reports_say_what_was_made_and_what_it_took()
    {
        var day = await ArrangeAsync();

        var runs = await ReportAsync<ProductionHistoryRow>(
            day.Client, $"/api/reports/production-history?productId={day.Cookie.Id}");

        var run = Assert.Single(runs.Items);
        Assert.Equal(day.Order.Number, run.Number);
        Assert.Equal(ProductionOrderStatus.Completed, run.Status);
        Assert.Equal(1000m, run.PlannedQuantity);
        Assert.Equal(940m, run.ProducedQuantity);
        Assert.Equal(94m, run.YieldPercent);

        var consumption = await ReportAsync<MaterialConsumptionRow>(
            day.Client, "/api/reports/material-consumption");

        var flour = consumption.Items.Single(r => r.Sku == day.Flour.Sku);
        Assert.Equal(100m, flour.ConsumedQuantity);
        Assert.Equal(1, flour.MovementCount);
        Assert.NotNull(flour.LastConsumedAt);

        var sugar = consumption.Items.Single(r => r.Sku == day.Sugar.Sku);
        Assert.Equal(40m, sugar.ConsumedQuantity);

        var finished = await ReportAsync<FinishedGoodsRow>(
            day.Client, $"/api/reports/finished-goods?productId={day.Cookie.Id}");

        var cookies = Assert.Single(finished.Items);
        Assert.Equal(940m, cookies.ProducedQuantity);
        Assert.Equal(1, cookies.MovementCount);
    }

    [Fact]
    public async Task The_adjustment_report_finds_the_count_correction()
    {
        var day = await ArrangeAsync();

        var report = await ReportAsync<AdjustmentRow>(
            day.Client, $"/api/reports/adjustments?warehouseId={day.Store.Id}");

        var row = Assert.Single(report.Items);
        Assert.True(row.IsSurplus);
        Assert.Equal(day.Flour.Sku, row.Sku);
        Assert.Equal(12m, row.Quantity);
        Assert.Equal("A-01", row.LocationCode);
        Assert.Equal(day.Store.Code, row.WarehouseCode);
        Assert.Equal("Found during count", row.Reason);
        Assert.NotNull(row.ConfirmedBy);

        // Nothing was ever short, so the other half of the report is empty for this warehouse.
        var shortages = await ReportAsync<AdjustmentRow>(
            day.Client, $"/api/reports/adjustments?warehouseId={day.Store.Id}&isSurplus=false");

        Assert.Empty(shortages.Items);
    }

    [Fact]
    public async Task Reports_are_open_to_any_authenticated_user_and_reject_a_sort_they_cannot_do()
    {
        var day = await ArrangeAsync();

        var viewer = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.ViewerEmail, FlowStockApiFactory.ViewerPassword);

        var readable = await ReportAsync<CurrentStockRow>(viewer, "/api/reports/current-stock");
        Assert.NotEmpty(readable.Items);

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/reports/current-stock")).StatusCode);

        var badSort = await day.Client.GetAsync("/api/reports/current-stock?sort=colour");
        Assert.Equal(HttpStatusCode.BadRequest, badSort.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed,
            (await badSort.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code);
    }

    private record Day(
        HttpClient Client,
        ProductResponse Cookie,
        ProductResponse Flour,
        ProductResponse Sugar,
        WarehouseResponse Store,
        WarehouseResponse Plant,
        WarehouseResponse FinishedGoods,
        StorageLocationResponse StoreLocation,
        StorageLocationResponse Line,
        ProductionOrderResponse Order);
}
