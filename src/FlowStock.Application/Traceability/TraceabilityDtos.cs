using FlowStock.Application.Common;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;

namespace FlowStock.Application.Traceability;

/// <summary>
/// Which way stock went in one history entry, seen from the product — or, when the question was
/// asked about one location, seen from that location.
/// </summary>
public enum StockFlow
{
    /// <summary>Stock arrived: a receipt, a production output, the receiving end of a transfer.</summary>
    In,

    /// <summary>Stock left: a consumption, a write-off, the sending end of a transfer.</summary>
    Out,

    /// <summary>A transfer seen from the outside, where both ends are someone else's location.</summary>
    Transfer
}

/// <summary>
/// Who did something. The name is resolved here rather than left as an id, because "who moved
/// this material" is one of the questions this module exists to answer (docs/PLAN.md, section 39).
/// </summary>
public record TraceUser(Guid? UserId, string? Name, string? Email);

/// <summary>
/// One confirmed movement, as it touched one product: how much, which way, between which
/// locations, why, and who confirmed it.
/// </summary>
public record ProductHistoryEntry(
    Guid MovementId,
    string MovementNumber,
    MovementType MovementType,
    StockFlow Flow,
    DateTime OccurredAt,
    Guid? BatchId,
    string? BatchNumber,
    decimal Quantity,
    string UnitOfMeasureCode,
    Guid? SourceLocationId,
    string? SourceLocationCode,
    Guid? DestinationLocationId,
    string? DestinationLocationCode,
    string? Reason,
    Guid? ProductionOrderId,
    string? ProductionOrderNumber,
    TraceUser PerformedBy);

/// <summary>
/// One movement that brought a material to the place it was consumed from.
///
/// For a batch-tracked material these are the movements of that exact lot, which is the answer
/// section 19 asks for. For a product without batch tracking the stock in a location is fungible,
/// so these are the deliveries that could have supplied it — candidates, not certainties.
/// </summary>
public record MaterialSource(
    Guid MovementId,
    string MovementNumber,
    MovementType MovementType,
    DateTime OccurredAt,
    decimal Quantity,
    Guid? SourceLocationId,
    string? SourceLocationCode,
    string? Reason,
    TraceUser PerformedBy);

/// <summary>One material a run consumed, and where that material came from.</summary>
public record ConsumedMaterial(
    Guid ComponentProductId,
    string ComponentSku,
    string ComponentName,
    Guid? BatchId,
    string? BatchNumber,
    decimal RequiredQuantity,
    decimal ConsumedQuantity,
    string UnitOfMeasureCode,
    Guid? MovementId,
    string? MovementNumber,
    DateTime? ConsumedAt,
    TraceUser? ConsumedBy,
    IReadOnlyList<MaterialSource> Sources);

/// <summary>Where the finished goods of a run went.</summary>
public record ProductionOutput(
    Guid MovementId,
    string MovementNumber,
    DateTime OccurredAt,
    Guid? BatchId,
    string? BatchNumber,
    decimal Quantity,
    Guid LocationId,
    string LocationCode,
    TraceUser PerformedBy);

/// <summary>
/// Backward traceability (docs/PLAN.md, section 19): given a finished product, everything that
/// went into it — the recipe version, the materials, the movements that consumed them, the
/// deliveries that could have supplied them, and the people and times behind each step.
/// </summary>
public record ProductionTraceResponse(
    Guid ProductionOrderId,
    string Number,
    ProductionOrderStatus Status,
    Guid ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasureCode,
    Guid BillOfMaterialId,
    int BillOfMaterialVersion,
    decimal PlannedQuantity,
    decimal ProducedQuantity,
    Guid ProductionLocationId,
    string ProductionLocationCode,
    DateTime CreatedAt,
    TraceUser CreatedBy,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    ProductionOutput? Output,
    IReadOnlyList<ConsumedMaterial> Materials);

/// <summary>
/// Forward traceability (docs/PLAN.md, section 19): given a material, one run that consumed it
/// and the finished goods that run produced.
/// </summary>
public record MaterialUsageEntry(
    Guid ProductionOrderId,
    string Number,
    ProductionOrderStatus Status,
    decimal ConsumedQuantity,
    string UnitOfMeasureCode,
    DateTime? ConsumedAt,
    Guid ProductionLocationId,
    string ProductionLocationCode,
    Guid ProducedProductId,
    string ProducedSku,
    string ProducedProductName,
    decimal ProducedQuantity,
    string ProducedUnitOfMeasureCode,
    Guid OutputLocationId,
    string OutputLocationCode,
    DateTime? CompletedAt,
    TraceUser PerformedBy);

/// <summary>Where one lot of goods is now: a balance of that lot in one location.</summary>
public record BatchLocation(
    Guid LocationId,
    string LocationCode,
    Guid WarehouseId,
    string WarehouseCode,
    decimal Quantity,
    decimal ReservedQuantity);

/// <summary>
/// One production run that consumed a lot, and the goods that run made — the "Flour batch #123 →
/// Production Order #10042" of docs/PLAN.md, section 19.
/// </summary>
public record BatchConsumer(
    Guid ProductionOrderId,
    string Number,
    ProductionOrderStatus Status,
    decimal ConsumedQuantity,
    DateTime? ConsumedAt,
    Guid ProducedProductId,
    string ProducedSku,
    string ProducedProductName,
    decimal ProducedQuantity,
    Guid? ProducedBatchId,
    string? ProducedBatchNumber);

/// <summary>
/// Everything one lot can answer for: what it is, where it came from, where it is now, everything
/// that moved it, and which runs it ended up in (docs/PLAN.md, sections 19 and 20).
/// </summary>
public record BatchTraceResponse(
    Guid BatchId,
    string Number,
    Guid ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasureCode,
    string? Supplier,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    bool IsExpired,
    Guid? ProducedByProductionOrderId,
    string? ProducedByProductionOrderNumber,
    decimal QuantityOnHand,
    IReadOnlyList<BatchLocation> Locations,
    IReadOnlyList<ProductHistoryEntry> History,
    IReadOnlyList<BatchConsumer> ConsumedBy,
    DateTime CreatedAt,
    TraceUser CreatedBy);

/// <summary>Filters for GET /api/traceability/products/{productId}/history.</summary>
public class ProductHistoryQuery : PagedQuery
{
    /// <summary>Only movements touching this location, at either end.</summary>
    public Guid? LocationId { get; set; }

    public MovementType? MovementType { get; set; }

    /// <summary>Only movements of one lot.</summary>
    public Guid? BatchId { get; set; }

    /// <summary>Inclusive lower bound on when the movement was confirmed, UTC.</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound on when the movement was confirmed, UTC.</summary>
    public DateTime? To { get; set; }

    /// <summary>occurredAt, optionally prefixed with '-' for descending. Newest first by default.</summary>
    public string? Sort { get; set; }
}

/// <summary>Filters for GET /api/traceability/products/{productId}/usage.</summary>
public class MaterialUsageQuery : PagedQuery
{
    public ProductionOrderStatus? Status { get; set; }

    /// <summary>Inclusive lower bound on when the run started, UTC.</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound on when the run started, UTC.</summary>
    public DateTime? To { get; set; }

    /// <summary>startedAt, optionally prefixed with '-' for descending. Newest first by default.</summary>
    public string? Sort { get; set; }
}
