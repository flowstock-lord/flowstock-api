namespace FlowStock.Domain.Common;

/// <summary>
/// Audit stamps applied automatically on save. Timestamps are UTC.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }

    Guid? CreatedBy { get; set; }

    DateTime? UpdatedAt { get; set; }

    Guid? UpdatedBy { get; set; }
}
