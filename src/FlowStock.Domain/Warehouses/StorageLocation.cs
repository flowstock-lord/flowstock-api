using FlowStock.Domain.Common;

namespace FlowStock.Domain.Warehouses;

/// <summary>
/// A physical place inside a warehouse (a rack, a production line). See docs/PLAN.md, section 9.
/// This is the address stock is actually held at, so a location belongs to exactly one warehouse
/// and never moves to another (docs/PLAN.md, section 27).
/// </summary>
public class StorageLocation : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WarehouseId { get; set; }

    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>Stored normalized (trimmed, upper-case) and unique within its warehouse.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
