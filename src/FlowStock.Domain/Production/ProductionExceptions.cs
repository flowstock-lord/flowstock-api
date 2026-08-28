using FlowStock.Domain.Common;

namespace FlowStock.Domain.Production;

public class BomNotFoundException(Guid billOfMaterialId)
    : DomainException("BOM_NOT_FOUND", $"Bill of materials '{billOfMaterialId}' was not found.",
        new Dictionary<string, object?> { ["billOfMaterialId"] = billOfMaterialId });

/// <summary>The recipe does not describe something that can be produced.</summary>
public class BomInvalidException(string message, IReadOnlyDictionary<string, object?>? details = null)
    : DomainException("BOM_INVALID", message, details);
