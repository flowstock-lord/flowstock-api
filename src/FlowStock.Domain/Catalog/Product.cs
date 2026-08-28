using FlowStock.Domain.Common;

namespace FlowStock.Domain.Catalog;

/// <summary>
/// Anything that can exist in inventory: raw material, packaging, semi-finished or finished goods.
/// See docs/PLAN.md, section 6.
/// </summary>
public class Product : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stored normalized (trimmed, upper-case) and unique across products.</summary>
    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ProductType ProductType { get; set; }

    public Guid UnitOfMeasureId { get; set; }

    public UnitOfMeasure UnitOfMeasure { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();
}
