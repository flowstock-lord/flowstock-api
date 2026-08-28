using System.Net;
using System.Net.Http.Json;
using FlowStock.Application.Catalog;
using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using FlowStock.Application.Production;
using FlowStock.Application.Traceability;
using FlowStock.Application.Warehouses;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Warehouses;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

/// <summary>
/// Batch tracking against the real database (docs/PLAN.md, section 20): lots are balances of their
/// own, the untracked products keep their single anonymous balance, and a lot can be traced from
/// the delivery that brought it to the goods it became.
/// </summary>
[Collection(ApiCollection.Name)]
public class BatchEndpointTests(FlowStockApiFactory factory)
{
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body = null)
    {
        var response = await client.PostAsJsonAsync(url, body ?? new { }, ApiJson.Options);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(ApiJson.Options))!;
    }

    /// <summary>Flour kept lot by lot, sugar not kept lot by lot, and somewhere to put them.</summary>
    private async Task<Plant> ArrangeAsync()
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

        async Task<ProductResponse> ProductAsync(
            string sku, string name, ProductType type, Guid unitId, bool batchTracked) =>
            await PostAsync<ProductResponse>(admin, "/api/products",
                new CreateProductRequest(Unique(sku), name, null, type, unitId, batchTracked));

        var cookie = await ProductAsync("COOKIE", "Cookie", ProductType.FinishedProduct, piece.Id, true);
        var flour = await ProductAsync("FLOUR", "Flour", ProductType.RawMaterial, kilogram.Id, true);
        var sugar = await ProductAsync("SUGAR", "Sugar", ProductType.RawMaterial, kilogram.Id, false);

        async Task<StorageLocationResponse> LocationAsync(string code, WarehouseType type, string locationCode)
        {
            var created = await PostAsync<WarehouseResponse>(admin, "/api/warehouses",
                new CreateWarehouseRequest(Unique(code), code, null, type));

            return await PostAsync<StorageLocationResponse>(admin, "/api/storage-locations",
                new CreateStorageLocationRequest(created.Id, locationCode, locationCode, null));
        }

        var line = await LocationAsync("PROD", WarehouseType.Production, "LINE-01");
        var finished = await LocationAsync("FIN", WarehouseType.FinishedGoods, "FG-01");

        return new Plant(admin, warehouse, production, cookie, flour, sugar, line, finished);
    }

    private static Task<BatchResponse> RegisterAsync(
        Plant plant,
        ProductResponse product,
        string number,
        DateOnly? expiry = null)
        => PostAsync<BatchResponse>(plant.Warehouse, "/api/batches",
            new CreateBatchRequest(product.Id, number, "Supplier A", Today.AddDays(-2), expiry, null));

    private static async Task<StockMovementResponse> ReceiveAsync(
        Plant plant,
        StorageLocationResponse destination,
        params CreateStockMovementLineRequest[] lines)
    {
        var draft = await PostAsync<StockMovementResponse>(plant.Warehouse, "/api/stock-movements",
            new CreateStockMovementRequest(
                MovementType.Receipt, null, destination.Id, "Supplier delivery", lines));

        return await PostAsync<StockMovementResponse>(
            plant.Warehouse, $"/api/stock-movements/{draft.Id}/confirm");
    }

    private static async Task<IReadOnlyList<StockResponse>> BalancesAsync(
        Plant plant,
        ProductResponse product,
        StorageLocationResponse location)
    {
        var page = await plant.Warehouse.GetFromJsonAsync<PagedResult<StockResponse>>(
            $"/api/stock/{product.Id}?locationId={location.Id}", ApiJson.Options);

        return page!.Items;
    }

    /// <summary>
    /// Two lots of one product are two balances; a product nobody tracks keeps exactly one. The
    /// second half is what the NULLS NOT DISTINCT unique index is there for — without it every
    /// receipt of an untracked product would open a new anonymous balance.
    /// </summary>
    [Fact]
    public async Task Stock_is_kept_lot_by_lot_and_untracked_products_keep_one_balance()
    {
        var plant = await ArrangeAsync();

        var first = await RegisterAsync(plant, plant.Flour, "fl-2026-0828", Today.AddDays(180));
        var second = await RegisterAsync(plant, plant.Flour, "FL-2026-0901", Today.AddDays(200));

        Assert.Equal("FL-2026-0828", first.Number);
        Assert.False(first.IsExpired);

        await ReceiveAsync(plant, plant.Line,
            new CreateStockMovementLineRequest(plant.Flour.Id, 500m, first.Id),
            new CreateStockMovementLineRequest(plant.Flour.Id, 300m, second.Id),
            new CreateStockMovementLineRequest(plant.Sugar.Id, 200m));

        // A second delivery of the same goods: the tracked lot and the anonymous balance both grow.
        await ReceiveAsync(plant, plant.Line,
            new CreateStockMovementLineRequest(plant.Flour.Id, 100m, first.Id),
            new CreateStockMovementLineRequest(plant.Sugar.Id, 50m));

        var flour = await BalancesAsync(plant, plant.Flour, plant.Line);

        Assert.Equal(
            [("FL-2026-0828", 600m), ("FL-2026-0901", 300m)],
            flour.OrderBy(s => s.BatchNumber).Select(s => (s.BatchNumber, s.Quantity)));
        Assert.All(flour, s => Assert.NotNull(s.BatchExpiryDate));

        var sugar = Assert.Single(await BalancesAsync(plant, plant.Sugar, plant.Line));
        Assert.Null(sugar.BatchId);
        Assert.Equal(250m, sugar.Quantity);
    }

    [Fact]
    public async Task A_movement_of_a_tracked_product_must_name_a_lot_that_belongs_to_it()
    {
        var plant = await ArrangeAsync();

        var flourBatch = await RegisterAsync(plant, plant.Flour, "FL-0828");

        async Task<string> RejectedAsync(CreateStockMovementRequest request)
        {
            var response = await plant.Warehouse.PostAsJsonAsync("/api/stock-movements", request, ApiJson.Options);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            return (await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code;
        }

        var anonymous = new CreateStockMovementRequest(MovementType.Receipt, null, plant.Line.Id, null,
            [new CreateStockMovementLineRequest(plant.Flour.Id, 10m)]);
        Assert.Equal("BATCH_REQUIRED", await RejectedAsync(anonymous));

        var unwanted = new CreateStockMovementRequest(MovementType.Receipt, null, plant.Line.Id, null,
            [new CreateStockMovementLineRequest(plant.Sugar.Id, 10m, flourBatch.Id)]);
        Assert.Equal("BATCH_NOT_ALLOWED", await RejectedAsync(unwanted));

        var missing = new CreateStockMovementRequest(MovementType.Receipt, null, plant.Line.Id, null,
            [new CreateStockMovementLineRequest(plant.Flour.Id, 10m, Guid.NewGuid())]);
        Assert.Equal("BATCH_NOT_FOUND", await RejectedAsync(missing));

        // A lot of another product is not this product's lot.
        var cookieBatch = await RegisterAsync(plant, plant.Cookie, "CK-0001");
        var wrongProduct = new CreateStockMovementRequest(MovementType.Receipt, null, plant.Line.Id, null,
            [new CreateStockMovementLineRequest(plant.Flour.Id, 10m, cookieBatch.Id)]);
        Assert.Equal("BATCH_INVALID", await RejectedAsync(wrongProduct));
    }

    /// <summary>A shortage is a shortage of one lot, even when another lot is full.</summary>
    [Fact]
    public async Task Taking_more_than_one_lot_holds_is_refused_by_lot()
    {
        var plant = await ArrangeAsync();

        var small = await RegisterAsync(plant, plant.Flour, "FL-SMALL");
        var large = await RegisterAsync(plant, plant.Flour, "FL-LARGE");

        await ReceiveAsync(plant, plant.Line,
            new CreateStockMovementLineRequest(plant.Flour.Id, 50m, small.Id),
            new CreateStockMovementLineRequest(plant.Flour.Id, 900m, large.Id));

        var transfer = await PostAsync<StockMovementResponse>(plant.Warehouse, "/api/stock-movements",
            new CreateStockMovementRequest(MovementType.Transfer, plant.Line.Id, plant.Finished.Id, null,
                [new CreateStockMovementLineRequest(plant.Flour.Id, 100m, small.Id)]));

        var confirm = await plant.Warehouse.PostAsync($"/api/stock-movements/{transfer.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);

        var error = await confirm.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("INSUFFICIENT_STOCK", error!.Code);
        Assert.Contains("FL-SMALL", error.Message);
    }

    /// <summary>
    /// The Phase 8 Definition of Done over HTTP: a run takes one named lot of flour and produces a
    /// lot of cookies, and the lot can be traced in both directions (docs/PLAN.md, section 19).
    /// </summary>
    [Fact]
    public async Task A_lot_can_be_traced_from_the_delivery_to_the_goods_it_became()
    {
        var plant = await ArrangeAsync();

        var flourBatch = await RegisterAsync(plant, plant.Flour, "FL-0828", Today.AddDays(180));

        await ReceiveAsync(plant, plant.Line,
            new CreateStockMovementLineRequest(plant.Flour.Id, 500m, flourBatch.Id),
            new CreateStockMovementLineRequest(plant.Sugar.Id, 200m));

        await PostAsync<BillOfMaterialResponse>(plant.Production, "/api/boms",
            new CreateBillOfMaterialRequest(plant.Cookie.Id, 100m, "Cookie", null,
            [
                new CreateBillOfMaterialItemRequest(plant.Flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(plant.Sugar.Id, 4m)
            ]));

        var order = await PostAsync<ProductionOrderResponse>(plant.Production, "/api/production-orders",
            new CreateProductionOrderRequest(
                plant.Cookie.Id, 1000m, plant.Line.Id, plant.Finished.Id, null, null, null,
                [new ProductionOrderMaterialBatchRequest(plant.Flour.Id, flourBatch.Id)]));

        Assert.Equal("FL-0828",
            order.Materials.Single(m => m.ComponentProductId == plant.Flour.Id).BatchNumber);

        await PostAsync<ProductionOrderResponse>(plant.Production, $"/api/production-orders/{order.Id}/plan");
        await PostAsync<ProductionOrderResponse>(plant.Production, $"/api/production-orders/{order.Id}/start");

        var completed = await PostAsync<ProductionOrderResponse>(
            plant.Production, $"/api/production-orders/{order.Id}/complete",
            new CompleteProductionOrderRequest(null, null, "CK-2026-001", Today.AddDays(90)));

        Assert.Equal("CK-2026-001", completed.OutputBatchNumber);

        // The cookies are in their own lot in the finished goods warehouse.
        var cookies = Assert.Single(await BalancesAsync(plant, plant.Cookie, plant.Finished));
        Assert.Equal(completed.OutputBatchId, cookies.BatchId);
        Assert.Equal(1000m, cookies.Quantity);

        // Forward: from the flour lot to the run and the goods it became.
        var trace = await plant.Production.GetFromJsonAsync<BatchTraceResponse>(
            $"/api/traceability/batches/{flourBatch.Id}", ApiJson.Options);

        Assert.Equal("FL-0828", trace!.Number);
        Assert.Equal(400m, trace.QuantityOnHand);
        Assert.Equal("LINE-01", Assert.Single(trace.Locations).LocationCode);
        Assert.Equal([MovementType.Receipt, MovementType.Consumption], trace.History.Select(e => e.MovementType));

        var consumer = Assert.Single(trace.ConsumedBy);
        Assert.Equal(order.Number, consumer.Number);
        Assert.Equal(100m, consumer.ConsumedQuantity);
        Assert.Equal("CK-2026-001", consumer.ProducedBatchNumber);

        // Backward: from the run to the exact lot it was made of.
        var production = await plant.Production.GetFromJsonAsync<ProductionTraceResponse>(
            $"/api/traceability/production-orders/{order.Id}", ApiJson.Options);

        var flour = production!.Materials.Single(m => m.ComponentProductId == plant.Flour.Id);
        Assert.Equal("FL-0828", flour.BatchNumber);
        Assert.Equal(MovementType.Receipt, Assert.Single(flour.Sources).MovementType);
        Assert.Equal("CK-2026-001", production.Output!.BatchNumber);

        // And the finished lot knows the run that made it.
        var cookieTrace = await plant.Production.GetFromJsonAsync<BatchTraceResponse>(
            $"/api/traceability/batches/{completed.OutputBatchId}", ApiJson.Options);

        Assert.Equal(order.Id, cookieTrace!.ProducedByProductionOrderId);
        Assert.Equal(order.Number, cookieTrace.ProducedByProductionOrderNumber);
        Assert.Equal(1000m, cookieTrace.QuantityOnHand);
    }

    [Fact]
    public async Task Registering_a_lot_needs_the_warehouse_role_and_reading_one_does_not()
    {
        var plant = await ArrangeAsync();

        var batch = await RegisterAsync(plant, plant.Flour, "FL-0828", Today.AddDays(30));

        var viewer = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.ViewerEmail, FlowStockApiFactory.ViewerPassword);

        var forbidden = await viewer.PostAsJsonAsync("/api/batches",
            new CreateBatchRequest(plant.Flour.Id, "FL-9999", null, null, null, null), ApiJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var read = await viewer.GetFromJsonAsync<BatchResponse>($"/api/batches/{batch.Id}", ApiJson.Options);
        Assert.Equal(batch.Id, read!.Id);

        // A duplicate lot number for the same product is refused by the database as well as the service.
        var duplicate = await plant.Warehouse.PostAsJsonAsync("/api/batches",
            new CreateBatchRequest(plant.Flour.Id, "FL-0828", null, null, null, null), ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal("BATCH_NUMBER_EXISTS",
            (await duplicate.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code);
    }

    private record Plant(
        HttpClient Admin,
        HttpClient Warehouse,
        HttpClient Production,
        ProductResponse Cookie,
        ProductResponse Flour,
        ProductResponse Sugar,
        StorageLocationResponse Line,
        StorageLocationResponse Finished);
}
