using FlowStock.Domain.Catalog;
using FlowStock.Domain.Common;
using FlowStock.Domain.Warehouses;

namespace FlowStock.Domain.Inventory;

/// <summary>
/// The current balance of one product in one storage location (docs/PLAN.md, section 10).
///
/// This is a derived value, never a source of truth: it exists only because confirmed stock
/// movements were applied to it. Nothing outside the inventory application logic may change
/// <see cref="Quantity"/> — see CLAUDE.md, rule 1.
/// </summary>
public class Stock : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public Guid LocationId { get; set; }

    public StorageLocation Location { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>Reserved by an operation that has not consumed it yet. Reservations arrive in Phase 6.</summary>
    public decimal ReservedQuantity { get; set; }

    /// <summary>What may still be taken from this location. Must never go below zero.</summary>
    public decimal AvailableQuantity => Quantity - ReservedQuantity;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Identifies one stock balance. Used to load the exact rows an operation will touch.</summary>
public readonly record struct StockKey(Guid ProductId, Guid LocationId);
