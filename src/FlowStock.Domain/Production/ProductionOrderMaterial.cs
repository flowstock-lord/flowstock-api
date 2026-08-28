using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;

namespace FlowStock.Domain.Production;

/// <summary>
/// One material the run needs, scaled from the recipe when the order is created
/// (docs/PLAN.md, section 16).
///
/// It is a snapshot, not a view over the recipe: the order states what it undertook to consume
/// even though the recipe may be superseded later, and it is what the reservation and the
/// consumption movement are built from.
/// </summary>
public class ProductionOrderMaterial
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductionOrderId { get; set; }

    public ProductionOrder ProductionOrder { get; set; } = null!;

    public Guid ComponentProductId { get; set; }

    public Product ComponentProduct { get; set; } = null!;

    /// <summary>
    /// The lot the run will take, for a batch-tracked component; null for anything else. It is
    /// chosen when the order is written, not when it starts, so the reservation can hold the exact
    /// goods the run counted on (docs/PLAN.md, section 20).
    /// </summary>
    public Guid? BatchId { get; set; }

    public Batch? Batch { get; set; }

    /// <summary>Copied from the component product, exactly as on a movement line.</summary>
    public Guid UnitOfMeasureId { get; set; }

    public UnitOfMeasure UnitOfMeasure { get; set; } = null!;

    /// <summary>What the planned quantity needs, per the recipe version the order was built from.</summary>
    public decimal RequiredQuantity { get; set; }

    /// <summary>What the run actually took. Zero until the order starts.</summary>
    public decimal ConsumedQuantity { get; set; }
}
