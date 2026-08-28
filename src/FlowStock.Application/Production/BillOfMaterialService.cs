using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Production;

public interface IBillOfMaterialService
{
    Task<PagedResult<BillOfMaterialResponse>> ListAsync(
        BillOfMaterialQuery query,
        CancellationToken cancellationToken);

    Task<BillOfMaterialResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<BillOfMaterialResponse> CreateAsync(
        CreateBillOfMaterialRequest request,
        CancellationToken cancellationToken);

    Task<BillOfMaterialResponse> UpdateAsync(
        Guid id,
        UpdateBillOfMaterialRequest request,
        CancellationToken cancellationToken);

    Task<BillOfMaterialResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);

    Task<MaterialRequirementsResponse> CalculateRequirementsAsync(
        Guid id,
        decimal quantity,
        CancellationToken cancellationToken);
}

/// <summary>
/// Manages recipes and answers what producing a given quantity would consume
/// (docs/PLAN.md, section 14). It only reads and writes recipes — it never touches stock.
/// </summary>
public class BillOfMaterialService(
    IFlowStockDbContext db,
    ILogger<BillOfMaterialService> logger) : IBillOfMaterialService
{
    public async Task<PagedResult<BillOfMaterialResponse>> ListAsync(
        BillOfMaterialQuery query,
        CancellationToken cancellationToken)
    {
        var boms = Include(db.BillsOfMaterial);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            boms = boms.Where(b =>
                b.Product.Sku.ToLower().Contains(search) ||
                b.Product.Name.ToLower().Contains(search));
        }

        if (query.ProductId is not null)
        {
            boms = boms.Where(b => b.ProductId == query.ProductId);
        }

        if (query.IsActive is not null)
        {
            boms = boms.Where(b => b.IsActive == query.IsActive);
        }

        var totalCount = await boms.CountAsync(cancellationToken);

        var items = await Sort(boms, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<BillOfMaterialResponse>(
            items.Select(ToResponse).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<BillOfMaterialResponse> GetAsync(Guid id, CancellationToken cancellationToken)
        => ToResponse(await FindAsync(id, cancellationToken));

    public async Task<BillOfMaterialResponse> CreateAsync(
        CreateBillOfMaterialRequest request,
        CancellationToken cancellationToken)
    {
        var product = await db.Products
                          .Include(p => p.UnitOfMeasure)
                          .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
                      ?? throw new ProductNotFoundException(request.ProductId);

        var components = await ResolveComponentsAsync(product, request.Items, cancellationToken);

        // Publishing a new version replaces the current one: a product has exactly one recipe in
        // force at a time, and the older versions stay readable for the orders that used them.
        // Standing the old version down is saved first and separately — a filtered unique index
        // enforces "one active version", and EF is free to order an insert before an update
        // within one save, which would trip the index on the intermediate state.
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var existing = await db.BillsOfMaterial
            .Where(b => b.ProductId == product.Id)
            .ToListAsync(cancellationToken);

        await DeactivateAsync(existing.Where(b => b.IsActive), cancellationToken);

        var bom = new BillOfMaterial
        {
            ProductId = product.Id,
            Version = existing.Count == 0 ? 1 : existing.Max(b => b.Version) + 1,
            OutputQuantity = request.OutputQuantity,
            Name = Trimmed(request.Name),
            Description = Trimmed(request.Description),
            IsActive = true,
            Items = request.Items.Select(item => new BillOfMaterialItem
            {
                ComponentProductId = item.ComponentProductId,
                UnitOfMeasureId = components[item.ComponentProductId].UnitOfMeasureId,
                Quantity = item.Quantity
            }).ToList()
        };

        db.BillsOfMaterial.Add(bom);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Bill of materials for {Sku} published as version {Version} with {ItemCount} component(s)",
            product.Sku, bom.Version, bom.Items.Count);

        return ToResponse(await FindAsync(bom.Id, cancellationToken));
    }

    public async Task<BillOfMaterialResponse> UpdateAsync(
        Guid id,
        UpdateBillOfMaterialRequest request,
        CancellationToken cancellationToken)
    {
        var bom = await FindAsync(id, cancellationToken);

        bom.Name = Trimmed(request.Name);
        bom.Description = Trimmed(request.Description);

        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(bom);
    }

    public async Task<BillOfMaterialResponse> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var bom = await FindAsync(id, cancellationToken);

        if (isActive && !bom.IsActive)
        {
            // Only one version of a product's recipe may be in force, so activating this one
            // stands the current version down — saved first, for the same reason as in CreateAsync.
            var current = await db.BillsOfMaterial
                .Where(b => b.ProductId == bom.ProductId && b.IsActive && b.Id != bom.Id)
                .ToListAsync(cancellationToken);

            await DeactivateAsync(current, cancellationToken);
        }

        bom.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Bill of materials {BomId} (version {Version}) active flag set to {IsActive}",
            bom.Id, bom.Version, isActive);

        return ToResponse(bom);
    }

    private async Task DeactivateAsync(IEnumerable<BillOfMaterial> boms, CancellationToken cancellationToken)
    {
        var superseded = boms.ToList();

        if (superseded.Count == 0)
        {
            return;
        }

        foreach (var bom in superseded)
        {
            bom.IsActive = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MaterialRequirementsResponse> CalculateRequirementsAsync(
        Guid id,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var bom = await FindAsync(id, cancellationToken);

        return new MaterialRequirementsResponse(
            bom.Id,
            bom.Version,
            bom.ProductId,
            bom.Product.Sku,
            bom.Product.Name,
            quantity,
            bom.Product.UnitOfMeasure.Code,
            bom.OutputQuantity,
            bom.Items
                .OrderBy(item => item.ComponentProduct.Sku)
                .Select(item => new MaterialRequirementResponse(
                    item.ComponentProductId,
                    item.ComponentProduct.Sku,
                    item.ComponentProduct.Name,
                    item.Quantity,
                    bom.RequiredQuantityFor(item.Quantity, quantity),
                    item.UnitOfMeasure.Code))
                .ToList());
    }

    /// <summary>
    /// A recipe needs components that exist, each named once, and none of them the product it
    /// produces — a product that consumed itself could never be resolved into materials.
    /// </summary>
    private async Task<Dictionary<Guid, Product>> ResolveComponentsAsync(
        Product product,
        IReadOnlyList<CreateBillOfMaterialItemRequest> items,
        CancellationToken cancellationToken)
    {
        var componentIds = items.Select(item => item.ComponentProductId).ToList();

        var duplicate = componentIds.GroupBy(componentId => componentId).FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new BomInvalidException(
                "A component may appear only once in a bill of materials; add its quantities together.",
                new Dictionary<string, object?> { ["componentProductId"] = duplicate.Key });
        }

        if (componentIds.Contains(product.Id))
        {
            throw new BomInvalidException(
                $"Product {product.Sku} cannot be a component of its own bill of materials.",
                new Dictionary<string, object?> { ["productId"] = product.Id, ["sku"] = product.Sku });
        }

        var components = await db.Products
            .Where(p => componentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var missing = componentIds.FirstOrDefault(componentId => !components.ContainsKey(componentId));

        return missing == Guid.Empty ? components : throw new ProductNotFoundException(missing);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<BillOfMaterial> FindAsync(Guid id, CancellationToken cancellationToken)
        => await Include(db.BillsOfMaterial).FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
           ?? throw new BomNotFoundException(id);

    private static IQueryable<BillOfMaterial> Include(IQueryable<BillOfMaterial> boms) => boms
        .Include(b => b.Product).ThenInclude(p => p.UnitOfMeasure)
        .Include(b => b.Items).ThenInclude(i => i.ComponentProduct)
        .Include(b => b.Items).ThenInclude(i => i.UnitOfMeasure);

    private static IQueryable<BillOfMaterial> Sort(IQueryable<BillOfMaterial> boms, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        var field = (descending ? sort![1..] : sort)?.Trim().ToLowerInvariant();

        return (field, descending) switch
        {
            ("version", false) => boms.OrderBy(b => b.Product.Sku).ThenBy(b => b.Version),
            ("version", true) => boms.OrderBy(b => b.Product.Sku).ThenByDescending(b => b.Version),
            ("createdat", false) => boms.OrderBy(b => b.CreatedAt),
            ("createdat", true) => boms.OrderByDescending(b => b.CreatedAt),
            (_, true) => boms.OrderByDescending(b => b.Product.Sku).ThenByDescending(b => b.Version),
            // Newest version of each product first: the one in force is the one people look for.
            _ => boms.OrderBy(b => b.Product.Sku).ThenByDescending(b => b.Version)
        };
    }

    private static BillOfMaterialResponse ToResponse(BillOfMaterial bom) => new(
        bom.Id,
        bom.ProductId,
        bom.Product.Sku,
        bom.Product.Name,
        bom.Version,
        bom.OutputQuantity,
        bom.Product.UnitOfMeasure.Code,
        bom.Name,
        bom.Description,
        bom.IsActive,
        bom.Items
            .OrderBy(item => item.ComponentProduct.Sku)
            .Select(item => new BillOfMaterialItemResponse(
                item.Id,
                item.ComponentProductId,
                item.ComponentProduct.Sku,
                item.ComponentProduct.Name,
                item.Quantity,
                item.UnitOfMeasureId,
                item.UnitOfMeasure.Code))
            .ToList(),
        bom.CreatedAt,
        bom.CreatedBy,
        bom.UpdatedAt);
}
