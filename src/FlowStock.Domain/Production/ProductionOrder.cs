using FlowStock.Domain.Catalog;
using FlowStock.Domain.Common;
using FlowStock.Domain.Warehouses;

namespace FlowStock.Domain.Production;

/// <summary>
/// One actual production run (docs/PLAN.md, section 15): produce <see cref="PlannedQuantity"/> of
/// <see cref="Product"/> from the materials of <see cref="BillOfMaterial"/>.
///
/// The order is the document; it never changes stock by itself. Consuming materials and booking
/// finished goods in are stock movements it owns, exactly like every other stock change
/// (CLAUDE.md, rule 1).
/// </summary>
public class ProductionOrder : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human readable document number, unique and assigned by the system.</summary>
    public string Number { get; set; } = string.Empty;

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>
    /// The exact recipe version this run was built from. It is recorded rather than looked up
    /// again later, so a completed order can always show what it actually used.
    /// </summary>
    public Guid BillOfMaterialId { get; set; }

    public BillOfMaterial BillOfMaterial { get; set; } = null!;

    public decimal PlannedQuantity { get; set; }

    /// <summary>How much the run actually yielded. Zero until the order is completed.</summary>
    public decimal ProducedQuantity { get; set; }

    /// <summary>Where the materials are reserved and consumed from — the shop floor location.</summary>
    public Guid ProductionLocationId { get; set; }

    public StorageLocation ProductionLocation { get; set; } = null!;

    /// <summary>
    /// Where the finished goods are booked in (docs/PLAN.md, section 17). Not in section 15's
    /// suggested field list, but section 17 requires the output to land in a named location, and
    /// finished goods rarely stay where the materials were consumed.
    /// </summary>
    public Guid OutputLocationId { get; set; }

    public StorageLocation OutputLocation { get; set; } = null!;

    public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Draft;

    public DateTime? PlannedStartAt { get; set; }

    public DateTime? ActualStartAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    /// <summary>Free text: why this run exists, or why it was cancelled.</summary>
    public string? Notes { get; set; }

    /// <summary>What the run needs, scaled from the recipe when the order is created.</summary>
    public ICollection<ProductionOrderMaterial> Materials { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// The workflow of docs/PLAN.md, section 18: <c>Draft → Planned → InProgress → Completed</c>,
    /// with a cancellation possible only while nothing has been consumed yet.
    /// </summary>
    public void RequireStatus(ProductionOrderStatus expected, string operation)
    {
        if (Status == expected)
        {
            return;
        }

        if (Status == ProductionOrderStatus.Completed)
        {
            throw new ProductionOrderAlreadyCompletedException(Id, Number);
        }

        throw new ProductionOrderInvalidException(
            $"Production order {Number} cannot be {operation}: it is {Status}, not {expected}.",
            new Dictionary<string, object?>
            {
                ["productionOrderId"] = Id,
                ["number"] = Number,
                ["status"] = Status.ToString(),
                ["expectedStatus"] = expected.ToString()
            });
    }
}
