using FlowStock.Domain.Common;

namespace FlowStock.Domain.Warehouses;

/// <summary>
/// A physical inventory site. See docs/PLAN.md, section 8. Stock never sits on a warehouse
/// directly — it sits in one of the warehouse's storage locations.
/// </summary>
public class Warehouse : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stored normalized (trimmed, upper-case) and unique across warehouses.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public WarehouseType WarehouseType { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public ICollection<StorageLocation> Locations { get; set; } = [];

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
