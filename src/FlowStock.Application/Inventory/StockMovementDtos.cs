using FlowStock.Application.Common;
using FlowStock.Domain.Inventory;

namespace FlowStock.Application.Inventory;

public record StockMovementLineResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
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

/// <summary>The unit is taken from the product, so a quantity can never be recorded in the wrong one.</summary>
public record CreateStockMovementLineRequest(Guid ProductId, decimal Quantity);

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

    /// <summary>Inclusive lower bound on CreatedAt, UTC.</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound on CreatedAt, UTC.</summary>
    public DateTime? To { get; set; }

    /// <summary>number | createdAt, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}
