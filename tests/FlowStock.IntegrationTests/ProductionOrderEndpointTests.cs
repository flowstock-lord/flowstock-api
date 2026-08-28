using System.Net;
using System.Net.Http.Json;
using FlowStock.Application.Catalog;
using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using FlowStock.Application.Production;
using FlowStock.Application.Warehouses;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;
using FlowStock.Domain.Warehouses;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ProductionOrderEndpointTests(FlowStockApiFactory factory)
{
    private Task<HttpClient> AdminClient() =>
        factory.AuthenticatedClientAsync(FlowStockApiFactory.AdminEmail, FlowStockApiFactory.AdminPassword);

    private Task<HttpClient> WarehouseClient() =>
        factory.AuthenticatedClientAsync(
            FlowStockApiFactory.WarehouseManagerEmail, FlowStockApiFactory.WarehouseManagerPassword);

    private Task<HttpClient> ViewerClient() =>
        factory.AuthenticatedClientAsync(FlowStockApiFactory.ViewerEmail, FlowStockApiFactory.ViewerPassword);

    private Task<HttpClient> ProductionClient() =>
        factory.AuthenticatedClientAsync(
            FlowStockApiFactory.ProductionManagerEmail, FlowStockApiFactory.ProductionManagerPassword);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body = null)
    {
        var response = await client.PostAsJsonAsync(url, body ?? new { }, ApiJson.Options);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(ApiJson.Options))!;
    }

    /// <summary>
    /// The factory of docs/PLAN.md, section 40: a main warehouse holding flour and sugar, a
    /// production line, a finished goods warehouse, and the cookie recipe.
    /// </summary>
    private async Task<Plant> ArrangeAsync()
    {
        var admin = await AdminClient();

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

        async Task<StorageLocationResponse> LocationAsync(string warehouseCode, WarehouseType type, string code)
        {
            var warehouse = await PostAsync<WarehouseResponse>(admin, "/api/warehouses",
                new CreateWarehouseRequest(Unique(warehouseCode), warehouseCode, null, type));

            return await PostAsync<StorageLocationResponse>(admin, "/api/storage-locations",
                new CreateStorageLocationRequest(warehouse.Id, code, code, null));
        }

        var line = await LocationAsync("PROD", WarehouseType.Production, "LINE-01");
        var finished = await LocationAsync("FIN", WarehouseType.FinishedGoods, "FG-01");

        var production = await ProductionClient();

        var bom = await PostAsync<BillOfMaterialResponse>(production, "/api/boms",
            new CreateBillOfMaterialRequest(cookie.Id, 100m, "Cookie", null,
            [
                new CreateBillOfMaterialItemRequest(flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(sugar.Id, 4m)
            ]));

        return new Plant(production, await WarehouseClient(), cookie, flour, sugar, line, finished, bom);
    }

    /// <summary>Puts materials on the production line: a receipt the warehouse confirms.</summary>
    private static async Task StockTheLineAsync(Plant plant, decimal flour, decimal sugar)
    {
        var receipt = await PostAsync<StockMovementResponse>(plant.Warehouse, "/api/stock-movements",
            new CreateStockMovementRequest(MovementType.Receipt, null, plant.Line.Id, "Supplier delivery",
            [
                new CreateStockMovementLineRequest(plant.Flour.Id, flour),
                new CreateStockMovementLineRequest(plant.Sugar.Id, sugar)
            ]));

        await PostAsync<StockMovementResponse>(plant.Warehouse, $"/api/stock-movements/{receipt.Id}/confirm");
    }

    private static Task<ProductionOrderResponse> CreateOrderAsync(
        Plant plant,
        decimal quantity,
        Guid? bomId = null)
        => PostAsync<ProductionOrderResponse>(plant.Production, "/api/production-orders",
            new CreateProductionOrderRequest(
                plant.Cookie.Id, quantity, plant.Line.Id, plant.Finished.Id, bomId, null, null));

    private static async Task<StockResponse?> BalanceAsync(HttpClient client, Guid productId, Guid locationId)
    {
        var page = await client.GetFromJsonAsync<PagedResult<StockResponse>>(
            $"/api/stock/{productId}?locationId={locationId}", ApiJson.Options);

        return page!.Items.SingleOrDefault();
    }

    /// <summary>
    /// The Phase 6 Definition of Done over HTTP: raw materials on the line, a production order
    /// that consumes them, finished goods in the finished goods warehouse, and every stock change
    /// traceable to the order that caused it (docs/PLAN.md, sections 16, 17, 19 and 40).
    /// </summary>
    [Fact]
    public async Task A_production_order_consumes_materials_and_delivers_finished_goods()
    {
        var plant = await ArrangeAsync();
        await StockTheLineAsync(plant, flour: 500m, sugar: 200m);

        var order = await CreateOrderAsync(plant, 1000m);

        Assert.Equal(ProductionOrderStatus.Draft, order.Status);
        Assert.StartsWith("PRD-", order.Number);
        Assert.Equal(plant.Bom.Id, order.BillOfMaterialId);
        Assert.Equal(
            [(plant.Flour.Sku, 100m), (plant.Sugar.Sku, 40m)],
            order.Materials.OrderBy(m => m.ComponentName).Select(m => (m.ComponentSku, m.RequiredQuantity)));

        // Planning reserves the materials: still in stock, no longer available to anybody else.
        var planned = await PostAsync<ProductionOrderResponse>(
            plant.Production, $"/api/production-orders/{order.Id}/plan");

        Assert.Equal(ProductionOrderStatus.Planned, planned.Status);

        var reserved = await BalanceAsync(plant.Warehouse, plant.Flour.Id, plant.Line.Id);
        Assert.Equal(500m, reserved!.Quantity);
        Assert.Equal(100m, reserved.ReservedQuantity);
        Assert.Equal(400m, reserved.AvailableQuantity);

        var started = await PostAsync<ProductionOrderResponse>(
            plant.Production, $"/api/production-orders/{order.Id}/start");

        Assert.Equal(ProductionOrderStatus.InProgress, started.Status);
        Assert.NotNull(started.ActualStartAt);

        var consumed = await BalanceAsync(plant.Warehouse, plant.Flour.Id, plant.Line.Id);
        Assert.Equal(400m, consumed!.Quantity);
        Assert.Equal(0m, consumed.ReservedQuantity);
        Assert.Equal(160m, (await BalanceAsync(plant.Warehouse, plant.Sugar.Id, plant.Line.Id))!.Quantity);

        var completed = await PostAsync<ProductionOrderResponse>(
            plant.Production, $"/api/production-orders/{order.Id}/complete",
            new CompleteProductionOrderRequest(null, null));

        Assert.Equal(ProductionOrderStatus.Completed, completed.Status);
        Assert.Equal(1000m, completed.ProducedQuantity);
        Assert.Equal(1000m,
            (await BalanceAsync(plant.Warehouse, plant.Cookie.Id, plant.Finished.Id))!.Quantity);

        // Backward traceability: the run's own movements, both confirmed, both pointing at it.
        var movements = await plant.Production.GetFromJsonAsync<PagedResult<StockMovementResponse>>(
            $"/api/stock-movements?productionOrderId={order.Id}&sort=number", ApiJson.Options);

        Assert.Equal(
            [MovementType.Consumption, MovementType.ProductionOutput],
            movements!.Items.Select(m => m.MovementType));
        Assert.All(movements.Items, movement =>
        {
            Assert.Equal(MovementStatus.Confirmed, movement.Status);
            Assert.Equal(order.Id, movement.ProductionOrderId);
            Assert.Contains(order.Number, movement.Reason);
        });

        // Forward traceability: which runs a given raw material went into.
        var runsUsingFlour = await plant.Production.GetFromJsonAsync<PagedResult<ProductionOrderResponse>>(
            $"/api/production-orders?componentProductId={plant.Flour.Id}", ApiJson.Options);

        Assert.Contains(runsUsingFlour!.Items, o => o.Id == order.Id);
    }

    [Fact]
    public async Task Planning_a_run_the_line_cannot_feed_is_rejected_with_the_shortfall()
    {
        var plant = await ArrangeAsync();
        await StockTheLineAsync(plant, flour: 50m, sugar: 200m);

        var order = await CreateOrderAsync(plant, 1000m);

        var response = await plant.Production.PostAsJsonAsync(
            $"/api/production-orders/{order.Id}/plan", new { }, ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("INSUFFICIENT_STOCK", error!.Code);

        // Nothing was reserved, and the order is still a draft.
        var balance = await BalanceAsync(plant.Warehouse, plant.Flour.Id, plant.Line.Id);
        Assert.Equal(0m, balance!.ReservedQuantity);

        var unchanged = await plant.Production.GetFromJsonAsync<ProductionOrderResponse>(
            $"/api/production-orders/{order.Id}", ApiJson.Options);
        Assert.Equal(ProductionOrderStatus.Draft, unchanged!.Status);
    }

    /// <summary>
    /// A reservation is what planning buys: the warehouse can no longer move the material out
    /// from under a run that is about to use it (CLAUDE.md, rule 6).
    /// </summary>
    [Fact]
    public async Task Reserved_material_cannot_be_transferred_away()
    {
        var plant = await ArrangeAsync();
        await StockTheLineAsync(plant, flour: 120m, sugar: 200m);

        var order = await CreateOrderAsync(plant, 1000m);
        await PostAsync<ProductionOrderResponse>(plant.Production, $"/api/production-orders/{order.Id}/plan");

        var transfer = await PostAsync<StockMovementResponse>(plant.Warehouse, "/api/stock-movements",
            new CreateStockMovementRequest(MovementType.Transfer, plant.Line.Id, plant.Finished.Id, null,
                [new CreateStockMovementLineRequest(plant.Flour.Id, 30m)]));

        var confirm = await plant.Warehouse.PostAsync($"/api/stock-movements/{transfer.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Equal("INSUFFICIENT_STOCK",
            (await confirm.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code);
        Assert.Equal(120m, (await BalanceAsync(plant.Warehouse, plant.Flour.Id, plant.Line.Id))!.Quantity);
    }

    [Fact]
    public async Task Cancelling_a_planned_run_releases_its_reservations()
    {
        var plant = await ArrangeAsync();
        await StockTheLineAsync(plant, flour: 500m, sugar: 200m);

        var order = await CreateOrderAsync(plant, 1000m);
        await PostAsync<ProductionOrderResponse>(plant.Production, $"/api/production-orders/{order.Id}/plan");

        var cancelled = await PostAsync<ProductionOrderResponse>(
            plant.Production, $"/api/production-orders/{order.Id}/cancel",
            new CancelProductionOrderRequest("Line broke down"));

        Assert.Equal(ProductionOrderStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CancelledBy);

        var balance = await BalanceAsync(plant.Warehouse, plant.Flour.Id, plant.Line.Id);
        Assert.Equal(500m, balance!.Quantity);
        Assert.Equal(0m, balance.ReservedQuantity);
    }

    /// <summary>The workflow only runs forwards (docs/PLAN.md, section 18).</summary>
    [Fact]
    public async Task A_run_cannot_skip_or_repeat_a_step()
    {
        var plant = await ArrangeAsync();
        await StockTheLineAsync(plant, flour: 500m, sugar: 200m);

        var order = await CreateOrderAsync(plant, 1000m);

        async Task<string> RejectedAsync(string step, object? body = null)
        {
            var response = await plant.Production.PostAsJsonAsync(
                $"/api/production-orders/{order.Id}/{step}", body ?? new { }, ApiJson.Options);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            return (await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code;
        }

        Assert.Equal("PRODUCTION_ORDER_INVALID", await RejectedAsync("start"));

        await PostAsync<ProductionOrderResponse>(plant.Production, $"/api/production-orders/{order.Id}/plan");
        Assert.Equal("PRODUCTION_ORDER_INVALID", await RejectedAsync("plan"));

        await PostAsync<ProductionOrderResponse>(plant.Production, $"/api/production-orders/{order.Id}/start");

        // A started run has confirmed movements behind it: it is corrected, not cancelled.
        Assert.Equal("PRODUCTION_ORDER_INVALID", await RejectedAsync("cancel"));

        await PostAsync<ProductionOrderResponse>(
            plant.Production, $"/api/production-orders/{order.Id}/complete",
            new CompleteProductionOrderRequest(null, null));

        Assert.Equal("PRODUCTION_ORDER_ALREADY_COMPLETED",
            await RejectedAsync("complete", new CompleteProductionOrderRequest(null, null)));
    }

    /// <summary>
    /// Running production is a production responsibility. A warehouse manager may move stock, and
    /// a viewer may read the production history, but neither runs an order (docs/PLAN.md, section 25).
    /// </summary>
    [Fact]
    public async Task Running_production_needs_the_production_role()
    {
        var plant = await ArrangeAsync();
        await StockTheLineAsync(plant, flour: 500m, sugar: 200m);

        var order = await CreateOrderAsync(plant, 1000m);

        var create = await plant.Warehouse.PostAsJsonAsync("/api/production-orders",
            new CreateProductionOrderRequest(
                plant.Cookie.Id, 100m, plant.Line.Id, plant.Finished.Id, null, null, null),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var plan = await plant.Warehouse.PostAsJsonAsync(
            $"/api/production-orders/{order.Id}/plan", new { }, ApiJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, plan.StatusCode);

        // Reading is open to any authenticated user: production history is part of the audit trail.
        var viewer = await ViewerClient();

        var read = await viewer.GetFromJsonAsync<ProductionOrderResponse>(
            $"/api/production-orders/{order.Id}", ApiJson.Options);

        Assert.Equal(order.Id, read!.Id);
    }

    private record Plant(
        HttpClient Production,
        HttpClient Warehouse,
        ProductResponse Cookie,
        ProductResponse Flour,
        ProductResponse Sugar,
        StorageLocationResponse Line,
        StorageLocationResponse Finished,
        BillOfMaterialResponse Bom);
}
