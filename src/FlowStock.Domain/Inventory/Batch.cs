using FlowStock.Domain.Catalog;
using FlowStock.Domain.Common;

namespace FlowStock.Domain.Inventory;

/// <summary>
/// One lot of a product: the flour that arrived on one delivery, or the cookies one production run
/// made (docs/PLAN.md, section 20).
///
/// A batch identifies goods, it does not hold them: how much of it is left, and where, is a
/// <see cref="Stock"/> balance like any other. The batch is what makes that balance answerable —
/// which supplier it came from, when it was made, when it expires, and which run produced it.
/// </summary>
public class Batch : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>
    /// The lot number as it is written on the goods (`FL-2026-0828`). Normalized upper-case and
    /// unique per product, and immutable after creation — inventory history refers to it.
    /// </summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>
    /// Who supplied it, as free text. There is no supplier entity yet — purchasing is not in any
    /// phase of docs/PLAN.md — and section 20 asks only that a batch can name its supplier.
    /// </summary>
    public string? Supplier { get; set; }

    /// <summary>When the goods were made. A calendar date, not an instant.</summary>
    public DateOnly? ProductionDate { get; set; }

    /// <summary>The last day the goods may be used. A calendar date, not an instant.</summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>
    /// The run that produced this lot, for a batch of finished goods. Null for a batch that
    /// arrived from outside (docs/PLAN.md, section 19).
    /// </summary>
    public Guid? ProductionOrderId { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static string NormalizeNumber(string number) => number.Trim().ToUpperInvariant();

    /// <summary>Whether the lot is past its expiry date on the given day, in UTC.</summary>
    public bool IsExpiredOn(DateOnly today) => ExpiryDate is { } expiry && expiry < today;
}
