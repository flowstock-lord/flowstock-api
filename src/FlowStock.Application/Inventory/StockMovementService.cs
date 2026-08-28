using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Inventory;

public interface IStockMovementService
{
    Task<PagedResult<StockMovementResponse>> ListAsync(
        StockMovementQuery query,
        CancellationToken cancellationToken);

    Task<StockMovementResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<StockMovementResponse> CreateAsync(
        CreateStockMovementRequest request,
        CancellationToken cancellationToken);

    Task<StockMovementResponse> ConfirmAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Posts a movement that a production order owns: created and confirmed in one step, stamped
    /// with the order it belongs to, and joined to the caller's transaction so the whole
    /// production operation stays one unit of work.
    ///
    /// Consumption and production output never exist as drafts — the order's own status is the
    /// workflow, which is why they cannot be created through <see cref="CreateAsync"/>.
    /// </summary>
    Task<StockMovementResponse> PostForProductionOrderAsync(
        CreateStockMovementRequest request,
        Guid productionOrderId,
        CancellationToken cancellationToken);

    Task<StockMovementResponse> CancelAsync(
        Guid id,
        CancelStockMovementRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// The only way stock ever changes. A movement is created as a draft, and confirming it applies
/// the whole document — every source decrease, every destination increase and the document's own
/// status — inside one transaction (docs/PLAN.md, sections 3.3, 3.5, 13 and 28).
/// </summary>
public class StockMovementService(
    IFlowStockDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<StockMovementService> logger) : IStockMovementService
{
    public async Task<PagedResult<StockMovementResponse>> ListAsync(
        StockMovementQuery query,
        CancellationToken cancellationToken)
    {
        var movements = Include(db.StockMovements);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            movements = movements.Where(m => m.Number.ToLower().Contains(search));
        }

        if (query.MovementType is not null)
        {
            movements = movements.Where(m => m.MovementType == query.MovementType);
        }

        if (query.Status is not null)
        {
            movements = movements.Where(m => m.Status == query.Status);
        }

        if (query.ProductId is not null)
        {
            movements = movements.Where(m => m.Lines.Any(l => l.ProductId == query.ProductId));
        }

        if (query.LocationId is not null)
        {
            movements = movements.Where(m =>
                m.SourceLocationId == query.LocationId || m.DestinationLocationId == query.LocationId);
        }

        if (query.BatchId is not null)
        {
            movements = movements.Where(m => m.Lines.Any(l => l.BatchId == query.BatchId));
        }

        if (query.ProductionOrderId is not null)
        {
            movements = movements.Where(m => m.ProductionOrderId == query.ProductionOrderId);
        }

        if (query.From is not null)
        {
            movements = movements.Where(m => m.CreatedAt >= query.From);
        }

        if (query.To is not null)
        {
            movements = movements.Where(m => m.CreatedAt < query.To);
        }

        var totalCount = await movements.CountAsync(cancellationToken);

        var items = await Sort(movements, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<StockMovementResponse>(
            items.Select(ToResponse).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<StockMovementResponse> GetAsync(Guid id, CancellationToken cancellationToken)
        => ToResponse(await FindAsync(id, cancellationToken));

    public async Task<StockMovementResponse> CreateAsync(
        CreateStockMovementRequest request,
        CancellationToken cancellationToken)
        => ToResponse(await FindAsync(
            (await CreateDraftAsync(request, productionOrderId: null, cancellationToken)).Id,
            cancellationToken));

    public async Task<StockMovementResponse> PostForProductionOrderAsync(
        CreateStockMovementRequest request,
        Guid productionOrderId,
        CancellationToken cancellationToken)
    {
        var movement = await CreateDraftAsync(request, productionOrderId, cancellationToken);

        return await ConfirmAsync(movement.Id, cancellationToken);
    }

    public async Task<StockMovementResponse> ConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        // Everything below — the balance changes and the document's own status — is one unit of
        // work: it all happens or none of it does (CLAUDE.md, rule 3).
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var movement = await FindAsync(id, cancellationToken);

        RequireDraft(movement);
        RequireActiveEndpoints(movement);

        var balances = await LockBalancesAsync(movement, cancellationToken);

        foreach (var line in movement.Lines)
        {
            if (movement.SourceLocationId is { } sourceId)
            {
                var source = balances[new StockKey(line.ProductId, sourceId, line.BatchId)];

                // Checked before the subtraction so the error can report what was actually there.
                if (source.AvailableQuantity < line.Quantity)
                {
                    throw new InsufficientStockException(
                        line.ProductId,
                        line.Product.Sku,
                        sourceId,
                        movement.SourceLocation!.Code,
                        line.Quantity,
                        source.AvailableQuantity,
                        line.BatchId,
                        line.Batch?.Number);
                }

                source.Quantity -= line.Quantity;
            }

            if (movement.DestinationLocationId is { } destinationId)
            {
                balances[new StockKey(line.ProductId, destinationId, line.BatchId)].Quantity += line.Quantity;
            }
        }

        movement.Status = MovementStatus.Confirmed;
        movement.ConfirmedAt = timeProvider.GetUtcNow().UtcDateTime;
        movement.ConfirmedBy = currentUser.UserId;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Stock movement {Number} confirmed by {UserId}: {MovementType} of {LineCount} line(s) " +
            "from {SourceLocationId} to {DestinationLocationId}",
            movement.Number, movement.ConfirmedBy, movement.MovementType, movement.Lines.Count,
            movement.SourceLocationId, movement.DestinationLocationId);

        return ToResponse(movement);
    }

    public async Task<StockMovementResponse> CancelAsync(
        Guid id,
        CancelStockMovementRequest request,
        CancellationToken cancellationToken)
    {
        var movement = await FindAsync(id, cancellationToken);

        // A confirmed movement is history. Correcting it means a compensating movement, never a
        // cancellation (docs/PLAN.md, section 13).
        RequireDraft(movement);

        movement.Status = MovementStatus.Cancelled;
        movement.CancelledAt = timeProvider.GetUtcNow().UtcDateTime;
        movement.CancelledBy = currentUser.UserId;

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            movement.Reason = request.Reason.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Stock movement {Number} cancelled by {UserId}",
            movement.Number, movement.CancelledBy);

        return ToResponse(movement);
    }

    private async Task<StockMovement> CreateDraftAsync(
        CreateStockMovementRequest request,
        Guid? productionOrderId,
        CancellationToken cancellationToken)
    {
        StockMovement.ValidateEndpoints(request.MovementType, request.SourceLocationId, request.DestinationLocationId);

        var source = await ResolveLocationAsync(request.SourceLocationId, cancellationToken);
        var destination = await ResolveLocationAsync(request.DestinationLocationId, cancellationToken);

        var products = await ResolveProductsAsync(request.Lines, cancellationToken);
        await ValidateBatchesAsync(request.Lines, products, cancellationToken);

        var movement = new StockMovement
        {
            Number = await NextNumberAsync(cancellationToken),
            MovementType = request.MovementType,
            Status = MovementStatus.Draft,
            SourceLocationId = source?.Id,
            DestinationLocationId = destination?.Id,
            ProductionOrderId = productionOrderId,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            Lines = request.Lines.Select(line => new StockMovementLine
            {
                ProductId = line.ProductId,
                // The unit follows the product, so a quantity can never be recorded in another one.
                UnitOfMeasureId = products[line.ProductId].UnitOfMeasureId,
                BatchId = line.BatchId,
                Quantity = line.Quantity
            }).ToList()
        };

        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Stock movement {Number} created as {MovementType} draft with {LineCount} line(s) " +
            "from {SourceLocationId} to {DestinationLocationId}",
            movement.Number, movement.MovementType, movement.Lines.Count,
            movement.SourceLocationId, movement.DestinationLocationId);

        return movement;
    }

