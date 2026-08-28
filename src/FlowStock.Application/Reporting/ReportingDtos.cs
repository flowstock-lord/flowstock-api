using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;

namespace FlowStock.Application.Reporting;

/// <summary>
/// What one product holds across the whole warehouse (docs/PLAN.md, section 30, "Current stock").
///
/// Quantities are only ever summed within one product, so they are only ever summed within one
/// unit of measure: kilograms and pieces are not a total.
/// </summary>
public record CurrentStockRow(
    Guid ProductId,
    string Sku,
    string ProductName,
    ProductType ProductType,
    string UnitOfMeasureCode,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    int LocationCount,
    int BatchCount);

/// <summary>What one product holds in one warehouse ("Stock by warehouse").</summary>
public record WarehouseStockRow(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasureCode,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    int LocationCount);

/// <summary>
/// One line of one confirmed movement ("Stock movement history"): the journal read line by line
/// rather than document by document, so a product's ins and outs can be totalled by eye.
/// </summary>
public record MovementHistoryRow(
    Guid MovementId,
    string MovementNumber,
    MovementType MovementType,
    DateTime OccurredAt,
    Guid ProductId,
    string Sku,
    string ProductName,
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
    Guid? ConfirmedBy);

/// <summary>One production run as a report line ("Production history").</summary>
public record ProductionHistoryRow(
    Guid ProductionOrderId,
    string Number,
    ProductionOrderStatus Status,
    Guid ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasureCode,
    decimal PlannedQuantity,
    decimal ProducedQuantity,
    // Produced against planned, as a percentage. Null until the run has completed.
    decimal? YieldPercent,
    Guid? OutputBatchId,
    string? OutputBatchNumber,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);

/// <summary>
/// How much of one material production has consumed ("Material consumption"). Built from confirmed
/// consumption movements, not from what orders planned to take: the report says what left the
/// shelf.
/// </summary>
public record MaterialConsumptionRow(
    Guid ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasureCode,
    decimal ConsumedQuantity,
    int MovementCount,
    DateTime? FirstConsumedAt,
    DateTime? LastConsumedAt);

/// <summary>How much of one product production has made ("Finished goods production").</summary>
public record FinishedGoodsRow(
    Guid ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasureCode,
    decimal ProducedQuantity,
    int MovementCount,
    DateTime? FirstProducedAt,
    DateTime? LastProducedAt);

/// <summary>
/// One confirmed correction of a counted quantity ("Inventory adjustments"). A surplus arrives
/// into a location, a shortage leaves one, and both must say why.
/// </summary>
public record AdjustmentRow(
    Guid MovementId,
    string MovementNumber,
    DateTime OccurredAt,
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid? BatchId,
    string? BatchNumber,
    // True when the count found more than the books said.
    bool IsSurplus,
    decimal Quantity,
    string UnitOfMeasureCode,
    Guid LocationId,
    string LocationCode,
    Guid WarehouseId,
    string WarehouseCode,
    string? Reason,
    Guid? ConfirmedBy);

/// <summary>Filters for GET /api/reports/current-stock.</summary>
public class CurrentStockQuery : PagedQuery
{
    /// <summary>Case-insensitive match against SKU or product name.</summary>
    public string? Search { get; set; }

    public ProductType? ProductType { get; set; }

    /// <summary>Only what one warehouse holds.</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>
    /// Hides balances that have fallen to zero. On by default: a stock report is about stock.
    /// Either way the report reads balances, so a product nothing ever touched has no row at all.
    /// </summary>
    public bool OnlyInStock { get; set; } = true;

    /// <summary>sku | quantity, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}

/// <summary>Filters for GET /api/reports/stock-by-warehouse.</summary>
public class WarehouseStockQuery : PagedQuery
{
    public Guid? WarehouseId { get; set; }

    public Guid? ProductId { get; set; }

    public ProductType? ProductType { get; set; }

    public bool OnlyInStock { get; set; } = true;

    /// <summary>warehouse | sku | quantity, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}

/// <summary>Filters shared by the reports that read a period of history.</summary>
public class HistoryReportQuery : PagedQuery
{
    public Guid? ProductId { get; set; }

    /// <summary>Inclusive lower bound on when the movement was confirmed, UTC.</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound on when the movement was confirmed, UTC.</summary>
    public DateTime? To { get; set; }
}

/// <summary>Filters for GET /api/reports/movement-history.</summary>
public class MovementHistoryQuery : HistoryReportQuery
{
    public MovementType? MovementType { get; set; }

    /// <summary>Movements touching this location at either end.</summary>
    public Guid? LocationId { get; set; }

    public Guid? WarehouseId { get; set; }

    public Guid? BatchId { get; set; }

    /// <summary>occurredAt, optionally prefixed with '-' for descending. Newest first by default.</summary>
    public string? Sort { get; set; }
}

/// <summary>Filters for GET /api/reports/production-history.</summary>
public class ProductionHistoryQuery : PagedQuery
{
    public Guid? ProductId { get; set; }

    public ProductionOrderStatus? Status { get; set; }

    /// <summary>Inclusive lower bound on when the run was created, UTC.</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound on when the run was created, UTC.</summary>
    public DateTime? To { get; set; }

    /// <summary>number | createdAt | completedAt, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}

/// <summary>Filters for the two totalled production reports.</summary>
public class ProductionTotalsQuery : HistoryReportQuery
{
    /// <summary>Only what one location consumed or produced.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>sku | quantity, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}

/// <summary>Filters for GET /api/reports/adjustments.</summary>
public class AdjustmentReportQuery : HistoryReportQuery
{
    public Guid? LocationId { get; set; }

    public Guid? WarehouseId { get; set; }

    /// <summary>Only surpluses, or only shortages.</summary>
    public bool? IsSurplus { get; set; }

    /// <summary>occurredAt, optionally prefixed with '-' for descending. Newest first by default.</summary>
    public string? Sort { get; set; }
}
