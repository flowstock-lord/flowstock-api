using FlowStock.Domain.Common;

namespace FlowStock.Domain.Catalog;

/// <summary>
/// How a product's quantity is measured (kg, liter, piece). See docs/PLAN.md, section 7.
/// A product is bound to exactly one unit so quantities are never mixed across incompatible units.
/// </summary>
public class UnitOfMeasure : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stored normalized (trimmed, lower-case) and unique, e.g. "kg".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name, e.g. "Kilogram".</summary>
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public ICollection<Product> Products { get; set; } = [];

    public static string NormalizeCode(string code) => code.Trim().ToLowerInvariant();
}
