using FlowStock.Domain.Common;

namespace FlowStock.Domain.Warehouses;

public class WarehouseNotFoundException(Guid warehouseId)
    : DomainException("WAREHOUSE_NOT_FOUND", $"Warehouse '{warehouseId}' was not found.",
        new Dictionary<string, object?> { ["warehouseId"] = warehouseId });

public class WarehouseCodeAlreadyExistsException(string code)
    : DomainException("WAREHOUSE_CODE_EXISTS", $"A warehouse with code '{code}' already exists.",
        new Dictionary<string, object?> { ["code"] = code });

/// <summary>A deactivated warehouse is closed for new locations.</summary>
public class WarehouseInactiveException(Guid warehouseId, string code)
    : DomainException("WAREHOUSE_INACTIVE", $"Warehouse '{code}' is deactivated.",
        new Dictionary<string, object?> { ["warehouseId"] = warehouseId, ["code"] = code });

public class LocationNotFoundException(Guid locationId)
    : DomainException("LOCATION_NOT_FOUND", $"Storage location '{locationId}' was not found.",
        new Dictionary<string, object?> { ["locationId"] = locationId });

public class LocationCodeAlreadyExistsException(Guid warehouseId, string code)
    : DomainException("LOCATION_CODE_EXISTS", $"Location code '{code}' already exists in this warehouse.",
        new Dictionary<string, object?> { ["warehouseId"] = warehouseId, ["code"] = code });
