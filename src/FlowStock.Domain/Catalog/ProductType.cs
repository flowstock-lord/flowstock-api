namespace FlowStock.Domain.Catalog;

/// <summary>
/// What a product is used for. See docs/PLAN.md, section 6. Persisted by name, so the stored
/// values stay readable and reordering the enum can never reinterpret existing rows.
/// </summary>
public enum ProductType
{
    RawMaterial,
    Packaging,
    FinishedProduct,
    SemiFinishedProduct,
    Other
}
