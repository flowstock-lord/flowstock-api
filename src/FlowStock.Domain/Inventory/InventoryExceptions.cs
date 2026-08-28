using FlowStock.Domain.Common;

namespace FlowStock.Domain.Inventory;

public class MovementNotFoundException(Guid movementId)
    : DomainException("MOVEMENT_NOT_FOUND", $"Stock movement '{movementId}' was not found.",
        new Dictionary<string, object?> { ["movementId"] = movementId });

/// <summary>The movement does not describe a possible stock operation.</summary>
public class InvalidMovementException(string message, IReadOnlyDictionary<string, object?>? details = null)
    : DomainException("INVALID_MOVEMENT", message, details);

/// <summary>
/// The core inventory rule: available stock may never go negative (CLAUDE.md, rule 6).
/// </summary>
public class InsufficientStockException(
    Guid productId,
    string sku,
    Guid locationId,
    string locationCode,
    decimal requested,
    decimal available)
    : DomainException(
        "INSUFFICIENT_STOCK",
        $"Insufficient stock for product {sku} in location {locationCode}.",
        new Dictionary<string, object?>
        {
            ["productId"] = productId,
            ["sku"] = sku,
            ["locationId"] = locationId,
            ["locationCode"] = locationCode,
            ["requested"] = requested,
            ["available"] = available
        });

/// <summary>A confirmed movement is history: it is never re-confirmed, edited or cancelled.</summary>
public class MovementAlreadyConfirmedException(Guid movementId, string number)
    : DomainException("MOVEMENT_ALREADY_CONFIRMED",
        $"Stock movement {number} is already confirmed. Correct it with a compensating movement.",
        new Dictionary<string, object?> { ["movementId"] = movementId, ["number"] = number });

public class MovementAlreadyCancelledException(Guid movementId, string number)
    : DomainException("MOVEMENT_ALREADY_CANCELLED", $"Stock movement {number} is already cancelled.",
        new Dictionary<string, object?> { ["movementId"] = movementId, ["number"] = number });

/// <summary>A deactivated location is closed for stock operations.</summary>
public class LocationInactiveException(Guid locationId, string code)
    : DomainException("LOCATION_INACTIVE", $"Storage location '{code}' is deactivated.",
        new Dictionary<string, object?> { ["locationId"] = locationId, ["code"] = code });
