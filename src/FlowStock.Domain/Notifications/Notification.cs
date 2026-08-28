using FlowStock.Domain.Common;

namespace FlowStock.Domain.Notifications;

/// <summary>
/// What the system noticed and thinks somebody should know about (docs/PLAN.md, section 31).
/// Persisted by name, so the enum can be reordered without reinterpreting existing notifications.
/// </summary>
public enum NotificationType
{
    /// <summary>A production run cannot be fed from what its location holds.</summary>
    ProductionShortage,

    /// <summary>A lot is past its expiry date and still has stock somewhere.</summary>
    ExpiredBatch,

    /// <summary>A run has delivered its finished goods.</summary>
    ProductionCompleted,

    /// <summary>A transfer has been confirmed, so its destination now holds the goods.</summary>
    TransferReceived,

    /// <summary>
    /// Stock has fallen below what the product should keep. Not raised yet: there is nothing to
    /// compare a balance against until reorder points arrive with Phase 11.
    /// </summary>
    LowStock
}

/// <summary>
/// One notification (docs/PLAN.md, section 31). A record of something that happened or was
/// noticed, never a thing that happens: raising one changes no stock, and a notification that is
/// never read changes nothing at all.
///
/// Notifications belong to the team, not to a person: there are no per-user inboxes in this phase,
/// so reading one records who read it and when, for everybody.
/// </summary>
public class Notification : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public NotificationType Type { get; set; }

    /// <summary>
    /// What makes this notification the one and only notification of its event — for example
    /// <c>ExpiredBatch:{batchId}</c>. It is unique, which is what stops a periodic scan from
    /// raising the same expired lot every quarter of an hour.
    /// </summary>
    public string EventKey { get; set; } = string.Empty;

    /// <summary>A sentence a person can read without opening anything else.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>When the thing being reported happened, UTC. Not when the row was written.</summary>
    public DateTime OccurredAt { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? BatchId { get; set; }

    public Guid? LocationId { get; set; }

    public Guid? ProductionOrderId { get; set; }

    public Guid? StockMovementId { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public Guid? ReadBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static string KeyFor(NotificationType type, params object[] parts) =>
        $"{type}:{string.Join(':', parts)}";
}
