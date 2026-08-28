namespace FlowStock.Domain.Production;

/// <summary>
/// See docs/PLAN.md, sections 15 and 18. Persisted by name, so the enum can be reordered without
/// reinterpreting existing orders.
/// </summary>
public enum ProductionOrderStatus
{
    /// <summary>Written down, nothing reserved, nothing consumed.</summary>
    Draft,

    /// <summary>Materials are reserved at the production location and wait for the run to start.</summary>
    Planned,

    /// <summary>Materials have been consumed; the finished goods are not booked in yet.</summary>
    InProgress,

    /// <summary>The finished goods have been booked into the output location.</summary>
    Completed,

    /// <summary>Abandoned before any material was consumed. Reservations are released.</summary>
    Cancelled
}