    /// <summary>
    /// Loads every balance the document will touch, locked for the rest of the transaction, so a
    /// second confirmation of the same stock waits instead of reading a stale quantity.
    /// </summary>
    private async Task<Dictionary<StockKey, Stock>> LockBalancesAsync(
        StockMovement movement,
        CancellationToken cancellationToken)
    {
        var keys = new HashSet<StockKey>();

        foreach (var line in movement.Lines)
        {
            if (movement.SourceLocationId is { } sourceId)
            {
                keys.Add(new StockKey(line.ProductId, sourceId, line.BatchId));
            }

            if (movement.DestinationLocationId is { } destinationId)
            {
                keys.Add(new StockKey(line.ProductId, destinationId, line.BatchId));
            }
        }

        var stocks = await db.LockStockAsync(keys, cancellationToken);

        return stocks.ToDictionary(s => new StockKey(s.ProductId, s.LocationId, s.BatchId));
    }

    private static void RequireDraft(StockMovement movement)
    {
        switch (movement.Status)
        {
            case MovementStatus.Confirmed:
                throw new MovementAlreadyConfirmedException(movement.Id, movement.Number);
            case MovementStatus.Cancelled:
                throw new MovementAlreadyCancelledException(movement.Id, movement.Number);
        }
    }

    /// <summary>A location may have been deactivated between the draft and the confirmation.</summary>
    private static void RequireActiveEndpoints(StockMovement movement)
    {
        foreach (var location in new[] { movement.SourceLocation, movement.DestinationLocation })
        {
            if (location is { IsActive: false })
            {
                throw new LocationInactiveException(location.Id, location.Code);
            }
        }
    }

    private async Task<StorageLocation?> ResolveLocationAsync(Guid? locationId, CancellationToken cancellationToken)
    {
        if (locationId is not { } id)
        {
            return null;
        }

        var location = await db.StorageLocations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                       ?? throw new LocationNotFoundException(id);

        if (!location.IsActive)
        {
            throw new LocationInactiveException(location.Id, location.Code);
        }

        return location;
    }

