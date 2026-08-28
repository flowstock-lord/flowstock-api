using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;
using FlowStock.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Production;

public interface IProductionOrderService
{
    Task<PagedResult<ProductionOrderResponse>> ListAsync(
        ProductionOrderQuery query,
        CancellationToken cancellationToken);

    Task<ProductionOrderResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductionOrderResponse> CreateAsync(
        CreateProductionOrderRequest request,
        CancellationToken cancellationToken);

    Task<ProductionOrderResponse> PlanAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductionOrderResponse> StartAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductionOrderResponse> CompleteAsync(
        Guid id,
        CompleteProductionOrderRequest request,
        CancellationToken cancellationToken);

    Task<ProductionOrderResponse> CancelAsync(
        Guid id,
        CancelProductionOrderRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Runs the production workflow of docs/PLAN.md, section 18:
/// <c>Draft → Planned → InProgress → Completed</c>.
///
/// The order is a document, not a way to edit stock. Planning reserves material on the shop floor,
/// starting consumes it and completing books the finished goods in — and every one of those stock
/// changes is a confirmed stock movement posted through <see cref="IStockMovementService"/>,
/// carrying this order's id (CLAUDE.md, rule 1, and docs/PLAN.md, sections 16, 17 and 19).
/// </summary>
public class ProductionOrderService(
    IFlowStockDbContext db,
    IStockMovementService stockMovements,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<ProductionOrderService> logger) : IProductionOrderService
{
    public async Task<PagedResult<ProductionOrderResponse>> ListAsync(
        ProductionOrderQuery query,
        CancellationToken cancellationToken)
    {
        var orders = Include(db.ProductionOrders);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            orders = orders.Where(o => o.Number.ToLower().Contains(search));
        }

        if (query.Status is not null)
        {
            orders = orders.Where(o => o.Status == query.Status);
        }

        if (query.ProductId is not null)
        {
            orders = orders.Where(o => o.ProductId == query.ProductId);
        }

        // Forward traceability: which runs a given material went into (docs/PLAN.md, section 19).
        if (query.ComponentProductId is not null)
        {
            orders = orders.Where(o => o.Materials.Any(m => m.ComponentProductId == query.ComponentProductId));
        }

        if (query.LocationId is not null)
        {
            orders = orders.Where(o =>
                o.ProductionLocationId == query.LocationId || o.OutputLocationId == query.LocationId);
        }

        if (query.From is not null)
        {
            orders = orders.Where(o => o.CreatedAt >= query.From);
        }

        if (query.To is not null)
        {
            orders = orders.Where(o => o.CreatedAt < query.To);
        }

        var totalCount = await orders.CountAsync(cancellationToken);

        var items = await Sort(orders, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductionOrderResponse>(
            items.Select(ToResponse).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<ProductionOrderResponse> GetAsync(Guid id, CancellationToken cancellationToken)
        => ToResponse(await FindAsync(id, cancellationToken));

    /// <summary>
    /// Writes the order down and freezes what it undertakes to consume. The materials are scaled
    /// from the recipe here, not read from it later: the recipe may be superseded while the order
    /// is still open, and the order must keep saying what it was built from.
    /// </summary>
    public async Task<ProductionOrderResponse> CreateAsync(
        CreateProductionOrderRequest request,
        CancellationToken cancellationToken)
    {
        var bom = await ResolveBillOfMaterialAsync(request, cancellationToken);

        if (bom.Items.Count == 0)
        {
            throw new ProductionOrderInvalidException(
                $"Bill of materials version {bom.Version} of {bom.Product.Sku} has no components.",
                new Dictionary<string, object?> { ["billOfMaterialId"] = bom.Id });
        }

        var productionLocation = await ResolveLocationAsync(request.ProductionLocationId, cancellationToken);
        var outputLocation = await ResolveLocationAsync(request.OutputLocationId, cancellationToken);

        var order = new ProductionOrder
        {
            Number = await NextNumberAsync(cancellationToken),
            ProductId = bom.ProductId,
            BillOfMaterialId = bom.Id,
            PlannedQuantity = request.PlannedQuantity,
            ProductionLocationId = productionLocation.Id,
            OutputLocationId = outputLocation.Id,
            Status = ProductionOrderStatus.Draft,
            PlannedStartAt = request.PlannedStartAt,
            Notes = Trimmed(request.Notes),
            Materials = bom.Items.Select(item => new ProductionOrderMaterial
            {
                ComponentProductId = item.ComponentProductId,
                // The unit follows the component product, exactly as on a movement line.
                UnitOfMeasureId = item.UnitOfMeasureId,
                RequiredQuantity = bom.RequiredQuantityFor(item.Quantity, request.PlannedQuantity)
            }).ToList()
        };

        var unscaled = order.Materials.FirstOrDefault(m => m.RequiredQuantity <= 0);

        if (unscaled is not null)
        {
            // A run so small that a component rounds away to nothing is not a run anybody meant.
            throw new ProductionOrderInvalidException(
                "The planned quantity is too small: a component would round down to zero.",
                new Dictionary<string, object?>
                {
                    ["componentProductId"] = unscaled.ComponentProductId,
                    ["plannedQuantity"] = request.PlannedQuantity
                });
        }

        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Production order {Number} created for {Quantity} × {Sku} from BOM version {Version} " +
            "at {ProductionLocationId}, output to {OutputLocationId}",
            order.Number, order.PlannedQuantity, bom.Product.Sku, bom.Version,
            order.ProductionLocationId, order.OutputLocationId);

        return ToResponse(await FindAsync(order.Id, cancellationToken));
    }

    /// <summary>
    /// Reserves every material on the shop floor. Nothing leaves stock yet — a reservation only
    /// says the quantity is spoken for, so a competing operation can no longer take it
    /// (CLAUDE.md, rule 6).
    /// </summary>
    public async Task<ProductionOrderResponse> PlanAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var order = await FindAsync(id, cancellationToken);

        order.RequireStatus(ProductionOrderStatus.Draft, "planned");
        RequireActiveLocations(order);

        var balances = await LockMaterialsAsync(order, cancellationToken);

        foreach (var material in order.Materials)
        {
            var balance = balances[new StockKey(material.ComponentProductId, order.ProductionLocationId)];

            if (balance.AvailableQuantity < material.RequiredQuantity)
            {
                throw new InsufficientStockException(
                    material.ComponentProductId,
                    material.ComponentProduct.Sku,
                    order.ProductionLocationId,
                    order.ProductionLocation.Code,
                    material.RequiredQuantity,
                    balance.AvailableQuantity);
            }

            balance.ReservedQuantity += material.RequiredQuantity;
        }

        order.Status = ProductionOrderStatus.Planned;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Production order {Number} planned by {UserId}: {MaterialCount} material(s) reserved at {LocationId}",
            order.Number, currentUser.UserId, order.Materials.Count, order.ProductionLocationId);

        return ToResponse(order);
    }

    /// <summary>
    /// Starts the run: the reserved materials are consumed (docs/PLAN.md, section 16). The
    /// reservation is released and a confirmed Consumption movement takes the same quantities out
    /// of the shop floor location — both inside one transaction, so stock is never left reserved
    /// for a run that did not start, or consumed by one that did not.
    /// </summary>
    public async Task<ProductionOrderResponse> StartAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var order = await FindAsync(id, cancellationToken);

        order.RequireStatus(ProductionOrderStatus.Planned, "started");
        RequireActiveLocations(order);

        // Released before the movement is confirmed: the confirmation checks available quantity,
        // and this order's own reservation must not stand in the way of its own consumption.
        await ReleaseReservationsAsync(order, cancellationToken);

        var consumption = await stockMovements.PostForProductionOrderAsync(
            new CreateStockMovementRequest(
                MovementType.Consumption,
                SourceLocationId: order.ProductionLocationId,
                DestinationLocationId: null,
                Reason: $"Production order {order.Number}",
                Lines: order.Materials
                    .Select(m => new CreateStockMovementLineRequest(m.ComponentProductId, m.RequiredQuantity))
                    .ToList()),
            order.Id,
            cancellationToken);

        foreach (var material in order.Materials)
        {
            material.ConsumedQuantity = material.RequiredQuantity;
        }

        order.Status = ProductionOrderStatus.InProgress;
        order.ActualStartAt = timeProvider.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Production order {Number} started by {UserId}: materials consumed by movement {MovementNumber}",
            order.Number, currentUser.UserId, consumption.Number);

