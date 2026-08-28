using System.Net;
using System.Net.Http.Json;
using FlowStock.Application.Catalog;
using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using FlowStock.Application.Notifications;
using FlowStock.Application.Production;
using FlowStock.Application.Warehouses;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Notifications;
using FlowStock.Domain.Warehouses;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

/// <summary>
/// Notifications over HTTP (docs/PLAN.md, section 31): what an operation reports as it happens,
/// and what the scan notices. The background timer is off in tests, so the scan runs deliberately
/// and nothing races an assertion.
/// </summary>
[Collection(ApiCollection.Name)]
public class NotificationEndpointTests(FlowStockApiFactory factory)
{
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body = null)
    {
        var response = await client.PostAsJsonAsync(url, body ?? new { }, ApiJson.Options);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(ApiJson.Options))!;
    }

    /// <summary>A batch-tracked flour, a line to put it on, and a recipe that uses it.</summary>
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

        var flour = await PostAsync<ProductResponse>(admin, "/api/products",
            new CreateProductRequest(Unique("FLOUR"), "Flour", null, ProductType.RawMaterial, kilogram.Id, true));
        var cookie = await PostAsync<ProductResponse>(admin, "/api/products",
            new CreateProductRequest(Unique("COOKIE"), "Cookie", null, ProductType.FinishedProduct, piece.Id));

        async Task<StorageLocationResponse> LocationAsync(string code, WarehouseType type, string locationCode)
        {
            var created = await PostAsync<WarehouseResponse>(admin, "/api/warehouses",
                new CreateWarehouseRequest(Unique(code), code, null, type));

            return await PostAsync<StorageLocationResponse>(admin, "/api/storage-locations",
                new CreateStorageLocationRequest(created.Id, locationCode, locationCode, null));
        }

        var store = await LocationAsync("MAIN", WarehouseType.RawMaterials, "A-01");
        var line = await LocationAsync("PROD", WarehouseType.Production, "LINE-01");
        var finished = await LocationAsync("FIN", WarehouseType.FinishedGoods, "FG-01");

        await PostAsync<BillOfMaterialResponse>(production, "/api/boms",
            new CreateBillOfMaterialRequest(cookie.Id, 100m, "Cookie", null,
                [new CreateBillOfMaterialItemRequest(flour.Id, 10m)]));

        return new Plant(admin, warehouse, production, cookie, flour, store, line, finished);
    }

    private static async Task<StockMovementResponse> MoveAsync(
        Plant plant,
        MovementType type,
        Guid? source,
        Guid? destination,
        params CreateStockMovementLineRequest[] lines)
    {
        var draft = await PostAsync<StockMovementResponse>(plant.Warehouse, "/api/stock-movements",
            new CreateStockMovementRequest(type, source, destination, null, lines));

        return await PostAsync<StockMovementResponse>(
            plant.Warehouse, $"/api/stock-movements/{draft.Id}/confirm");
    }

    private static async Task<IReadOnlyList<NotificationResponse>> InboxAsync(
        HttpClient client,
        NotificationType? type = null,
        Guid? productionOrderId = null)
    {
        var query = type is null ? string.Empty : $"?type={type}";

        if (productionOrderId is { } orderId)
        {
            query += (query.Length == 0 ? "?" : "&") + $"productionOrderId={orderId}";
        }

        var page = await client.GetFromJsonAsync<PagedResult<NotificationResponse>>(
            $"/api/notifications{query}", ApiJson.Options);

        return page!.Items;
    }

    /// <summary>
    /// The Phase 10 Definition of Done over HTTP: a transfer and a finished run report themselves,
    /// and the inbox can be read and marked read.
    /// </summary>
    [Fact]
    public async Task Operations_report_themselves_as_they_happen()
    {
        var plant = await ArrangeAsync();

        var batch = await PostAsync<BatchResponse>(plant.Warehouse, "/api/batches",
            new CreateBatchRequest(plant.Flour.Id, Unique("FL"), "Supplier A", null, Today.AddDays(90), null));

        await MoveAsync(plant, MovementType.Receipt, null, plant.Store.Id,
            new CreateStockMovementLineRequest(plant.Flour.Id, 500m, batch.Id));

        var transfer = await MoveAsync(plant, MovementType.Transfer, plant.Store.Id, plant.Line.Id,
            new CreateStockMovementLineRequest(plant.Flour.Id, 300m, batch.Id));

        var transfers = await InboxAsync(plant.Production, NotificationType.TransferReceived);
        var arrival = Assert.Single(transfers, n => n.StockMovementId == transfer.Id);

        Assert.Equal(plant.Line.Id, arrival.LocationId);
        Assert.Equal("LINE-01", arrival.LocationCode);
        Assert.Equal(transfer.Number, arrival.StockMovementNumber);
        Assert.False(arrival.IsRead);

        var order = await PostAsync<ProductionOrderResponse>(plant.Production, "/api/production-orders",
            new CreateProductionOrderRequest(
                plant.Cookie.Id, 1000m, plant.Line.Id, plant.Finished.Id, null, null, null,
                [new ProductionOrderMaterialBatchRequest(plant.Flour.Id, batch.Id)]));

        await PostAsync<ProductionOrderResponse>(plant.Production, $"/api/production-orders/{order.Id}/plan");
        await PostAsync<ProductionOrderResponse>(plant.Production, $"/api/production-orders/{order.Id}/start");
        await PostAsync<ProductionOrderResponse>(
            plant.Production, $"/api/production-orders/{order.Id}/complete",
            new CompleteProductionOrderRequest(940m, null));

        var completed = Assert.Single(await InboxAsync(plant.Production, productionOrderId: order.Id));

        Assert.Equal(NotificationType.ProductionCompleted, completed.Type);
        Assert.Equal(order.Number, completed.ProductionOrderNumber);
        Assert.Equal(plant.Cookie.Sku, completed.Sku);
        Assert.Contains("940", completed.Message);

        // Read is a state of the notification, not of a person: there are no per-user inboxes yet.
        var read = await PostAsync<NotificationResponse>(
            plant.Production, $"/api/notifications/{completed.Id}/read");

        Assert.True(read.IsRead);
        Assert.NotNull(read.ReadBy);
        Assert.NotNull(read.ReadAt);

        var unread = await PostAsync<NotificationResponse>(
            plant.Production, $"/api/notifications/{completed.Id}/unread");

        Assert.False(unread.IsRead);
        Assert.Null(unread.ReadBy);
    }

    /// <summary>
    /// The conditions no operation reports: a lot past its date with stock on it, and a draft run
    /// the line cannot feed. Running the scan twice must not report either of them twice.
    /// </summary>
    [Fact]
    public async Task The_scan_finds_what_no_single_operation_would_report()
    {
        var plant = await ArrangeAsync();

        var expired = await PostAsync<BatchResponse>(plant.Warehouse, "/api/batches",
            new CreateBatchRequest(plant.Flour.Id, Unique("OLD"), "Supplier A", null, Today.AddDays(-1), null));

        await MoveAsync(plant, MovementType.Receipt, null, plant.Line.Id,
            new CreateStockMovementLineRequest(plant.Flour.Id, 40m, expired.Id));

        // 1,000 cookies need 100 kg of flour, and the line holds 40.
        var order = await PostAsync<ProductionOrderResponse>(plant.Production, "/api/production-orders",
            new CreateProductionOrderRequest(
                plant.Cookie.Id, 1000m, plant.Line.Id, plant.Finished.Id, null, null, null,
                [new ProductionOrderMaterialBatchRequest(plant.Flour.Id, expired.Id)]));

        var scan = await PostAsync<NotificationScanResponse>(plant.Admin, "/api/notifications/scan");

        Assert.True(scan.ExpiredBatches >= 1);
        Assert.True(scan.ProductionShortages >= 1);

        var expiries = await InboxAsync(plant.Production, NotificationType.ExpiredBatch);
        var expiry = Assert.Single(expiries, n => n.BatchId == expired.Id);
        Assert.Equal(expired.Number, expiry.BatchNumber);
        Assert.Contains("40", expiry.Message);

        var shortage = Assert.Single(
            await InboxAsync(plant.Production, NotificationType.ProductionShortage),
            n => n.ProductionOrderId == order.Id);

        Assert.Equal(plant.Flour.Id, shortage.ProductId);
        Assert.Equal("LINE-01", shortage.LocationCode);

        // One event, one notification, however often the scan runs.
        await PostAsync<NotificationScanResponse>(plant.Admin, "/api/notifications/scan");

        Assert.Single(await InboxAsync(plant.Production, NotificationType.ExpiredBatch),
            n => n.BatchId == expired.Id);
        Assert.Single(await InboxAsync(plant.Production, NotificationType.ProductionShortage),
            n => n.ProductionOrderId == order.Id);
    }

    [Fact]
    public async Task Reading_notifications_is_open_to_everyone_and_scanning_is_not()
    {
        var plant = await ArrangeAsync();

        var viewer = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.ViewerEmail, FlowStockApiFactory.ViewerPassword);

        var readable = await viewer.GetAsync("/api/notifications");
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);

        // Running the checks by hand is an administrator's business.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await plant.Production.PostAsync("/api/notifications/scan", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsync("/api/notifications/scan", null)).StatusCode);

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/notifications")).StatusCode);

        var missing = await plant.Production.PostAsync($"/api/notifications/{Guid.NewGuid()}/read", null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("NOTIFICATION_NOT_FOUND",
            (await missing.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code);
    }

    private record Plant(
        HttpClient Admin,
        HttpClient Warehouse,
        HttpClient Production,
        ProductResponse Cookie,
        ProductResponse Flour,
        StorageLocationResponse Store,
        StorageLocationResponse Line,
        StorageLocationResponse Finished);
}
