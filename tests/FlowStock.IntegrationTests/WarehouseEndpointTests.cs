using System.Net;
using System.Net.Http.Json;
using FlowStock.Application.Common;
using FlowStock.Application.Warehouses;
using FlowStock.Domain.Warehouses;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

[Collection(ApiCollection.Name)]
public class WarehouseEndpointTests(FlowStockApiFactory factory)
{
    private Task<HttpClient> AdminClient() =>
        factory.AuthenticatedClientAsync(FlowStockApiFactory.AdminEmail, FlowStockApiFactory.AdminPassword);

    /// <summary>Codes are globally unique, so every test builds its own.</summary>
    private static string Code(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    private static async Task<WarehouseResponse> CreateWarehouseAsync(
        HttpClient client,
        string code,
        WarehouseType type = WarehouseType.General)
    {
        var response = await client.PostAsJsonAsync(
            "/api/warehouses",
            new CreateWarehouseRequest(code, code, null, type),
            ApiJson.Options);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<WarehouseResponse>(ApiJson.Options))!;
    }

    private static async Task<HttpResponseMessage> CreateLocationAsync(
        HttpClient client,
        Guid warehouseId,
        string code)
        => await client.PostAsJsonAsync(
            "/api/storage-locations",
            new CreateStorageLocationRequest(warehouseId, code, code, null),
            ApiJson.Options);

    /// <summary>
    /// The Phase 3 Definition of Done: Main Warehouse (A-01, A-02), Production (LINE-01, LINE-02),
    /// Finished Goods (FG-01).
    /// </summary>
    [Fact]
    public async Task Admin_can_build_the_warehouse_tree()
    {
        var client = await AdminClient();

        var main = await CreateWarehouseAsync(client, Code("MAIN"), WarehouseType.RawMaterials);
        var production = await CreateWarehouseAsync(client, Code("PROD"), WarehouseType.Production);
        var finished = await CreateWarehouseAsync(client, Code("FG"), WarehouseType.FinishedGoods);

        var tree = new Dictionary<WarehouseResponse, string[]>
        {
            [main] = ["A-01", "A-02"],
            [production] = ["LINE-01", "LINE-02"],
            [finished] = ["FG-01"]
        };

        foreach (var (warehouse, codes) in tree)
        {
            foreach (var code in codes)
            {
                var created = await CreateLocationAsync(client, warehouse.Id, code);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            }

            var locations = await client.GetFromJsonAsync<PagedResult<StorageLocationResponse>>(
                $"/api/storage-locations?warehouseId={warehouse.Id}", ApiJson.Options);

            Assert.Equal(codes, locations!.Items.Select(l => l.Code));
            Assert.All(locations.Items, l => Assert.Equal(warehouse.Code, l.WarehouseCode));

            var refreshed = await client.GetFromJsonAsync<WarehouseResponse>(
                $"/api/warehouses/{warehouse.Id}", ApiJson.Options);
            Assert.Equal(codes.Length, refreshed!.LocationCount);
        }
    }

    [Fact]
    public async Task A_duplicate_warehouse_code_returns_the_domain_error()
    {
        var client = await AdminClient();
        var code = Code("DUP");

        await CreateWarehouseAsync(client, code);

        var response = await client.PostAsJsonAsync("/api/warehouses",
            new CreateWarehouseRequest(code.ToLowerInvariant(), "Second", null, WarehouseType.General),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("WAREHOUSE_CODE_EXISTS", error!.Code);
    }

    [Fact]
    public async Task The_same_location_code_is_free_in_another_warehouse_but_not_in_the_same_one()
    {
        var client = await AdminClient();
        var first = await CreateWarehouseAsync(client, Code("W1"));
        var second = await CreateWarehouseAsync(client, Code("W2"));

        Assert.Equal(HttpStatusCode.Created, (await CreateLocationAsync(client, first.Id, "A-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateLocationAsync(client, second.Id, "A-01")).StatusCode);

        var duplicate = await CreateLocationAsync(client, first.Id, "a-01");
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var error = await duplicate.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("LOCATION_CODE_EXISTS", error!.Code);
    }

    [Fact]
    public async Task An_unknown_warehouse_returns_the_domain_error()
    {
        var client = await AdminClient();

        var response = await CreateLocationAsync(client, Guid.NewGuid(), "A-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("WAREHOUSE_NOT_FOUND", error!.Code);
    }

    [Fact]
    public async Task A_deactivated_warehouse_accepts_no_new_locations()
    {
        var client = await AdminClient();
        var warehouse = await CreateWarehouseAsync(client, Code("CLOSED"));

        (await client.PostAsync($"/api/warehouses/{warehouse.Id}/deactivate", null)).EnsureSuccessStatusCode();

        var response = await CreateLocationAsync(client, warehouse.Id, "A-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("WAREHOUSE_INACTIVE", error!.Code);
    }

    [Fact]
    public async Task An_invalid_code_is_rejected_by_validation()
    {
        var client = await AdminClient();

        var response = await client.PostAsJsonAsync("/api/warehouses",
            new CreateWarehouseRequest("not a code!", "Bad", null, WarehouseType.General),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal(ErrorCodes.ValidationFailed, error!.Code);
        Assert.True(error.Details!.ContainsKey("Code"));
    }

    [Fact]
    public async Task An_unknown_sort_field_is_rejected_by_validation()
    {
        var client = await AdminClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/warehouses?sort=size")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/storage-locations?sort=size")).StatusCode);
    }

    [Fact]
    public async Task Viewer_can_read_warehouses_but_not_change_them()
    {
        var admin = await AdminClient();
        var warehouse = await CreateWarehouseAsync(admin, Code("READ"));

        var viewer = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.ViewerEmail, FlowStockApiFactory.ViewerPassword);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/warehouses")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/storage-locations")).StatusCode);

        var create = await viewer.PostAsJsonAsync("/api/warehouses",
            new CreateWarehouseRequest(Code("DENY"), "Denied", null, WarehouseType.General), ApiJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await CreateLocationAsync(viewer, warehouse.Id, "A-01")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_to_warehouses_return_401()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/warehouses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/storage-locations")).StatusCode);
    }
}
