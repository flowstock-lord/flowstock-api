using FlowStock.Domain.Catalog;
using FlowStock.Domain.Common;

namespace FlowStock.Domain.Production;

/// <summary>
/// A recipe: what it takes to produce a given quantity of a product (docs/PLAN.md, section 14).
///
/// A version is immutable once created. Changing a recipe means adding a version, never editing
/// a published one — a production order records the <c>BillOfMaterialId</c> it was built from, so
/// a completed order must still be able to show the recipe it actually used.
/// </summary>
public class BillOfMaterial : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The product this recipe produces.</summary>
    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>1 for the first recipe of a product, then 2, 3... Assigned by the system.</summary>
    public int Version { get; set; }

    /// <summary>
    /// How much of the product one run of this recipe yields — the "Cookie / 100 pcs" of
    /// section 14. Without it the item quantities would have no scale to be read against.
    /// </summary>
    public decimal OutputQuantity { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>At most one version of a product's recipe is active at a time: the current one.</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<BillOfMaterialItem> Items { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// How much of every component one run of <paramref name="quantity"/> products needs.
    /// Scaled from the recipe's own output quantity, and rounded to the 4 decimals that
    /// quantities are stored at.
    /// </summary>
    public decimal RequiredQuantityFor(decimal componentQuantity, decimal quantity)
    {
        if (OutputQuantity <= 0)
        {
            throw new BomInvalidException("A bill of materials must produce a positive quantity.");
        }

        return Math.Round(componentQuantity * quantity / OutputQuantity, 4, MidpointRounding.AwayFromZero);
    }
}
