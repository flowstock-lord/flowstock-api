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
using FlowStock.Domain.Production;
using FlowStock.Domain.Warehouses;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

/// <summary>
/// The traceability questions of docs/PLAN.md, section 39, asked over HTTP of a run that really
/// happened: flour received, transferred, consumed, and turned into cookies.
/// </summary>
[Collection(ApiCollection.Name)]
public class TraceabilityEndpointTests(FlowStockApiFactory factory)
{
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body = null)
    {
        var response = await client.PostAsJsonAsync(url, body ?? new { }, ApiJson.Options);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(ApiJson.Options))!;
    }

    /// <summary>
    /// The whole chain of docs/PLAN.md, section 40, run once: a delivery into the main warehouse,
    /// a transfer to the line, and a production order that turns it into 1,000 cookies.
    /// </summary>
    private async Task<Run> RunProductionAsync()
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

        async Task<StockMovementResponse> MoveAsync(
            MovementType type, Guid? source, Guid? destination, decimal quantity, string? reason)
        {
            var draft = await PostAsync<StockMovementResponse>(warehouse, "/api/stock-movements",
                new CreateStockMovementRequest(type, source, destination, reason,
                    [new CreateStockMovementLineRequest(flour.Id, quantity)]));

            return await PostAsync<StockMovementResponse>(
                warehouse, $"/api/stock-movements/{draft.Id}/confirm");
        }

        var receipt = await MoveAsync(MovementType.Receipt, null, store.Id, 500m, "Supplier delivery");
        var transfer = await MoveAsync(MovementType.Transfer, store.Id, line.Id, 300m, null);

        var order = await PostAsync<ProductionOrderResponse>(production, "/api/production-orders",
            new CreateProductionOrderRequest(cookie.Id, 1000m, line.Id, finished.Id, null, null, null));

        await PostAsync<ProductionOrderResponse>(production, $"/api/production-orders/{order.Id}/plan");
        await PostAsync<ProductionOrderResponse>(production, $"/api/production-orders/{order.Id}/start");

        var completed = await PostAsync<ProductionOrderResponse>(
            production, $"/api/production-orders/{order.Id}/complete",
            new CompleteProductionOrderRequest(null, null));

        return new Run(production, cookie, flour, store, line, finished, receipt, transfer, completed);
    }

    /// <summary>"Where did this product come from?", "Who moved it?", "When did it happen?"</summary>
    [Fact]
    public async Task The_history_of_a_material_reads_the_whole_chain_back()
    {
        var run = await RunProductionAsync();

        var history = await run.Client.GetFromJsonAsync<PagedResult<ProductHistoryEntry>>(
            $"/api/traceability/products/{run.Flour.Id}/history?sort=occurredAt", ApiJson.Options);

        Assert.Equal(
            [MovementType.Receipt, MovementType.Transfer, MovementType.Consumption],
            history!.Items.Select(e => e.MovementType));

        var receipt = history.Items.First();
        Assert.Equal(run.Receipt.Number, receipt.MovementNumber);
        Assert.Equal(StockFlow.In, receipt.Flow);
        Assert.Equal(500m, receipt.Quantity);
        Assert.Equal("A-01", receipt.DestinationLocationCode);
        Assert.Equal("Supplier delivery", receipt.Reason);
        Assert.NotEqual(default, receipt.OccurredAt);

        // Who: the seeded warehouse manager, resolved to a person rather than left as an id.
        Assert.NotNull(receipt.PerformedBy.UserId);
        Assert.Equal(FlowStockApiFactory.WarehouseManagerEmail, receipt.PerformedBy.Email);
        Assert.False(string.IsNullOrWhiteSpace(receipt.PerformedBy.Name));

        var consumption = history.Items.Last();
        Assert.Equal(StockFlow.Out, consumption.Flow);
        Assert.Equal(100m, consumption.Quantity);
        Assert.Equal(run.Order.Id, consumption.ProductionOrderId);
        Assert.Equal(run.Order.Number, consumption.ProductionOrderNumber);
        Assert.Equal(FlowStockApiFactory.ProductionManagerEmail, consumption.PerformedBy.Email);

        // The same history seen from the production line: in on the transfer, out on the run.
        var atLine = await run.Client.GetFromJsonAsync<PagedResult<ProductHistoryEntry>>(
            $"/api/traceability/products/{run.Flour.Id}/history?locationId={run.Line.Id}&sort=occurredAt",
            ApiJson.Options);

        Assert.Equal([StockFlow.In, StockFlow.Out], atLine!.Items.Select(e => e.Flow));
        Assert.Equal([300m, 100m], atLine.Items.Select(e => e.Quantity));
    }

    /// <summary>Backward traceability: "What materials were used to produce this product?"</summary>
    [Fact]
    public async Task A_finished_run_shows_everything_that_went_into_it()
    {
        var run = await RunProductionAsync();

        var trace = await run.Client.GetFromJsonAsync<ProductionTraceResponse>(
            $"/api/traceability/production-orders/{run.Order.Id}", ApiJson.Options);

        Assert.Equal(ProductionOrderStatus.Completed, trace!.Status);
        Assert.Equal(run.Cookie.Sku, trace.Sku);
        Assert.Equal(1000m, trace.ProducedQuantity);
        Assert.Equal(1, trace.BillOfMaterialVersion);
        Assert.Equal(FlowStockApiFactory.ProductionManagerEmail, trace.CreatedBy.Email);
        Assert.NotNull(trace.StartedAt);
        Assert.NotNull(trace.CompletedAt);

        var flour = Assert.Single(trace.Materials);
        Assert.Equal(run.Flour.Sku, flour.ComponentSku);
        Assert.Equal(100m, flour.ConsumedQuantity);
        Assert.NotNull(flour.ConsumedAt);
        Assert.Equal(FlowStockApiFactory.ProductionManagerEmail, flour.ConsumedBy!.Email);

        // Where the flour on the line had come from: the transfer, and the warehouse manager.
        var source = Assert.Single(flour.Sources);
        Assert.Equal(run.Transfer.Number, source.MovementNumber);
        Assert.Equal(300m, source.Quantity);
        Assert.Equal("A-01", source.SourceLocationCode);
        Assert.Equal(FlowStockApiFactory.WarehouseManagerEmail, source.PerformedBy.Email);

        Assert.NotNull(trace.Output);
        Assert.Equal(1000m, trace.Output.Quantity);
        Assert.Equal("FG-01", trace.Output.LocationCode);
    }

    /// <summary>Forward traceability: "Where was this material used?"</summary>
    [Fact]
    public async Task A_material_names_the_runs_it_ended_up_in()
    {
        var run = await RunProductionAsync();

        var usage = await run.Client.GetFromJsonAsync<PagedResult<MaterialUsageEntry>>(
            $"/api/traceability/products/{run.Flour.Id}/usage", ApiJson.Options);

        var entry = Assert.Single(usage!.Items);
        Assert.Equal(run.Order.Number, entry.Number);
        Assert.Equal(ProductionOrderStatus.Completed, entry.Status);
        Assert.Equal(100m, entry.ConsumedQuantity);
        Assert.NotNull(entry.ConsumedAt);
        Assert.Equal(run.Cookie.Sku, entry.ProducedSku);
        Assert.Equal(1000m, entry.ProducedQuantity);
        Assert.Equal("FG-01", entry.OutputLocationCode);
    }

    /// <summary>The audit trail is open to everyone who is logged in, and to nobody who is not.</summary>
    [Fact]
    public async Task Traceability_is_readable_by_any_authenticated_user_and_by_no_one_else()
    {
        var run = await RunProductionAsync();

        var viewer = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.ViewerEmail, FlowStockApiFactory.ViewerPassword);

        var trace = await viewer.GetFromJsonAsync<ProductionTraceResponse>(
            $"/api/traceability/production-orders/{run.Order.Id}", ApiJson.Options);

        Assert.Equal(run.Order.Number, trace!.Number);

        var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/traceability/products/{run.Flour.Id}/history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tracing_a_product_that_does_not_exist_returns_the_domain_error()
    {
        var run = await RunProductionAsync();

        var response = await run.Client.GetAsync($"/api/traceability/products/{Guid.NewGuid()}/history");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PRODUCT_NOT_FOUND",
            (await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code);

        var order = await run.Client.GetAsync($"/api/traceability/production-orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, order.StatusCode);
        Assert.Equal("PRODUCTION_ORDER_NOT_FOUND",
            (await order.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code);
    }

    private record Run(
        HttpClient Client,
        ProductResponse Cookie,
        ProductResponse Flour,
        StorageLocationResponse Store,
        StorageLocationResponse Line,
        StorageLocationResponse Finished,
        StockMovementResponse Receipt,
        StockMovementResponse Transfer,
        ProductionOrderResponse Order);
}
