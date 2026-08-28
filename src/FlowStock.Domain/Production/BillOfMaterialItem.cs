using FlowStock.Domain.Catalog;

namespace FlowStock.Domain.Production;

/// <summary>
/// One component of a recipe and how much of it a single run consumes
/// (docs/PLAN.md, section 14). Quantities are always positive.
/// </summary>
public class BillOfMaterialItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BillOfMaterialId { get; set; }

    public BillOfMaterial BillOfMaterial { get; set; } = null!;

    public Guid ComponentProductId { get; set; }

    public Product ComponentProduct { get; set; } = null!;

    /// <summary>
    /// Copied from the component so the recipe stays self-describing and quantities are never
    /// mixed across units, exactly as on a stock movement line.
    /// </summary>
    public Guid UnitOfMeasureId { get; set; }

    public UnitOfMeasure UnitOfMeasure { get; set; } = null!;

    /// <summary>How much of this component one full run of the recipe consumes.</summary>
    public decimal Quantity { get; set; }
}
