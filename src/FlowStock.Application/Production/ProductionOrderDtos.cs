using FlowStock.Application.Common;
using FlowStock.Domain.Production;

namespace FlowStock.Application.Production;

/// <summary>What one material of a run needs, and what it has actually taken.</summary>
public record ProductionOrderMaterialResponse(
    Guid Id,
    Guid ComponentProductId,
    string ComponentSku,
    string ComponentName,
    // The lot this run will take, for a batch-tracked component.
    Guid? BatchId,
    string? BatchNumber,
    decimal RequiredQuantity,
    decimal ConsumedQuantity,
    Guid UnitOfMeasureId,
    string UnitOfMeasureCode);

/// <summary>
/// A production run and everything it is answerable for: what it makes, from which recipe
/// version, where the materials come from, where the goods go, and the movements it posted
/// (docs/PLAN.md, sections 15 and 19).
/// </summary>
public record ProductionOrderResponse(
    Guid Id,
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
    Guid OutputLocationId,
    string OutputLocationCode,
    // The lot the finished goods were booked in under, once the run has completed.
    Guid? OutputBatchId,
    string? OutputBatchNumber,
    string? Notes,
    IReadOnlyList<ProductionOrderMaterialResponse> Materials,
    DateTime? PlannedStartAt,
    DateTime? ActualStartAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    Guid? CancelledBy,
    DateTime CreatedAt,
    Guid? CreatedBy,
    DateTime? UpdatedAt);

/// <summary>
/// Creates a Draft order. Nothing is reserved and nothing is consumed until it is planned and
/// started (docs/PLAN.md, section 18).
///
/// <paramref name="BillOfMaterialId"/> may be omitted, in which case the product's active recipe
/// version is used and recorded on the order.
/// </summary>
public record CreateProductionOrderRequest(
    Guid ProductId,
    decimal PlannedQuantity,
    Guid ProductionLocationId,
    Guid OutputLocationId,
    Guid? BillOfMaterialId,
    DateTime? PlannedStartAt,
    string? Notes,
    IReadOnlyList<ProductionOrderMaterialBatchRequest>? Materials = null);

/// <summary>
/// Which lot the run will take of one component. Required for every batch-tracked component and
/// rejected for any other: a run names the goods it will consume, the system never picks them
/// (docs/PLAN.md, section 20).
/// </summary>
public record ProductionOrderMaterialBatchRequest(Guid ComponentProductId, Guid BatchId);

/// <summary>
/// Completes a run. <paramref name="ProducedQuantity"/> may differ from the planned quantity —
/// a run yields what it yields — and defaults to the planned quantity when omitted.
/// </summary>
/// <summary>
/// <paramref name="OutputBatchNumber"/> names the lot the finished goods are booked in under. It
/// is required only for a batch-tracked product, where it defaults to the order number if omitted,
/// and rejected for any other.
/// </summary>
public record CompleteProductionOrderRequest(
    decimal? ProducedQuantity,
    string? Notes,
    string? OutputBatchNumber = null,
    DateOnly? OutputBatchExpiryDate = null);

public record CancelProductionOrderRequest(string? Reason);

/// <summary>Filters and sorting for GET /api/production-orders.</summary>
public class ProductionOrderQuery : PagedQuery
{
    /// <summary>Case-insensitive match against the document number.</summary>
    public string? Search { get; set; }

    public ProductionOrderStatus? Status { get; set; }

    /// <summary>Orders producing this product.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Orders that consume this product as a material — forward traceability.</summary>
    public Guid? ComponentProductId { get; set; }

    /// <summary>Orders touching this location at either end.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Inclusive lower bound on CreatedAt, UTC.</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound on CreatedAt, UTC.</summary>
    public DateTime? To { get; set; }

    /// <summary>number | createdAt | plannedStartAt, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}
