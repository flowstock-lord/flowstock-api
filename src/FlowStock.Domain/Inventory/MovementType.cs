namespace FlowStock.Domain.Inventory;

/// <summary>
/// Why stock moved. See docs/PLAN.md, section 11. Persisted by name, so the enum can be
/// reordered without reinterpreting existing history.
/// </summary>
public enum MovementType
{
    /// <summary>Between two locations.</summary>
    Transfer,

    /// <summary>From outside the system into a location (a supplier delivery).</summary>
    Receipt,

    /// <summary>A correction after a physical count, in either direction.</summary>
    Adjustment,

    /// <summary>Materials consumed by a production order. Phase 6.</summary>
    Consumption,

    /// <summary>Goods produced by a production order. Phase 6.</summary>
    ProductionOutput,

    /// <summary>Stock removed as damaged, expired or lost. Phase 6.</summary>
    WriteOff
}
