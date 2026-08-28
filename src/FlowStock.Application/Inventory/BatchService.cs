using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Inventory;

public interface IBatchService
{
    Task<PagedResult<BatchResponse>> ListAsync(BatchQuery query, CancellationToken cancellationToken);

    Task<BatchResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<BatchResponse> CreateAsync(CreateBatchRequest request, CancellationToken cancellationToken);

    Task<BatchResponse> UpdateAsync(Guid id, UpdateBatchRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Registers and reads lots (docs/PLAN.md, section 20). It never touches stock: a batch identifies
/// goods, and the quantity of those goods is a balance that only a confirmed movement can change
/// (CLAUDE.md, rule 1).
/// </summary>
public class BatchService(
    IFlowStockDbContext db,
    TimeProvider timeProvider,
    ILogger<BatchService> logger) : IBatchService
{
    public async Task<PagedResult<BatchResponse>> ListAsync(BatchQuery query, CancellationToken cancellationToken)
    {
        var batches = Include(db.Batches);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            batches = batches.Where(b => b.Number.ToLower().Contains(search));
        }

        if (query.ProductId is not null)
        {
            batches = batches.Where(b => b.ProductId == query.ProductId);
        }

        if (!string.IsNullOrWhiteSpace(query.Supplier))
        {
            var supplier = query.Supplier.Trim().ToLowerInvariant();
            batches = batches.Where(b => b.Supplier != null && b.Supplier.ToLower().Contains(supplier));
        }

        if (query.ProductionOrderId is not null)
        {
            batches = batches.Where(b => b.ProductionOrderId == query.ProductionOrderId);
        }

        if (query.ExpiringBefore is { } before)
        {
            batches = batches.Where(b => b.ExpiryDate != null && b.ExpiryDate < before);
        }

        var today = Today();

        if (query.IsExpired is { } expired)
        {
            batches = expired
                ? batches.Where(b => b.ExpiryDate != null && b.ExpiryDate < today)
                : batches.Where(b => b.ExpiryDate == null || b.ExpiryDate >= today);
        }

        var totalCount = await batches.CountAsync(cancellationToken);

        var items = await Sort(batches, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<BatchResponse>(
            items.Select(batch => ToResponse(batch, today)).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<BatchResponse> GetAsync(Guid id, CancellationToken cancellationToken)
        => ToResponse(await FindAsync(id, cancellationToken), Today());

    public async Task<BatchResponse> CreateAsync(CreateBatchRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products
                          .Include(p => p.UnitOfMeasure)
                          .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
                      ?? throw new ProductNotFoundException(request.ProductId);

        RequireBatchTracked(product);

        var number = Batch.NormalizeNumber(request.Number);

        if (await db.Batches.AnyAsync(b => b.ProductId == product.Id && b.Number == number, cancellationToken))
        {
            throw new BatchNumberAlreadyExistsException(product.Id, number);
        }

        RequireOrderedDates(request.ProductionDate, request.ExpiryDate);

        var batch = new Batch
        {
            ProductId = product.Id,
            Product = product,
            Number = number,
            Supplier = Trimmed(request.Supplier),
            ProductionDate = request.ProductionDate,
            ExpiryDate = request.ExpiryDate,
            Notes = Trimmed(request.Notes)
        };

        db.Batches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Batch {Number} registered for {Sku}, supplier {Supplier}, expiry {ExpiryDate}",
            batch.Number, product.Sku, batch.Supplier, batch.ExpiryDate);

        return ToResponse(batch, Today());
    }

    public async Task<BatchResponse> UpdateAsync(
        Guid id,
        UpdateBatchRequest request,
        CancellationToken cancellationToken)
    {
        var batch = await FindAsync(id, cancellationToken);

        RequireOrderedDates(request.ProductionDate, request.ExpiryDate);

        batch.Supplier = Trimmed(request.Supplier);
        batch.ProductionDate = request.ProductionDate;
        batch.ExpiryDate = request.ExpiryDate;
        batch.Notes = Trimmed(request.Notes);

        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(batch, Today());
    }

    /// <summary>A lot exists to answer "which goods are these", which only a tracked product asks.</summary>
    private static void RequireBatchTracked(Product product)
    {
        if (!product.IsBatchTracked)
        {
            throw new BatchNotAllowedException(product.Id, product.Sku);
        }
    }

    private static void RequireOrderedDates(DateOnly? producedOn, DateOnly? expiresOn)
    {
        if (producedOn is { } produced && expiresOn is { } expires && expires < produced)
        {
            throw new BatchInvalidException(
                "A batch cannot expire before it was produced.",
                new Dictionary<string, object?>
                {
                    ["productionDate"] = produced,
                    ["expiryDate"] = expires
                });
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<Batch> FindAsync(Guid id, CancellationToken cancellationToken)
        => await Include(db.Batches).FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
           ?? throw new BatchNotFoundException(id);

    private static IQueryable<Batch> Include(IQueryable<Batch> batches) => batches
        .Include(b => b.Product).ThenInclude(p => p.UnitOfMeasure);

    private static IQueryable<Batch> Sort(IQueryable<Batch> batches, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        var field = (descending ? sort![1..] : sort)?.Trim().ToLowerInvariant();

        return (field, descending) switch
        {
            ("number", false) => batches.OrderBy(b => b.Number),
            ("number", true) => batches.OrderByDescending(b => b.Number),
            ("createdat", false) => batches.OrderBy(b => b.CreatedAt),
            ("createdat", true) => batches.OrderByDescending(b => b.CreatedAt),
            ("expirydate", true) => batches.OrderByDescending(b => b.ExpiryDate).ThenBy(b => b.Number),
            // Soonest expiry first: the order the shelf should be emptied in.
            _ => batches.OrderBy(b => b.ExpiryDate).ThenBy(b => b.Number)
        };
    }

    private static BatchResponse ToResponse(Batch batch, DateOnly today) => new(
        batch.Id,
        batch.ProductId,
        batch.Product.Sku,
        batch.Product.Name,
        batch.Product.UnitOfMeasure.Code,
        batch.Number,
        batch.Supplier,
        batch.ProductionDate,
        batch.ExpiryDate,
        batch.IsExpiredOn(today),
        batch.ProductionOrderId,
        batch.Notes,
        batch.CreatedAt,
        batch.CreatedBy,
        batch.UpdatedAt);
}
