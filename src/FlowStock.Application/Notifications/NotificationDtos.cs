using FlowStock.Application.Common;
using FlowStock.Domain.Notifications;

namespace FlowStock.Application.Notifications;

/// <summary>
/// One notification, with the ids of whatever it is about so a client can link straight to it.
/// </summary>
public record NotificationResponse(
    Guid Id,
    NotificationType Type,
    string Message,
    DateTime OccurredAt,
    Guid? ProductId,
    string? Sku,
    string? ProductName,
    Guid? BatchId,
    string? BatchNumber,
    Guid? LocationId,
    string? LocationCode,
    Guid? ProductionOrderId,
    string? ProductionOrderNumber,
    Guid? StockMovementId,
    string? StockMovementNumber,
    bool IsRead,
    DateTime? ReadAt,
    Guid? ReadBy,
    DateTime CreatedAt);

/// <summary>What one run of the checks noticed. Only the notifications it actually raised count.</summary>
public record NotificationScanResponse(
    int Raised,
    int ExpiredBatches,
    int ProductionShortages,
    DateTime ScannedAt);

/// <summary>Filters for GET /api/notifications.</summary>
public class NotificationQuery : PagedQuery
{
    public NotificationType? Type { get; set; }

    /// <summary>Only what has been read, or only what has not.</summary>
    public bool? IsRead { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? ProductionOrderId { get; set; }

    /// <summary>Inclusive lower bound on when the thing happened, UTC.</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound on when the thing happened, UTC.</summary>
    public DateTime? To { get; set; }

    /// <summary>occurredAt, optionally prefixed with '-' for descending. Newest first by default.</summary>
    public string? Sort { get; set; }
}
