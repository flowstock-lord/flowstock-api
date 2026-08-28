using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Catalog;

public interface IProductService
{
    Task<PagedResult<ProductResponse>> ListAsync(ProductQuery query, CancellationToken cancellationToken);

    Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);

    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken);

    Task<ProductResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
}

public class ProductService(
    IFlowStockDbContext db,
    ILogger<ProductService> logger) : IProductService
{
    public async Task<PagedResult<ProductResponse>> ListAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        var products = db.Products
            .Include(p => p.UnitOfMeasure)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            products = products.Where(p =>
                p.Sku.ToLower().Contains(search) ||
                p.Name.ToLower().Contains(search));
        }

        if (query.ProductType is not null)
        {
            products = products.Where(p => p.ProductType == query.ProductType);
        }

        if (query.UnitOfMeasureId is not null)
        {
            products = products.Where(p => p.UnitOfMeasureId == query.UnitOfMeasureId);
        }

        if (query.IsBatchTracked is not null)
        {
            products = products.Where(p => p.IsBatchTracked == query.IsBatchTracked);
        }

        if (query.IsActive is not null)
        {
            products = products.Where(p => p.IsActive == query.IsActive);
        }

        var totalCount = await products.CountAsync(cancellationToken);

        var items = await Sort(products, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductResponse>(
            items.Select(ToResponse).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken)
        => ToResponse(await FindAsync(id, cancellationToken));

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var sku = Product.NormalizeSku(request.Sku);

        if (await db.Products.AnyAsync(p => p.Sku == sku, cancellationToken))
        {
            throw new SkuAlreadyExistsException(sku);
        }

        var unit = await ResolveUnitAsync(request.UnitOfMeasureId, cancellationToken);

        var product = new Product
        {
            Sku = sku,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ProductType = request.ProductType,
            UnitOfMeasureId = unit.Id,
            UnitOfMeasure = unit,
            IsBatchTracked = request.IsBatchTracked,
            IsActive = true
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Product {ProductId} created with SKU {Sku} measured in {Code}",
            product.Id, product.Sku, unit.Code);

        return ToResponse(product);
    }

    public async Task<ProductResponse> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await FindAsync(id, cancellationToken);

        if (product.UnitOfMeasureId != request.UnitOfMeasureId)
        {
            var unit = await ResolveUnitAsync(request.UnitOfMeasureId, cancellationToken);
            product.UnitOfMeasureId = unit.Id;
            product.UnitOfMeasure = unit;
        }

        product.Name = request.Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        product.ProductType = request.ProductType;

        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(product);
    }

    public async Task<ProductResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var product = await FindAsync(id, cancellationToken);

        product.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Product {ProductId} active flag set to {IsActive}", product.Id, isActive);

        return ToResponse(product);
    }

    private async Task<Product> FindAsync(Guid id, CancellationToken cancellationToken)
        => await db.Products
               .Include(p => p.UnitOfMeasure)
               .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
           ?? throw new ProductNotFoundException(id);

    private async Task<UnitOfMeasure> ResolveUnitAsync(Guid unitOfMeasureId, CancellationToken cancellationToken)
    {
        var unit = await db.UnitsOfMeasure.FirstOrDefaultAsync(u => u.Id == unitOfMeasureId, cancellationToken)
                   ?? throw new UnitOfMeasureNotFoundException(unitOfMeasureId);

        if (!unit.IsActive)
        {
            throw new UnitOfMeasureInactiveException(unit.Id, unit.Code);
        }

        return unit;
    }

    private static IQueryable<Product> Sort(IQueryable<Product> products, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        var field = (descending ? sort![1..] : sort)?.Trim().ToLowerInvariant();

        return (field, descending) switch
        {
            ("name", false) => products.OrderBy(p => p.Name),
            ("name", true) => products.OrderByDescending(p => p.Name),
            ("type", false) => products.OrderBy(p => p.ProductType).ThenBy(p => p.Sku),
            ("type", true) => products.OrderByDescending(p => p.ProductType).ThenBy(p => p.Sku),
            ("createdat", false) => products.OrderBy(p => p.CreatedAt),
            ("createdat", true) => products.OrderByDescending(p => p.CreatedAt),
            (_, true) => products.OrderByDescending(p => p.Sku),
            _ => products.OrderBy(p => p.Sku)
        };
    }

    private static ProductResponse ToResponse(Product product) => new(
        product.Id,
        product.Sku,
        product.Name,
        product.Description,
        product.ProductType,
        product.UnitOfMeasureId,
        product.UnitOfMeasure.Code,
        product.IsBatchTracked,
        product.IsActive,
        product.CreatedAt,
        product.UpdatedAt);
}