        return ToResponse(order);
    }

    /// <summary>
    /// Completes the run: the finished goods are booked into the output location by a confirmed
    /// ProductionOutput movement (docs/PLAN.md, section 17). A run yields what it yields, so the
    /// produced quantity may differ from the planned one.
    /// </summary>
    public async Task<ProductionOrderResponse> CompleteAsync(
        Guid id,
        CompleteProductionOrderRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var order = await FindAsync(id, cancellationToken);

        order.RequireStatus(ProductionOrderStatus.InProgress, "completed");
        RequireActiveLocations(order);

        var producedQuantity = request.ProducedQuantity ?? order.PlannedQuantity;

        if (producedQuantity <= 0)
        {
            throw new ProductionOrderInvalidException(
                $"Production order {order.Number} must produce a positive quantity.",
                new Dictionary<string, object?> { ["producedQuantity"] = producedQuantity });
        }

        var output = await stockMovements.PostForProductionOrderAsync(
            new CreateStockMovementRequest(
                MovementType.ProductionOutput,
                SourceLocationId: null,
                DestinationLocationId: order.OutputLocationId,
                Reason: $"Production order {order.Number}",
                Lines: [new CreateStockMovementLineRequest(order.ProductId, producedQuantity)]),
            order.Id,
            cancellationToken);

        order.ProducedQuantity = producedQuantity;
        order.Status = ProductionOrderStatus.Completed;
        order.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            order.Notes = request.Notes.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Production order {Number} completed by {UserId}: {Quantity} booked into {LocationId} " +
            "by movement {MovementNumber}",
            order.Number, currentUser.UserId, producedQuantity, order.OutputLocationId, output.Number);

        return ToResponse(order);
    }

    /// <summary>
    /// Abandons a run that has not consumed anything yet, releasing whatever it reserved. A run
    /// that has already started has confirmed movements behind it, and those are history: it is
    /// corrected with compensating movements, not by cancelling the order (CLAUDE.md, rule 2).
    /// </summary>
    public async Task<ProductionOrderResponse> CancelAsync(
        Guid id,
        CancelProductionOrderRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var order = await FindAsync(id, cancellationToken);

        switch (order.Status)
        {
            case ProductionOrderStatus.Draft:
                break;

            case ProductionOrderStatus.Planned:
                await ReleaseReservationsAsync(order, cancellationToken);
                break;

            case ProductionOrderStatus.Completed:
                throw new ProductionOrderAlreadyCompletedException(order.Id, order.Number);

            default:
                throw new ProductionOrderInvalidException(
                    $"Production order {order.Number} is {order.Status} and has already consumed its " +
                    "materials. Correct it with compensating stock movements.",
                    new Dictionary<string, object?>
                    {
                        ["productionOrderId"] = order.Id,
                        ["number"] = order.Number,
                        ["status"] = order.Status.ToString()
                    });
        }

        order.Status = ProductionOrderStatus.Cancelled;
        order.CancelledAt = timeProvider.GetUtcNow().UtcDateTime;
        order.CancelledBy = currentUser.UserId;

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            order.Notes = request.Reason.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Production order {Number} cancelled by {UserId}",
            order.Number, order.CancelledBy);

        return ToResponse(order);
    }

    /// <summary>
    /// Gives the reserved quantities back to the shop floor balance. Saved before anything else
    /// reads them, so the release is visible to the consumption that follows it.
    /// </summary>
    private async Task ReleaseReservationsAsync(ProductionOrder order, CancellationToken cancellationToken)
    {
        var balances = await LockMaterialsAsync(order, cancellationToken);

        foreach (var material in order.Materials)
        {
            var balance = balances[new StockKey(material.ComponentProductId, order.ProductionLocationId)];

            // Never below zero: a reservation is released once, but the balance is a shared row and
            // has to stay a truthful number whatever else happened to it meanwhile.
            balance.ReservedQuantity = Math.Max(0m, balance.ReservedQuantity - material.RequiredQuantity);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Locks the shop floor balance of every material the order touches, the same way and in the
    /// same order as a movement confirmation does, so the two can never interleave halfway.
    /// </summary>
    private async Task<Dictionary<StockKey, Stock>> LockMaterialsAsync(
        ProductionOrder order,
        CancellationToken cancellationToken)
    {
        var keys = order.Materials
            .Select(m => new StockKey(m.ComponentProductId, order.ProductionLocationId))
            .ToHashSet();

        var balances = await db.LockStockAsync(keys, cancellationToken);

        return balances.ToDictionary(s => new StockKey(s.ProductId, s.LocationId));
    }

    /// <summary>
    /// The recipe the run is built from: the one asked for, or the product's active version. An
    /// older version can still be named explicitly — a run may deliberately repeat one — but a
    /// product with no active version is not something to produce by guesswork.
    /// </summary>
    private async Task<BillOfMaterial> ResolveBillOfMaterialAsync(
        CreateProductionOrderRequest request,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
                      ?? throw new ProductNotFoundException(request.ProductId);

        if (request.BillOfMaterialId is { } bomId)
        {
            var named = await IncludeBom(db.BillsOfMaterial)
                            .FirstOrDefaultAsync(b => b.Id == bomId, cancellationToken)
                        ?? throw new BomNotFoundException(bomId);

            if (named.ProductId != product.Id)
            {
                throw new ProductionOrderInvalidException(
                    $"Bill of materials version {named.Version} does not produce {product.Sku}.",
                    new Dictionary<string, object?>
                    {
                        ["billOfMaterialId"] = named.Id,
                        ["productId"] = product.Id
                    });
            }

            return named;
        }

        return await IncludeBom(db.BillsOfMaterial)
                   .FirstOrDefaultAsync(b => b.ProductId == product.Id && b.IsActive, cancellationToken)
               ?? throw new ProductionOrderInvalidException(
                   $"Product {product.Sku} has no active bill of materials to produce it from.",
                   new Dictionary<string, object?> { ["productId"] = product.Id, ["sku"] = product.Sku });
    }

    private async Task<StorageLocation> ResolveLocationAsync(Guid locationId, CancellationToken cancellationToken)
    {
        var location = await db.StorageLocations.FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken)
                       ?? throw new LocationNotFoundException(locationId);

        return location.IsActive ? location : throw new LocationInactiveException(location.Id, location.Code);
    }

    /// <summary>A location may have been deactivated between two steps of the workflow.</summary>
    private static void RequireActiveLocations(ProductionOrder order)
    {
        foreach (var location in new[] { order.ProductionLocation, order.OutputLocation })
        {
            if (!location.IsActive)
            {
                throw new LocationInactiveException(location.Id, location.Code);
            }
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<string> NextNumberAsync(CancellationToken cancellationToken)
        => $"PRD-{await db.NextProductionOrderNumberAsync(cancellationToken):D6}";

    private async Task<ProductionOrder> FindAsync(Guid id, CancellationToken cancellationToken)
        => await Include(db.ProductionOrders).FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
           ?? throw new ProductionOrderNotFoundException(id);

    private static IQueryable<BillOfMaterial> IncludeBom(IQueryable<BillOfMaterial> boms) => boms
        .Include(b => b.Product)
        .Include(b => b.Items);

    private static IQueryable<ProductionOrder> Include(IQueryable<ProductionOrder> orders) => orders
        .Include(o => o.Product).ThenInclude(p => p.UnitOfMeasure)
        .Include(o => o.BillOfMaterial)
        .Include(o => o.ProductionLocation)
        .Include(o => o.OutputLocation)
        .Include(o => o.Materials).ThenInclude(m => m.ComponentProduct)
        .Include(o => o.Materials).ThenInclude(m => m.UnitOfMeasure);

    private static IQueryable<ProductionOrder> Sort(IQueryable<ProductionOrder> orders, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        var field = (descending ? sort![1..] : sort)?.Trim().ToLowerInvariant();

        return (field, descending) switch
        {
            ("number", false) => orders.OrderBy(o => o.Number),
            ("number", true) => orders.OrderByDescending(o => o.Number),
            ("createdat", false) => orders.OrderBy(o => o.CreatedAt).ThenBy(o => o.Number),
            ("plannedstartat", false) => orders.OrderBy(o => o.PlannedStartAt).ThenBy(o => o.Number),
            ("plannedstartat", true) => orders.OrderByDescending(o => o.PlannedStartAt).ThenByDescending(o => o.Number),
            // Newest first: the default a production journal is read in.
            _ => orders.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number)
        };
    }

    private static ProductionOrderResponse ToResponse(ProductionOrder order) => new(
        order.Id,
        order.Number,
        order.Status,
        order.ProductId,
        order.Product.Sku,
        order.Product.Name,
        order.Product.UnitOfMeasure.Code,
        order.BillOfMaterialId,
        order.BillOfMaterial.Version,
        order.PlannedQuantity,
        order.ProducedQuantity,
        order.ProductionLocationId,
        order.ProductionLocation.Code,
        order.OutputLocationId,
        order.OutputLocation.Code,
        order.Notes,
        order.Materials
            .OrderBy(m => m.ComponentProduct.Sku)
            .Select(m => new ProductionOrderMaterialResponse(
                m.Id,
                m.ComponentProductId,
                m.ComponentProduct.Sku,
                m.ComponentProduct.Name,
                m.RequiredQuantity,
                m.ConsumedQuantity,
                m.UnitOfMeasureId,
                m.UnitOfMeasure.Code))
            .ToList(),
        order.PlannedStartAt,
        order.ActualStartAt,
        order.CompletedAt,
        order.CancelledAt,
        order.CancelledBy,
        order.CreatedAt,
        order.CreatedBy,
        order.UpdatedAt);
}
