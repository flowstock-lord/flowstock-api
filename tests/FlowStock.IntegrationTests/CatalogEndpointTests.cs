using System.Net;
using System.Net.Http.Json;
using System.Text;
using FlowStock.Application.Catalog;
using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CatalogEndpointTests(FlowStockApiFactory factory)
{
    private Task<HttpClient> AdminClient() =>
        factory.AuthenticatedClientAsync(FlowStockApiFactory.AdminEmail, FlowStockApiFactory.AdminPassword);

    private Task<HttpClient> ViewerClient() =>
        factory.AuthenticatedClientAsync(FlowStockApiFactory.ViewerEmail, FlowStockApiFactory.ViewerPassword);

    private static async Task<UnitOfMeasureResponse> CreateUnitAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync(
            "/api/units-of-measure",
            new CreateUnitOfMeasureRequest(code, code.ToUpperInvariant()),
            ApiJson.Options);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UnitOfMeasureResponse>(ApiJson.Options))!;
    }

    /// <summary>The Phase 2 Definition of Done: Flour / FLOUR-001 / kg / RawMaterial.</summary>
    [Fact]
    public async Task Admin_can_create_flour_measured_in_kilograms()
    {
        var client = await AdminClient();
        var kilogram = await CreateUnitAsync(client, $"kg-{Guid.NewGuid():N}"[..12]);

        var response = await client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            $"FLOUR-{Guid.NewGuid():N}"[..16], "Flour", "Wheat flour", ProductType.RawMaterial, kilogram.Id),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var product = await response.Content.ReadFromJsonAsync<ProductResponse>(ApiJson.Options);
        Assert.Equal(ProductType.RawMaterial, product!.ProductType);
        Assert.Equal(kilogram.Code, product.UnitOfMeasureCode);
        Assert.True(product.IsActive);

        var fetched = await client.GetFromJsonAsync<ProductResponse>(
            $"/api/products/{product.Id}", ApiJson.Options);
        Assert.Equal(product.Sku, fetched!.Sku);
    }

    [Fact]
    public async Task A_duplicate_sku_returns_the_domain_error()
    {
        var client = await AdminClient();
        var unit = await CreateUnitAsync(client, $"u{Guid.NewGuid():N}"[..10]);
        var sku = $"DUP-{Guid.NewGuid():N}"[..14];

        var request = new CreateProductRequest(sku, "First", null, ProductType.Other, unit.Id);
        (await client.PostAsJsonAsync("/api/products", request, ApiJson.Options)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/products",
            request with { Sku = sku.ToLowerInvariant(), Name = "Second" },
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("SKU_ALREADY_EXISTS", error!.Code);
    }

    [Fact]
    public async Task An_unknown_unit_of_measure_returns_the_domain_error()
    {
        var client = await AdminClient();

        var response = await client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            $"GHOST-{Guid.NewGuid():N}"[..16], "Ghost", null, ProductType.Other, Guid.NewGuid()),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("UNIT_OF_MEASURE_NOT_FOUND", error!.Code);
    }

    [Fact]
    public async Task An_invalid_sku_is_rejected_by_validation()
    {
        var client = await AdminClient();
        var unit = await CreateUnitAsync(client, $"v{Guid.NewGuid():N}"[..10]);

        var response = await client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            "not a sku!", "Bad", null, ProductType.Other, unit.Id), ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal(ErrorCodes.ValidationFailed, error!.Code);
        Assert.True(error.Details!.ContainsKey("Sku"));
    }

    [Fact]
    public async Task A_malformed_body_still_uses_the_error_envelope()
    {
        var client = await AdminClient();

        var response = await client.PostAsync(
            "/api/products",
            new StringContent("{\"unitOfMeasureId\":\"not-a-guid\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal(ErrorCodes.ValidationFailed, error!.Code);
        Assert.DoesNotContain("FlowStock.Application", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_sort_field_is_rejected_by_validation()
    {
        var client = await AdminClient();

        var response = await client.GetAsync("/api/products?sort=colour");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal(ErrorCodes.ValidationFailed, error!.Code);
    }

    [Fact]
    public async Task Viewer_can_read_the_catalogue_but_not_change_it()
    {
        var admin = await AdminClient();
        var unit = await CreateUnitAsync(admin, $"w{Guid.NewGuid():N}"[..10]);

        var viewer = await ViewerClient();

        var list = await viewer.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var units = await viewer.GetAsync("/api/units-of-measure");
        Assert.Equal(HttpStatusCode.OK, units.StatusCode);

        var create = await viewer.PostAsJsonAsync("/api/products", new CreateProductRequest(
            $"DENY-{Guid.NewGuid():N}"[..15], "Denied", null, ProductType.Other, unit.Id), ApiJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_to_the_catalogue_return_401()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/units-of-measure")).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_product_stays_readable_and_is_filtered_out()
    {
        var client = await AdminClient();
        var unit = await CreateUnitAsync(client, $"x{Guid.NewGuid():N}"[..10]);
        var sku = $"OLD-{Guid.NewGuid():N}"[..14];

        var created = await (await client.PostAsJsonAsync("/api/products", new CreateProductRequest(
                sku, "Retired", null, ProductType.Other, unit.Id), ApiJson.Options))
            .Content.ReadFromJsonAsync<ProductResponse>(ApiJson.Options);

        var deactivate = await client.PostAsync($"/api/products/{created!.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var active = await client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            $"/api/products?isActive=true&search={sku}", ApiJson.Options);
        Assert.Empty(active!.Items);

        var all = await client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            $"/api/products?search={sku}", ApiJson.Options);
        Assert.False(Assert.Single(all!.Items).IsActive);
    }

    [Fact]
    public async Task A_deactivated_unit_cannot_be_attached_to_a_new_product()
    {
        var client = await AdminClient();
        var unit = await CreateUnitAsync(client, $"y{Guid.NewGuid():N}"[..10]);

        (await client.PostAsync($"/api/units-of-measure/{unit.Id}/deactivate", null)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            $"NOPE-{Guid.NewGuid():N}"[..15], "Nope", null, ProductType.Other, unit.Id), ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options);
        Assert.Equal("UNIT_OF_MEASURE_INACTIVE", error!.Code);
    }
}
