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
    decimal available,
    Guid? batchId = null,
    string? batchNumber = null)
    : DomainException(
        "INSUFFICIENT_STOCK",
        batchNumber is null
            ? $"Insufficient stock for product {sku} in location {locationCode}."
            : $"Insufficient stock for product {sku}, batch {batchNumber}, in location {locationCode}.",
        Describe(productId, sku, locationId, locationCode, requested, available, batchId, batchNumber))
{
    /// <summary>The lot is named only when the shortage is of one specific lot.</summary>
    private static Dictionary<string, object?> Describe(
        Guid productId,
        string sku,
        Guid locationId,
        string locationCode,
        decimal requested,
        decimal available,
        Guid? batchId,
        string? batchNumber)
    {
        var details = new Dictionary<string, object?>
        {
            ["productId"] = productId,
            ["sku"] = sku,
            ["locationId"] = locationId,
            ["locationCode"] = locationCode,
            ["requested"] = requested,
            ["available"] = available
        };

        if (batchId is not null)
        {
            details["batchId"] = batchId;
            details["batchNumber"] = batchNumber;
        }

        return details;
    }
}

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

public class BatchNotFoundException(Guid batchId)
    : DomainException("BATCH_NOT_FOUND", $"Batch '{batchId}' was not found.",
        new Dictionary<string, object?> { ["batchId"] = batchId });

public class BatchNumberAlreadyExistsException(Guid productId, string number)
    : DomainException("BATCH_NUMBER_EXISTS", $"Batch '{number}' already exists for this product.",
        new Dictionary<string, object?> { ["productId"] = productId, ["number"] = number });

/// <summary>
/// A batch-tracked product is never moved anonymously: history that cannot name the lot cannot
/// answer where the goods came from (docs/PLAN.md, section 20).
/// </summary>
public class BatchRequiredException(Guid productId, string sku)
    : DomainException("BATCH_REQUIRED", $"Product {sku} is batch tracked, so every line must name a batch.",
        new Dictionary<string, object?> { ["productId"] = productId, ["sku"] = sku });

/// <summary>A product that is not batch tracked has no lots to choose from.</summary>
public class BatchNotAllowedException(Guid productId, string sku)
    : DomainException("BATCH_NOT_ALLOWED", $"Product {sku} is not batch tracked, so a batch cannot be given.",
        new Dictionary<string, object?> { ["productId"] = productId, ["sku"] = sku });

/// <summary>The batch does not belong where it was used.</summary>
public class BatchInvalidException(string message, IReadOnlyDictionary<string, object?>? details = null)
    : DomainException("BATCH_INVALID", message, details);