    /// <summary>
    /// A deactivated product is still allowed to move: stock that already exists has to be able to
    /// leave the warehouse. What is rejected is a product that does not exist, and the same product
    /// and lot twice in one document, which would leave the intended quantity ambiguous. The same
    /// product in two different lots is not a duplicate: it is how two lots are taken at once.
    /// </summary>
    private async Task<Dictionary<Guid, Product>> ResolveProductsAsync(
        IReadOnlyList<CreateStockMovementLineRequest> lines,
        CancellationToken cancellationToken)
    {
        var productIds = lines.Select(l => l.ProductId).ToList();

        var duplicate = lines
            .GroupBy(l => (l.ProductId, l.BatchId))
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidMovementException(
                "A product may appear only once per batch in a movement; add its quantities together.",
                new Dictionary<string, object?>
                {
                    ["productId"] = duplicate.Key.ProductId,
                    ["batchId"] = duplicate.Key.BatchId
                });
        }

        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var missing = productIds.FirstOrDefault(id => !products.ContainsKey(id));

        return missing == Guid.Empty ? products : throw new ProductNotFoundException(missing);
    }

    /// <summary>
    /// Checks the lots the document names against the products that own them: a batch-tracked
    /// product must name one, anything else must not, and a lot always belongs to its own product
    /// (docs/PLAN.md, section 20).
    /// </summary>
    private async Task ValidateBatchesAsync(
        IReadOnlyList<CreateStockMovementLineRequest> lines,
        IReadOnlyDictionary<Guid, Product> products,
        CancellationToken cancellationToken)
    {
        var batchIds = lines.Select(l => l.BatchId).OfType<Guid>().Distinct().ToList();

        var batches = batchIds.Count == 0
            ? new Dictionary<Guid, Batch>()
            : await db.Batches
                .Where(b => batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, cancellationToken);

        foreach (var line in lines)
        {
            var product = products[line.ProductId];

            if (line.BatchId is not { } batchId)
            {
                if (product.IsBatchTracked)
                {
                    throw new BatchRequiredException(product.Id, product.Sku);
                }

                continue;
            }

            if (!product.IsBatchTracked)
            {
                throw new BatchNotAllowedException(product.Id, product.Sku);
            }

            var batch = batches.GetValueOrDefault(batchId) ?? throw new BatchNotFoundException(batchId);

            if (batch.ProductId != product.Id)
            {
                throw new BatchInvalidException(
                    $"Batch {batch.Number} does not belong to product {product.Sku}.",
                    new Dictionary<string, object?>
                    {
                        ["batchId"] = batch.Id,
                        ["number"] = batch.Number,
                        ["productId"] = product.Id,
                        ["sku"] = product.Sku
                    });
            }
        }
    }

    private async Task<string> NextNumberAsync(CancellationToken cancellationToken)
        => $"MOV-{await db.NextMovementNumberAsync(cancellationToken):D6}";

    private async Task<StockMovement> FindAsync(Guid id, CancellationToken cancellationToken)
        => await Include(db.StockMovements).FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
           ?? throw new MovementNotFoundException(id);

    private static IQueryable<StockMovement> Include(IQueryable<StockMovement> movements) => movements
        .Include(m => m.SourceLocation)
        .Include(m => m.DestinationLocation)
        .Include(m => m.Lines).ThenInclude(l => l.Product)
        .Include(m => m.Lines).ThenInclude(l => l.UnitOfMeasure)
        .Include(m => m.Lines).ThenInclude(l => l.Batch);

    private static IQueryable<StockMovement> Sort(IQueryable<StockMovement> movements, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        var field = (descending ? sort![1..] : sort)?.Trim().ToLowerInvariant();

        return (field, descending) switch
        {
            ("number", false) => movements.OrderBy(m => m.Number),
            ("number", true) => movements.OrderByDescending(m => m.Number),
            ("createdat", false) => movements.OrderBy(m => m.CreatedAt).ThenBy(m => m.Number),
            // Newest first: the default a movement journal is read in.
            _ => movements.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Number)
        };
    }

    private static StockMovementResponse ToResponse(StockMovement movement) => new(
        movement.Id,
        movement.Number,
        movement.MovementType,
        movement.Status,
        movement.ProductionOrderId,
        movement.SourceLocationId,
        movement.SourceLocation?.Code,
        movement.DestinationLocationId,
        movement.DestinationLocation?.Code,
        movement.Reason,
        movement.Lines
            .OrderBy(l => l.Product.Sku)
            .ThenBy(l => l.Batch?.Number)
            .Select(l => new StockMovementLineResponse(
                l.Id,
                l.ProductId,
                l.Product.Sku,
                l.Product.Name,
                l.BatchId,
                l.Batch?.Number,
                l.Quantity,
                l.UnitOfMeasureId,
                l.UnitOfMeasure.Code))
            .ToList(),
        movement.CreatedAt,
        movement.CreatedBy,
        movement.ConfirmedAt,
        movement.ConfirmedBy,
        movement.CancelledAt,
        movement.CancelledBy);
}
