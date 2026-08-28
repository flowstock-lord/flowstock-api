using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;

namespace FlowStock.Application.Catalog;

public record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    ProductType ProductType,
    Guid UnitOfMeasureId,
    string UnitOfMeasureCode,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateProductRequest(
    string Sku,
    string Name,
    string? Description,
    ProductType ProductType,
    Guid UnitOfMeasureId);

/// <summary>
/// The SKU is immutable — it is the stable business identifier that inventory history refers to.
/// </summary>
public record UpdateProductRequest(
    string Name,
    string? Description,
    ProductType ProductType,
    Guid UnitOfMeasureId);

/// <summary>Filters and sorting for GET /api/products.</summary>
public class ProductQuery : PagedQuery
{
    /// <summary>Case-insensitive match against SKU or name.</summary>
    public string? Search { get; set; }

    public ProductType? ProductType { get; set; }

    public Guid? UnitOfMeasureId { get; set; }

    public bool? IsActive { get; set; }

    /// <summary>sku | name | type | createdAt, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}
