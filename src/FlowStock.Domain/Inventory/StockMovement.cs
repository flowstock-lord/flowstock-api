using FlowStock.Domain.Common;
using FlowStock.Domain.Warehouses;

namespace FlowStock.Domain.Inventory;

/// <summary>
/// One business operation that moves stock (docs/PLAN.md, section 12). The document carries the
/// endpoints and the reason; the individual products and quantities live on its lines.
///
/// <see cref="IAuditable.CreatedBy"/> is the <c>CreatedByUserId</c> of section 11 — the movement
/// is the audit record, so it does not need a second column meaning the same thing.
/// </summary>
public class StockMovement : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human readable document number, unique and assigned by the system.</summary>
    public string Number { get; set; } = string.Empty;

    public MovementType MovementType { get; set; }

    public MovementStatus Status { get; set; } = MovementStatus.Draft;

    /// <summary>Where stock leaves from. Null for a receipt.</summary>
    public Guid? SourceLocationId { get; set; }

    public StorageLocation? SourceLocation { get; set; }

    /// <summary>Where stock arrives. Null for a write-off or a negative adjustment.</summary>
    public Guid? DestinationLocationId { get; set; }

    public StorageLocation? DestinationLocation { get; set; }

    public string? Reason { get; set; }

    /// <summary>
    /// The production order that posted this movement, for consumption and output documents
    /// (docs/PLAN.md, section 19). Null for every movement a warehouse user posts by hand. It is
    /// what makes traceability work in both directions: from a material to the runs that used it,
    /// and from a finished product back to what went into it.
    /// </summary>
    public Guid? ProductionOrderId { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public Guid? ConfirmedBy { get; set; }

    public DateTime? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public ICollection<StockMovementLine> Lines { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Which endpoints a movement type may and must carry (docs/PLAN.md, section 11). A receipt
    /// comes from outside the system, a transfer connects two locations, and an adjustment
    /// corrects exactly one location in one direction.
    /// </summary>
    public static void ValidateEndpoints(MovementType type, Guid? sourceLocationId, Guid? destinationLocationId)
    {
        switch (type)
        {
            case MovementType.Receipt when sourceLocationId is not null:
                throw new InvalidMovementException("A receipt comes from outside the system and has no source location.");
            case MovementType.Receipt when destinationLocationId is null:
                throw new InvalidMovementException("A receipt requires a destination location.");

            case MovementType.Transfer when sourceLocationId is null || destinationLocationId is null:
                throw new InvalidMovementException("A transfer requires both a source and a destination location.");
            case MovementType.Transfer when sourceLocationId == destinationLocationId:
                throw new InvalidMovementException("A transfer must move stock between two different locations.");

            case MovementType.Adjustment when (sourceLocationId is null) == (destinationLocationId is null):
                throw new InvalidMovementException(
                    "An adjustment corrects exactly one location: give a destination for a surplus " +
                    "or a source for a shortage.");

            // Materials leave the system into the run; the goods the run yields come out of it.
            case MovementType.Consumption or MovementType.WriteOff when sourceLocationId is null:
                throw new InvalidMovementException($"A {type} requires a source location.");
            case MovementType.Consumption or MovementType.WriteOff when destinationLocationId is not null:
                throw new InvalidMovementException($"A {type} removes stock and has no destination location.");

            case MovementType.ProductionOutput when destinationLocationId is null:
                throw new InvalidMovementException("A production output requires a destination location.");
            case MovementType.ProductionOutput when sourceLocationId is not null:
                throw new InvalidMovementException(
                    "A production output is created by the run and has no source location.");
        }
    }
}
