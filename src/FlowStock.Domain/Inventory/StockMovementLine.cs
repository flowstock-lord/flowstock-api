using FlowStock.Domain.Catalog;

namespace FlowStock.Domain.Inventory;

/// <summary>
/// One product and quantity inside a movement document (docs/PLAN.md, section 12). Quantities are
/// always positive — the direction comes from the document's endpoints, never from the sign.
/// </summary>
public class StockMovementLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StockMovementId { get; set; }

    public StockMovement StockMovement { get; set; } = null!;

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>
    /// Copied from the product so the line stays self-describing: history says which unit the
    /// quantity was recorded in, and quantities are never mixed across units.
    /// </summary>
    public Guid UnitOfMeasureId { get; set; }

    public UnitOfMeasure UnitOfMeasure { get; set; } = null!;

    public decimal Quantity { get; set; }
}
