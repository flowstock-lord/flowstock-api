using FlowStock.Application.Common;

namespace FlowStock.Application.Catalog;

public record UnitOfMeasureResponse(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateUnitOfMeasureRequest(string Code, string Name);

/// <summary>The code is immutable once created — it is referenced by every product using it.</summary>
public record UpdateUnitOfMeasureRequest(string Name);

/// <summary>Filters for GET /api/units-of-measure.</summary>
public class UnitOfMeasureQuery : PagedQuery
{
    /// <summary>Case-insensitive match against code or name.</summary>
    public string? Search { get; set; }

    public bool? IsActive { get; set; }
}
