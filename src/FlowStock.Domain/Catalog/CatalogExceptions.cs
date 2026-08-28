using FlowStock.Domain.Common;

namespace FlowStock.Domain.Catalog;

public class ProductNotFoundException(Guid productId)
    : DomainException("PRODUCT_NOT_FOUND", $"Product '{productId}' was not found.",
        new Dictionary<string, object?> { ["productId"] = productId });

public class SkuAlreadyExistsException(string sku)
    : DomainException("SKU_ALREADY_EXISTS", $"A product with SKU '{sku}' already exists.",
        new Dictionary<string, object?> { ["sku"] = sku });

public class UnitOfMeasureNotFoundException(Guid unitOfMeasureId)
    : DomainException("UNIT_OF_MEASURE_NOT_FOUND", $"Unit of measure '{unitOfMeasureId}' was not found.",
        new Dictionary<string, object?> { ["unitOfMeasureId"] = unitOfMeasureId });

public class UnitOfMeasureCodeAlreadyExistsException(string code)
    : DomainException("UNIT_OF_MEASURE_CODE_EXISTS", $"A unit of measure with code '{code}' already exists.",
        new Dictionary<string, object?> { ["code"] = code });

/// <summary>An inactive unit cannot be attached to a product — it would measure nothing meaningful.</summary>
public class UnitOfMeasureInactiveException(Guid unitOfMeasureId, string code)
    : DomainException("UNIT_OF_MEASURE_INACTIVE", $"Unit of measure '{code}' is deactivated.",
        new Dictionary<string, object?> { ["unitOfMeasureId"] = unitOfMeasureId, ["code"] = code });
