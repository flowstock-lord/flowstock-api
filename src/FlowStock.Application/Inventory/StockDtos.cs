using FlowStock.Application.Common;

namespace FlowStock.Application.Inventory;

/// <summary>
/// One balance: how much of a product sits in one storage location. Read-only — stock changes
/// only through confirmed movements (CLAUDE.md, rule 1).
/// </summary>
public record StockResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasureCode,
    // The lot this balance holds, for a batch-tracked product (docs/PLAN.md, section 20).
    Guid? BatchId,
    string? BatchNumber,
    DateOnly? BatchExpiryDate,
    Guid LocationId,
    string LocationCode,
    Guid WarehouseId,
    string WarehouseCode,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    DateTime? UpdatedAt);

/// <summary>Filters and sorting for GET /api/stock.</summary>
public class StockQuery : PagedQuery
{
    /// <summary>Case-insensitive match against the product SKU or name.</summary>
    public string? Search { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? LocationId { get; set; }

    /// <summary>The balance of one lot, wherever it sits.</summary>
    public Guid? BatchId { get; set; }

    /// <summary>Only lots expiring before this day — what the shelf should be emptied of first.</summary>
    public DateOnly? ExpiringBefore { get; set; }

    /// <summary>Every location of one warehouse.</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>Hides balances that have fallen to zero.</summary>
    public bool? OnlyInStock { get; set; }

    /// <summary>sku | location | quantity, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}
