using FlowStock.Application.Common;
using FlowStock.Domain.Inventory;

namespace FlowStock.Application.Inventory;

public record StockMovementLineResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    // The lot that moved, for a batch-tracked product (docs/PLAN.md, section 20).
    Guid? BatchId,
    string? BatchNumber,
    decimal Quantity,
    Guid UnitOfMeasureId,
    string UnitOfMeasureCode);

/// <summary>
/// A movement document and its lines. Carries the full answer to who, when, what, how much,
/// from where, to where and why (docs/PLAN.md, section 3.4).
/// </summary>
public record StockMovementResponse(
    Guid Id,
    string Number,
    MovementType MovementType,
    MovementStatus Status,
    // The production order that posted this movement, if any (docs/PLAN.md, section 19).
    Guid? ProductionOrderId,
    Guid? SourceLocationId,
    string? SourceLocationCode,
    Guid? DestinationLocationId,
    string? DestinationLocationCode,
    string? Reason,
    IReadOnlyList<StockMovementLineResponse> Lines,
    DateTime CreatedAt,
    Guid? CreatedBy,
    DateTime? ConfirmedAt,
    Guid? ConfirmedBy,
    DateTime? CancelledAt,
    Guid? CancelledBy);

/// <summary>
/// The unit is taken from the product, so a quantity can never be recorded in the wrong one.
///
/// <paramref name="BatchId"/> names the lot that moves. It is required for a batch-tracked product
/// and rejected for any other: the warehouse says which goods moved, the system never picks a lot
/// on its behalf (docs/PLAN.md, section 20). One line moves one lot — taking from two lots is two
/// lines.
/// </summary>
public record CreateStockMovementLineRequest(Guid ProductId, decimal Quantity, Guid? BatchId = null);

/// <summary>
/// Creates a Draft movement. Nothing happens to stock until it is confirmed
/// (docs/PLAN.md, section 13).
/// </summary>
public record CreateStockMovementRequest(
    MovementType MovementType,
    Guid? SourceLocationId,
    Guid? DestinationLocationId,
    string? Reason,
    IReadOnlyList<CreateStockMovementLineRequest> Lines);

public record CancelStockMovementRequest(string? Reason);

/// <summary>Filters and sorting for GET /api/stock-movements.</summary>
public class StockMovementQuery : PagedQuery
{
    /// <summary>Case-insensitive match against the document number.</summary>
    public string? Search { get; set; }

    public MovementType? MovementType { get; set; }

    public MovementStatus? Status { get; set; }

    /// <summary>Movements containing this product on any line.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Movements touching this location on either end.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Movements a given production run posted — its consumption and its output.</summary>
    public Guid? ProductionOrderId { get; set; }

    /// <summary>Movements that touched one lot, on any line.</summary>
    public Guid? BatchId { get; set; }

    /// <summary>Inclusive lower bound on CreatedAt, UTC.</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound on CreatedAt, UTC.</summary>
    public DateTime? To { get; set; }

    /// <summary>number | createdAt, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}
