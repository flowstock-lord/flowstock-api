using FlowStock.Domain.Common;

namespace FlowStock.Domain.Production;

public class BomNotFoundException(Guid billOfMaterialId)
    : DomainException("BOM_NOT_FOUND", $"Bill of materials '{billOfMaterialId}' was not found.",
        new Dictionary<string, object?> { ["billOfMaterialId"] = billOfMaterialId });

/// <summary>The recipe does not describe something that can be produced.</summary>
public class BomInvalidException(string message, IReadOnlyDictionary<string, object?>? details = null)
    : DomainException("BOM_INVALID", message, details);

public class ProductionOrderNotFoundException(Guid productionOrderId)
    : DomainException("PRODUCTION_ORDER_NOT_FOUND", $"Production order '{productionOrderId}' was not found.",
        new Dictionary<string, object?> { ["productionOrderId"] = productionOrderId });

/// <summary>The order does not describe a production run that can be carried out in its current state.</summary>
public class ProductionOrderInvalidException(string message, IReadOnlyDictionary<string, object?>? details = null)
    : DomainException("PRODUCTION_ORDER_INVALID", message, details);

/// <summary>
/// A completed order is history: its consumption and its output are confirmed movements, so it is
/// corrected with a compensating operation, never by reopening it.
/// </summary>
public class ProductionOrderAlreadyCompletedException(Guid productionOrderId, string number)
    : DomainException("PRODUCTION_ORDER_ALREADY_COMPLETED",
        $"Production order {number} is already completed.",
        new Dictionary<string, object?> { ["productionOrderId"] = productionOrderId, ["number"] = number });
