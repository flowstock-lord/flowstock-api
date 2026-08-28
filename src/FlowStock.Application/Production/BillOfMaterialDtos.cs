using FlowStock.Application.Common;

namespace FlowStock.Application.Production;

public record BillOfMaterialItemResponse(
    Guid Id,
    Guid ComponentProductId,
    string ComponentSku,
    string ComponentName,
    decimal Quantity,
    Guid UnitOfMeasureId,
    string UnitOfMeasureCode);

public record BillOfMaterialResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    int Version,
    decimal OutputQuantity,
    string OutputUnitOfMeasureCode,
    string? Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<BillOfMaterialItemResponse> Items,
    DateTime CreatedAt,
    Guid? CreatedBy,
    DateTime? UpdatedAt);

/// <summary>The unit is taken from the component product, so a recipe cannot mix units by mistake.</summary>
public record CreateBillOfMaterialItemRequest(Guid ComponentProductId, decimal Quantity);

/// <summary>
/// Creates the next version of a product's recipe. The version number is assigned by the system,
/// and the new version becomes the active one.
/// </summary>
public record CreateBillOfMaterialRequest(
    Guid ProductId,
    decimal OutputQuantity,
    string? Name,
    string? Description,
    IReadOnlyList<CreateBillOfMaterialItemRequest> Items);

/// <summary>
/// Only the labelling can change. The components, the output quantity and the version are what a
/// production order was built from, so they are fixed — a different recipe is a new version.
/// </summary>
public record UpdateBillOfMaterialRequest(string? Name, string? Description);

/// <summary>Filters and sorting for GET /api/boms.</summary>
public class BillOfMaterialQuery : PagedQuery
{
    /// <summary>Case-insensitive match against the produced product's SKU or name.</summary>
    public string? Search { get; set; }

    public Guid? ProductId { get; set; }

    public bool? IsActive { get; set; }

    /// <summary>sku | version | createdAt, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}

/// <summary>How much to produce, for GET /api/boms/{id}/requirements.</summary>
public class MaterialRequirementsQuery
{
    public decimal Quantity { get; set; }
}

/// <summary>One component of what a production run of a given size would need.</summary>
public record MaterialRequirementResponse(
    Guid ComponentProductId,
    string ComponentSku,
    string ComponentName,
    decimal QuantityPerRun,
    decimal RequiredQuantity,
    string UnitOfMeasureCode);

/// <summary>
/// What producing <paramref name="Quantity"/> of the product would consume, according to one
/// specific version of its recipe.
/// </summary>
public record MaterialRequirementsResponse(
    Guid BillOfMaterialId,
    int Version,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    string OutputUnitOfMeasureCode,
    decimal OutputQuantityPerRun,
    IReadOnlyList<MaterialRequirementResponse> Requirements);
