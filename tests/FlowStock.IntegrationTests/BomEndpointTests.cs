using System.Net;
using System.Net.Http.Json;
using FlowStock.Application.Catalog;
using FlowStock.Application.Common;
using FlowStock.Application.Production;
using FlowStock.Domain.Catalog;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

[Collection(ApiCollection.Name)]
public class BomEndpointTests(FlowStockApiFactory factory)
{
    private Task<HttpClient> AdminClient() =>
        factory.AuthenticatedClientAsync(FlowStockApiFactory.AdminEmail, FlowStockApiFactory.AdminPassword);

    private Task<HttpClient> ProductionClient() =>
        factory.AuthenticatedClientAsync(
            FlowStockApiFactory.ProductionManagerEmail, FlowStockApiFactory.ProductionManagerPassword);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, ApiJson.Options);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(ApiJson.Options))!;
    }

    /// <summary>Cookie, flour, sugar and butter — the cast of docs/PLAN.md, section 14.</summary>
    private async Task<Recipe> ArrangeAsync()
    {
        var admin = await AdminClient();

        var kilogram = await PostAsync<UnitOfMeasureResponse>(admin, "/api/units-of-measure",
            new CreateUnitOfMeasureRequest(Unique("kg"), "Kilogram"));
        var piece = await PostAsync<UnitOfMeasureResponse>(admin, "/api/units-of-measure",
            new CreateUnitOfMeasureRequest(Unique("pc"), "Piece"));

        async Task<ProductResponse> ProductAsync(string sku, string name, ProductType type, Guid unitId) =>
            await PostAsync<ProductResponse>(admin, "/api/products",
                new CreateProductRequest(Unique(sku), name, null, type, unitId));

        return new Recipe(
            await ProductionClient(),
            await ProductAsync("COOKIE", "Cookie", ProductType.FinishedProduct, piece.Id),
            await ProductAsync("FLOUR", "Flour", ProductType.RawMaterial, kilogram.Id),
            await ProductAsync("SUGAR", "Sugar", ProductType.RawMaterial, kilogram.Id),
            await ProductAsync("BUTTER", "Butter", ProductType.RawMaterial, kilogram.Id));
    }

    private static Task<BillOfMaterialResponse> PublishAsync(
        Recipe recipe,
        decimal outputQuantity,
        params (ProductResponse Component, decimal Quantity)[] items)
        => PostAsync<BillOfMaterialResponse>(recipe.Client, "/api/boms",
            new CreateBillOfMaterialRequest(recipe.Cookie.Id, outputQuantity, "Cookie", null,
                items.Select(i => new CreateBillOfMaterialItemRequest(i.Component.Id, i.Quantity)).ToList()));

    /// <summary>
    /// The Phase 5 Definition of Done over HTTP: Cookie × 100 from flour 10, sugar 4 and butter 2,
    /// and the API calculates what any run size requires.
    /// </summary>
    [Fact]
    public async Task A_published_recipe_can_calculate_the_materials_a_run_requires()
    {
        var recipe = await ArrangeAsync();

        var bom = await PublishAsync(recipe, 100m,
            (recipe.Flour, 10m), (recipe.Sugar, 4m), (recipe.Butter, 2m));

        Assert.Equal(1, bom.Version);
        Assert.True(bom.IsActive);

        var forOneRun = await RequirementsAsync(recipe.Client, bom.Id, 100m);
        Assert.Equal(
            [(recipe.Butter.Sku, 2m), (recipe.Flour.Sku, 10m), (recipe.Sugar.Sku, 4m)],
            forOneRun.Requirements
                .OrderBy(r => r.ComponentSku)
                .Select(r => (r.ComponentSku, r.RequiredQuantity)));

        var forTenRuns = await RequirementsAsync(recipe.Client, bom.Id, 1000m);
        Assert.Equal(1000m, forTenRuns.Quantity);
        Assert.Equal(
            [(recipe.Butter.Sku, 20m), (recipe.Flour.Sku, 100m), (recipe.Sugar.Sku, 40m)],
            forTenRuns.Requirements
                .OrderBy(r => r.ComponentSku)
                .Select(r => (r.ComponentSku, r.RequiredQuantity)));
    }

    /// <summary>
    /// The database, not only the service, holds the rule that a product has one recipe in force:
    /// the filtered unique index must survive publishing a second version.
    /// </summary>
    [Fact]
    public async Task Publishing_a_second_version_leaves_exactly_one_active_recipe()
    {
        var recipe = await ArrangeAsync();

        var first = await PublishAsync(recipe, 100m, (recipe.Flour, 10m), (recipe.Sugar, 4m));
        var second = await PublishAsync(recipe, 100m, (recipe.Flour, 10m), (recipe.Sugar, 3m));

        Assert.Equal(2, second.Version);

        var versions = await recipe.Client.GetFromJsonAsync<PagedResult<BillOfMaterialResponse>>(
            $"/api/boms?productId={recipe.Cookie.Id}", ApiJson.Options);

        Assert.Equal([2, 1], versions!.Items.Select(b => b.Version));
        Assert.Equal(second.Id, Assert.Single(versions.Items, b => b.IsActive).Id);

        // Putting the first version back in force must also leave exactly one active.
        var restored = await recipe.Client.PostAsync($"/api/boms/{first.Id}/activate", null);
        restored.EnsureSuccessStatusCode();

        versions = await recipe.Client.GetFromJsonAsync<PagedResult<BillOfMaterialResponse>>(
            $"/api/boms?productId={recipe.Cookie.Id}", ApiJson.Options);
        Assert.Equal(first.Id, Assert.Single(versions!.Items, b => b.IsActive).Id);
    }

    [Fact]
    public async Task A_superseded_version_still_reports_the_recipe_it_held()
    {
        var recipe = await ArrangeAsync();

        var first = await PublishAsync(recipe, 100m, (recipe.Flour, 10m), (recipe.Sugar, 4m));
        await PublishAsync(recipe, 100m, (recipe.Flour, 99m));

        var requirements = await RequirementsAsync(recipe.Client, first.Id, 100m);

        Assert.Equal(1, requirements.Version);
        Assert.Equal(10m, requirements.Requirements.Single(r => r.ComponentSku == recipe.Flour.Sku).RequiredQuantity);
    }

    [Fact]
    public async Task Update_cannot_reach_the_components()
    {
        var recipe = await ArrangeAsync();
        var bom = await PublishAsync(recipe, 100m, (recipe.Flour, 10m));

        var response = await recipe.Client.PutAsJsonAsync($"/api/boms/{bom.Id}",
            new UpdateBillOfMaterialRequest("Renamed", "Still the same recipe"), ApiJson.Options);
        response.EnsureSuccessStatusCode();

        var updated = (await response.Content.ReadFromJsonAsync<BillOfMaterialResponse>(ApiJson.Options))!;

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(10m, Assert.Single(updated.Items).Quantity);
        Assert.Equal(1, updated.Version);
    }

    [Fact]
    public async Task An_invalid_recipe_is_rejected()
    {
        var recipe = await ArrangeAsync();

        // A component twice over.
        var duplicate = await recipe.Client.PostAsJsonAsync("/api/boms",
            new CreateBillOfMaterialRequest(recipe.Cookie.Id, 100m, null, null,
            [
                new CreateBillOfMaterialItemRequest(recipe.Flour.Id, 10m),
                new CreateBillOfMaterialItemRequest(recipe.Flour.Id, 1m)
            ]),
            ApiJson.Options);
        Assert.Equal("BOM_INVALID", await CodeAsync(duplicate));

        // A product made of itself.
        var selfReference = await recipe.Client.PostAsJsonAsync("/api/boms",
            new CreateBillOfMaterialRequest(recipe.Cookie.Id, 100m, null, null,
                [new CreateBillOfMaterialItemRequest(recipe.Cookie.Id, 1m)]),
            ApiJson.Options);
        Assert.Equal("BOM_INVALID", await CodeAsync(selfReference));

        // No components at all, and a run that produces nothing.
        var empty = await recipe.Client.PostAsJsonAsync("/api/boms",
            new CreateBillOfMaterialRequest(recipe.Cookie.Id, 100m, null, null, []), ApiJson.Options);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeAsync(empty));

        var noOutput = await recipe.Client.PostAsJsonAsync("/api/boms",
            new CreateBillOfMaterialRequest(recipe.Cookie.Id, 0m, null, null,
                [new CreateBillOfMaterialItemRequest(recipe.Flour.Id, 1m)]),
            ApiJson.Options);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeAsync(noOutput));

        // An unknown recipe.
        var unknown = await recipe.Client.GetAsync($"/api/boms/{Guid.NewGuid()}");
        Assert.Equal("BOM_NOT_FOUND", await CodeAsync(unknown));
    }

    [Fact]
    public async Task Requirements_reject_a_quantity_that_is_not_positive()
    {
        var recipe = await ArrangeAsync();
        var bom = await PublishAsync(recipe, 100m, (recipe.Flour, 10m));

        foreach (var quantity in new[] { "0", "-5" })
        {
            var response = await recipe.Client.GetAsync($"/api/boms/{bom.Id}/requirements?quantity={quantity}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ErrorCodes.ValidationFailed, await CodeAsync(response));
        }
    }

    [Fact]
    public async Task An_unknown_sort_field_is_rejected_by_validation()
    {
        var client = await ProductionClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/boms?sort=flavour")).StatusCode);
    }

    [Fact]
    public async Task The_warehouse_manager_may_read_recipes_but_not_publish_them()
    {
        var recipe = await ArrangeAsync();
        var bom = await PublishAsync(recipe, 100m, (recipe.Flour, 10m));

        var warehouse = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.WarehouseManagerEmail, FlowStockApiFactory.WarehouseManagerPassword);

        Assert.Equal(HttpStatusCode.OK, (await warehouse.GetAsync("/api/boms")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await warehouse.GetAsync($"/api/boms/{bom.Id}/requirements?quantity=100")).StatusCode);

        var create = await warehouse.PostAsJsonAsync("/api/boms",
            new CreateBillOfMaterialRequest(recipe.Cookie.Id, 100m, null, null,
                [new CreateBillOfMaterialItemRequest(recipe.Flour.Id, 1m)]),
            ApiJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await warehouse.PostAsync($"/api/boms/{bom.Id}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_to_boms_return_401()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/boms")).StatusCode);
    }

    private static async Task<MaterialRequirementsResponse> RequirementsAsync(
        HttpClient client,
        Guid bomId,
        decimal quantity)
        => (await client.GetFromJsonAsync<MaterialRequirementsResponse>(
            $"/api/boms/{bomId}/requirements?quantity={quantity}", ApiJson.Options))!;

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options))!.Code;
    }

    private record Recipe(
        HttpClient Client,
        ProductResponse Cookie,
        ProductResponse Flour,
        ProductResponse Sugar,
        ProductResponse Butter);
}
