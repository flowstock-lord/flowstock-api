namespace FlowStock.Domain.Warehouses;

/// <summary>
/// What a warehouse holds. See docs/PLAN.md, section 8. Persisted by name, so the stored values
/// stay readable and reordering the enum can never reinterpret existing rows.
/// </summary>
public enum WarehouseType
{
    RawMaterials,
    Production,
    FinishedGoods,
    General
}
