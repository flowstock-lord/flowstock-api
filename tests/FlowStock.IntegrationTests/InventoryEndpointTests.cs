using System.Net;
using System.Net.Http.Json;
using FlowStock.Application.Catalog;
using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using FlowStock.Application.Warehouses;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Warehouses;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

[Collection(ApiCollection.Name)]
public class InventoryEndpointTests(FlowStockApiFactory factory)
{
    private Task<HttpClient> AdminClient() =>
        factory.AuthenticatedClientAsync(FlowStockApiFactory.AdminEmail, FlowStockApiFactory.AdminPassword);

    private Task<HttpClient> WarehouseClient() =>
        factory.AuthenticatedClientAsync(
            FlowStockApiFactory.WarehouseManagerEmail, FlowStockApiFactory.WarehouseManagerPassword);

    /// <summary>Codes and SKUs are globally unique, so every test builds its own.</summary>
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    /// <summary>One product and two locations to move it between.</summary>
    private async Task<Scenario> ArrangeAsync()
    {
        var admin = await AdminClient();

        var unit = await PostAsync<UnitOfMeasureResponse>(admin, "/api/units-of-measure",
            new CreateUnitOfMeasureRequest(Unique("u"), "Kilogram"));

        var product = await PostAsync<ProductResponse>(admin, "/api/products",
            new CreateProductRequest(Unique("SKU"), "Flour", null, ProductType.RawMaterial, unit.Id));

        var main = await PostAsync<WarehouseResponse>(admin, "/api/warehouses",
            new CreateWarehouseRequest(Unique("MAIN"), "Main Warehouse", null, WarehouseType.RawMaterials));

        var production = await PostAsync<WarehouseResponse>(admin, "/api/warehouses",
            new CreateWarehouseRequest(Unique("PROD"), "Production", null, WarehouseType.Production));

        var from = await PostAsync<StorageLocationResponse>(admin, "/api/storage-locations",
            new CreateStorageLocationRequest(main.Id, "A-01", "Rack A-01", null));

        var to = await PostAsync<StorageLocationResponse>(admin, "/api/storage-locations",
            new CreateStorageLocationRequest(production.Id, "LINE-01", "Line 1", null));

        return new Scenario(await WarehouseClient(), product, from, to);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, ApiJson.Options);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(ApiJson.Options))!;
    }

    private static Task<StockMovementResponse> CreateMovementAsync(
        HttpClient client,
        MovementType type,
        Guid? source,
        Guid? destination,
        Guid productId,
        decimal quantity,
        string? reason = null)
        => PostAsync<StockMovementResponse>(client, "/api/stock-movements",
            new CreateStockMovementRequest(type, source, destination, reason,
                [new CreateStockMovementLineRequest(productId, quantity)]));

    private static async Task<decimal> QuantityAsync(HttpClient client, Guid productId, Guid locationId)
    {
        var page = await client.GetFromJsonAsync<PagedResult<StockResponse>>(
            $"/api/stock/{productId}?locationId={locationId}", ApiJson.Options);

        return page!.Items.SingleOrDefault()?.Quantity ?? 0m;
    }

    /// <summary>
    /// The Phase 4 Definition of Done, end to end over HTTP: receive 500 kg of flour into the main
    /// warehouse, transfer 100 kg to production, and read 400 / 100 back — with the whole operation
    /// left in the movement history.
    /// </summary>
    [Fact]
    public async Task Receiving_and_transferring_stock_leaves_the_expected_balances_and_history()
    {
        var scenario = await ArrangeAsync();
        var client = scenario.Client;

        var receipt = await CreateMovementAsync(
            client, MovementType.Receipt, null, scenario.From.Id, scenario.Product.Id, 500m, "Supplier delivery");

        Assert.Equal(MovementStatus.Draft, receipt.Status);
        Assert.Equal(0m, await QuantityAsync(client, scenario.Product.Id, scenario.From.Id));

        var confirmedReceipt = await ConfirmAsync(client, receipt.Id);
        Assert.Equal(MovementStatus.Confirmed, confirmedReceipt.Status);
        Assert.NotNull(confirmedReceipt.ConfirmedAt);
        Assert.NotNull(confirmedReceipt.ConfirmedBy);
        Assert.Equal(500m, await QuantityAsync(client, scenario.Product.Id, scenario.From.Id));

        var transfer = await CreateMovementAsync(
            client, MovementType.Transfer, scenario.From.Id, scenario.To.Id, scenario.Product.Id, 100m);

        await ConfirmAsync(client, transfer.Id);

        Assert.Equal(400m, await QuantityAsync(client, scenario.Product.Id, scenario.From.Id));
        Assert.Equal(100m, await QuantityAsync(client, scenario.Product.Id, scenario.To.Id));

        // The complete history of the operation, from either end of the move.
        var history = await client.GetFromJsonAsync<PagedResult<StockMovementResponse>>(
            $"/api/stock-movements?productId={scenario.Product.Id}&sort=number", ApiJson.Options);

        Assert.Equal([MovementType.Receipt, MovementType.Transfer], history!.Items.Select(m => m.MovementType));
        Assert.All(history.Items, m => Assert.Equal(MovementStatus.Confirmed, m.Status));
        Assert.All(history.Items, m => Assert.Equal(scenario.Product.Id, m.Lines.Single().ProductId));

        var byLocation = await client.GetFromJsonAsync<PagedResult<StockMovementResponse>>(
            $"/api/stock-movements?locationId={scenario.To.Id}", ApiJson.Options);
        Assert.Equal(transfer.Id, Assert.Single(byLocation!.Items).Id);
    }

    /// <summary>
    /// docs/PLAN.md, section 28: several people move 80 out of a location holding 100 at the same
    /// instant. Exactly one must succeed — the balance may never go negative.
    /// </summary>
    [Fact]
    public async Task Simultaneous_transfers_cannot_both_take_the_same_stock()
    {
        const int contenders = 4;

        var scenario = await ArrangeAsync();
        var client = scenario.Client;

        var receipt = await CreateMovementAsync(
            client, MovementType.Receipt, null, scenario.From.Id, scenario.Product.Id, 100m);
        await ConfirmAsync(client, receipt.Id);

        var drafts = new List<StockMovementResponse>();
        var clients = new List<HttpClient>();

        for (var i = 0; i < contenders; i++)
        {
            drafts.Add(await CreateMovementAsync(
                client, MovementType.Transfer, scenario.From.Id, scenario.To.Id, scenario.Product.Id, 80m));
            clients.Add(await WarehouseClient());
        }

        // Each confirmation runs on its own connection, so they really do race.
        var responses = await Task.WhenAll(drafts.Select((draft, i) =>
            clients[i].PostAsync($"/api/stock-movements/{draft.Id}/confirm", null)));

        // Named so a failure says what actually came back, and what the API logged: a concurrency
        // test that fails with "expected 3, got 1" tells nobody why the losers did not get a 400.
        var outcome = string.Join(", ", await Task.WhenAll(responses.Select(async r =>
            $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}")))
            + Environment.NewLine + Environment.NewLine + "API errors:" + Environment.NewLine
            + factory.Errors.Report();

        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 1, outcome);
        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest) == contenders - 1, outcome);

        foreach (var rejected in responses.Where(r => r.StatusCode == HttpStatusCode.BadRequest))
        {
            var error = await rejected.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
            Assert.Equal("INSUFFICIENT_STOCK", error!.Code);
        }

        Assert.Equal(20m, await QuantityAsync(client, scenario.Product.Id, scenario.From.Id));
        Assert.Equal(80m, await QuantityAsync(client, scenario.Product.Id, scenario.To.Id));
    }

    [Fact]
    public async Task Taking_more_than_is_available_returns_the_domain_error_with_the_numbers()
    {
        var scenario = await ArrangeAsync();
        var client = scenario.Client;

        var receipt = await CreateMovementAsync(
            client, MovementType.Receipt, null, scenario.From.Id, scenario.Product.Id, 75m);
        await ConfirmAsync(client, receipt.Id);

        var transfer = await CreateMovementAsync(
            client, MovementType.Transfer, scenario.From.Id, scenario.To.Id, scenario.Product.Id, 100m);

        var response = await client.PostAsync($"/api/stock-movements/{transfer.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("INSUFFICIENT_STOCK", error!.Code);
        Assert.Equal(100m, Convert.ToDecimal(error.Details!["requested"]!.ToString()));
        Assert.Equal(75m, Convert.ToDecimal(error.Details["available"]!.ToString()));

        // Nothing moved, and the draft is still a draft.
        Assert.Equal(75m, await QuantityAsync(client, scenario.Product.Id, scenario.From.Id));
        Assert.Equal(MovementStatus.Draft, (await GetAsync(client, transfer.Id)).Status);
    }

    [Fact]
    public async Task A_confirmed_movement_can_never_be_confirmed_again_or_cancelled()
    {
        var scenario = await ArrangeAsync();
        var client = scenario.Client;

        var receipt = await CreateMovementAsync(
            client, MovementType.Receipt, null, scenario.From.Id, scenario.Product.Id, 10m);
        await ConfirmAsync(client, receipt.Id);

        foreach (var url in new[] { "confirm", "cancel" })
        {
            var response = await client.PostAsJsonAsync(
                $"/api/stock-movements/{receipt.Id}/{url}", new CancelStockMovementRequest(null), ApiJson.Options);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
            Assert.Equal("MOVEMENT_ALREADY_CONFIRMED", error!.Code);
        }

        // The single confirmation stands: a rejected retry must not double the stock.
        Assert.Equal(10m, await QuantityAsync(client, scenario.Product.Id, scenario.From.Id));
    }

    [Fact]
    public async Task A_cancelled_draft_never_reaches_stock()
    {
        var scenario = await ArrangeAsync();
        var client = scenario.Client;

        var receipt = await CreateMovementAsync(
            client, MovementType.Receipt, null, scenario.From.Id, scenario.Product.Id, 42m);

        var response = await client.PostAsJsonAsync(
            $"/api/stock-movements/{receipt.Id}/cancel",
            new CancelStockMovementRequest("Delivery never arrived"),
            ApiJson.Options);
        response.EnsureSuccessStatusCode();

        var cancelled = (await response.Content.ReadFromJsonAsync<StockMovementResponse>(ApiJson.Options))!;
        Assert.Equal(MovementStatus.Cancelled, cancelled.Status);
        Assert.Equal(0m, await QuantityAsync(client, scenario.Product.Id, scenario.From.Id));

        var confirm = await client.PostAsync($"/api/stock-movements/{receipt.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);

        var error = await confirm.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("MOVEMENT_ALREADY_CANCELLED", error!.Code);
    }

    [Fact]
    public async Task An_invalid_movement_is_rejected_before_it_is_ever_stored()
    {
        var scenario = await ArrangeAsync();
        var client = scenario.Client;

        // A receipt has no source.
        var withSource = await client.PostAsJsonAsync("/api/stock-movements",
            new CreateStockMovementRequest(MovementType.Receipt, scenario.From.Id, scenario.To.Id, null,
                [new CreateStockMovementLineRequest(scenario.Product.Id, 1m)]),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, withSource.StatusCode);
        Assert.Equal("INVALID_MOVEMENT",
            (await withSource.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code);

        // Quantities are positive, and validation catches it before any domain rule runs.
        var zeroQuantity = await client.PostAsJsonAsync("/api/stock-movements",
            new CreateStockMovementRequest(MovementType.Receipt, null, scenario.To.Id, null,
                [new CreateStockMovementLineRequest(scenario.Product.Id, 0m)]),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, zeroQuantity.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed,
            (await zeroQuantity.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code);

        // Consumption and production output are posted by production orders, not by hand.
        var consumption = await client.PostAsJsonAsync("/api/stock-movements",
            new CreateStockMovementRequest(MovementType.Consumption, scenario.From.Id, null, null,
                [new CreateStockMovementLineRequest(scenario.Product.Id, 1m)]),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, consumption.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed,
            (await consumption.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code);
    }

    [Fact]
    public async Task An_unknown_sort_field_is_rejected_by_validation()
    {
        var client = await WarehouseClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stock?sort=colour")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/stock-movements?sort=colour")).StatusCode);
    }

    [Fact]
    public async Task Viewer_can_read_stock_and_history_but_cannot_move_anything()
    {
        var scenario = await ArrangeAsync();

        var draft = await CreateMovementAsync(
            scenario.Client, MovementType.Receipt, null, scenario.From.Id, scenario.Product.Id, 5m);

        var viewer = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.ViewerEmail, FlowStockApiFactory.ViewerPassword);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await viewer.GetAsync($"/api/stock/by-location/{scenario.From.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/stock-movements")).StatusCode);

        var create = await viewer.PostAsJsonAsync("/api/stock-movements",
            new CreateStockMovementRequest(MovementType.Receipt, null, scenario.From.Id, null,
                [new CreateStockMovementLineRequest(scenario.Product.Id, 1m)]),
            ApiJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsync($"/api/stock-movements/{draft.Id}/confirm", null)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_to_inventory_return_401()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/stock-movements")).StatusCode);
    }

    private static async Task<StockMovementResponse> ConfirmAsync(HttpClient client, Guid id)
    {
        var response = await client.PostAsync($"/api/stock-movements/{id}/confirm", null);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<StockMovementResponse>(ApiJson.Options))!;
    }

    private static async Task<StockMovementResponse> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<StockMovementResponse>($"/api/stock-movements/{id}", ApiJson.Options))!;

    private record Scenario(
        HttpClient Client,
        ProductResponse Product,
        StorageLocationResponse From,
        StorageLocationResponse To);
}
