using FlowStock.Application.Common;
using FlowStock.Domain.Warehouses;

namespace FlowStock.Application.Warehouses;

public record WarehouseResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    WarehouseType WarehouseType,
    bool IsActive,
    int LocationCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateWarehouseRequest(
    string Code,
    string Name,
    string? Description,
    WarehouseType WarehouseType);

/// <summary>The code is immutable — inventory history addresses warehouses by it.</summary>
public record UpdateWarehouseRequest(
    string Name,
    string? Description,
    WarehouseType WarehouseType);

/// <summary>Filters and sorting for GET /api/warehouses.</summary>
public class WarehouseQuery : PagedQuery
{
    /// <summary>Case-insensitive match against code or name.</summary>
    public string? Search { get; set; }

    public WarehouseType? WarehouseType { get; set; }

    public bool? IsActive { get; set; }

    /// <summary>code | name | type | createdAt, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}

public record StorageLocationResponse(
    Guid Id,
    Guid WarehouseId,
    string WarehouseCode,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateStorageLocationRequest(
    Guid WarehouseId,
    string Code,
    string Name,
    string? Description);

/// <summary>
/// Neither the code nor the warehouse can change: a location is a physical address that stock
/// history points at (docs/PLAN.md, section 27).
/// </summary>
public record UpdateStorageLocationRequest(string Name, string? Description);

/// <summary>Filters and sorting for GET /api/storage-locations.</summary>
public class StorageLocationQuery : PagedQuery
{
    /// <summary>Case-insensitive match against code or name.</summary>
    public string? Search { get; set; }

    public Guid? WarehouseId { get; set; }

    public bool? IsActive { get; set; }

    /// <summary>code | name | createdAt, optionally prefixed with '-' for descending.</summary>
    public string? Sort { get; set; }
}
