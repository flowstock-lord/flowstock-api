using FlowStock.Application.Common;

namespace FlowStock.Application.Inventory;

/// <summary>
/// One lot of a product (docs/PLAN.md, section 20). The quantities are not here: how much of a
/// lot is left, and where, is a stock balance — read it from <c>/api/stock?batchId=</c>.
/// </summary>
public record BatchResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasureCode,
    string Number,
    string? Supplier,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    bool IsExpired,
    Guid? ProductionOrderId,
    string? Notes,
    DateTime CreatedAt,
    Guid? CreatedBy,
    DateTime? UpdatedAt);

/// <summary>
/// Registers a lot that has arrived, before the receipt that books it in. The number is unique
/// within the product and immutable afterwards — history refers to it.
/// </summary>
public record CreateBatchRequest(
    Guid ProductId,
    string Number,
    string? Supplier,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    string? Notes);

/// <summary>
/// Corrects what is known about a lot. The number and the product never change: the goods on the
/// shelf are what they are, and the movements that named them cannot be rewritten.
/// </summary>
public record UpdateBatchRequest(
    string? Supplier,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    string? Notes);

/// <summary>Filters and sorting for GET /api/batches.</summary>
public class BatchQuery : PagedQuery
{
    /// <summary>Case-insensitive match against the lot number.</summary>
    public string? Search { get; set; }

    public Guid? ProductId { get; set; }

    /// <summary>Case-insensitive match against the supplier.</summary>
    public string? Supplier { get; set; }

    /// <summary>Lots produced by one run.</summary>
    public Guid? ProductionOrderId { get; set; }

    /// <summary>Lots whose expiry date is before this day — what expiry management asks for.</summary>
    public DateOnly? ExpiringBefore { get; set; }

    /// <summary>Only lots that are already past their expiry date, or only those that are not.</summary>
    public bool? IsExpired { get; set; }

    /// <summary>number | expiryDate | createdAt, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}
