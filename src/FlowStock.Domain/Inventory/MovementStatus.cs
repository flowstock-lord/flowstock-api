namespace FlowStock.Domain.Inventory;

/// <summary>
/// See docs/PLAN.md, section 13. Only <see cref="Confirmed"/> movements affect stock, and a
/// confirmed movement is never edited — corrections are compensating movements.
/// </summary>
public enum MovementStatus
{
    Draft,
    Confirmed,
    Cancelled
}
