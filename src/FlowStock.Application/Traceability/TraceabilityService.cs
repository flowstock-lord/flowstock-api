using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;
using Microsoft.EntityFrameworkCore;

namespace FlowStock.Application.Traceability;

public interface ITraceabilityService
{
    Task<PagedResult<ProductHistoryEntry>> ProductHistoryAsync(
        Guid productId,
        ProductHistoryQuery query,
        CancellationToken cancellationToken);

    Task<ProductionTraceResponse> ProductionTraceAsync(Guid productionOrderId, CancellationToken cancellationToken);

    Task<PagedResult<MaterialUsageEntry>> MaterialUsageAsync(
        Guid productId,
        MaterialUsageQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// Answers the traceability questions of docs/PLAN.md, sections 19 and 39: where a product came
/// from, what a finished product was made of, where a material ended up, and who did it when.
///
/// Read-only, like every report built on the transaction history: it derives its answers from
/// confirmed movements and production orders and can never become another way to change stock
/// (CLAUDE.md, rule 1).
/// </summary>
public class TraceabilityService(IFlowStockDbContext db) : ITraceabilityService
{
    /// <summary>
    /// How many inbound movements are listed as the possible source of one consumed material.
    /// A handful answers "where did this come from"; the full list is the product history.
    /// </summary>
    private const int SourceLimit = 5;

    /// <summary>
    /// Everything that ever happened to one product, newest first: which document, which way,
    /// how much, between which locations, why, and who confirmed it.
    ///
    /// Only confirmed movements appear. A draft has not happened, and a cancelled one never did.
    /// </summary>
    public async Task<PagedResult<ProductHistoryEntry>> ProductHistoryAsync(
        Guid productId,
        ProductHistoryQuery query,
        CancellationToken cancellationToken)
    {
        await RequireProductAsync(productId, cancellationToken);

        var lines = db.StockMovementLines
            .Include(l => l.UnitOfMeasure)
            .Include(l => l.StockMovement).ThenInclude(m => m.SourceLocation)
            .Include(l => l.StockMovement).ThenInclude(m => m.DestinationLocation)
            .Where(l => l.ProductId == productId && l.StockMovement.Status == MovementStatus.Confirmed);

        if (query.LocationId is { } locationId)
        {
            lines = lines.Where(l =>
                l.StockMovement.SourceLocationId == locationId ||
                l.StockMovement.DestinationLocationId == locationId);
        }

        if (query.MovementType is not null)
        {
            lines = lines.Where(l => l.StockMovement.MovementType == query.MovementType);
        }

        if (query.From is not null)
        {
            lines = lines.Where(l => l.StockMovement.ConfirmedAt >= query.From);
        }

        if (query.To is not null)
        {
            lines = lines.Where(l => l.StockMovement.ConfirmedAt < query.To);
        }

        var totalCount = await lines.CountAsync(cancellationToken);

        var ascending = !string.IsNullOrWhiteSpace(query.Sort) && !query.Sort.StartsWith('-');

        var page = await (ascending
                ? lines.OrderBy(l => l.StockMovement.ConfirmedAt).ThenBy(l => l.StockMovement.Number)
                : lines.OrderByDescending(l => l.StockMovement.ConfirmedAt)
                    .ThenByDescending(l => l.StockMovement.Number))
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var movements = page.Select(l => l.StockMovement).ToList();
        var users = await ResolveUsersAsync(movements.Select(m => PerformedBy(m)), cancellationToken);
        var orderNumbers = await ResolveOrderNumbersAsync(movements, cancellationToken);

        var items = page.Select(line =>
        {
            var movement = line.StockMovement;

            return new ProductHistoryEntry(
                movement.Id,
                movement.Number,
                movement.MovementType,
                FlowOf(movement, query.LocationId),
                movement.ConfirmedAt!.Value,
                line.Quantity,
                line.UnitOfMeasure.Code,
                movement.SourceLocationId,
                movement.SourceLocation?.Code,
                movement.DestinationLocationId,
                movement.DestinationLocation?.Code,
                movement.Reason,
                movement.ProductionOrderId,
                movement.ProductionOrderId is { } orderId ? orderNumbers.GetValueOrDefault(orderId) : null,
                users.Of(PerformedBy(movement)));
        }).ToList();

        return new PagedResult<ProductHistoryEntry>(items, query.Page, query.PageSize, totalCount);
    }

    /// <summary>
    /// Backward traceability: what went into one production run — the recipe version it used, the
    /// materials it consumed, the movements that consumed them, where those materials had come
    /// from, and where the finished goods went (docs/PLAN.md, section 19).
    /// </summary>
    public async Task<ProductionTraceResponse> ProductionTraceAsync(
        Guid productionOrderId,
        CancellationToken cancellationToken)
    {
        var order = await db.ProductionOrders
                        .Include(o => o.Product).ThenInclude(p => p.UnitOfMeasure)
                        .Include(o => o.BillOfMaterial)
                        .Include(o => o.ProductionLocation)
                        .Include(o => o.OutputLocation)
                        .Include(o => o.Materials).ThenInclude(m => m.ComponentProduct)
                        .Include(o => o.Materials).ThenInclude(m => m.UnitOfMeasure)
                        .FirstOrDefaultAsync(o => o.Id == productionOrderId, cancellationToken)
                    ?? throw new ProductionOrderNotFoundException(productionOrderId);

        var posted = await db.StockMovements
            .Include(m => m.Lines)
            .Where(m => m.ProductionOrderId == order.Id && m.Status == MovementStatus.Confirmed)
            .ToListAsync(cancellationToken);

        var consumption = posted.FirstOrDefault(m => m.MovementType == MovementType.Consumption);
        var output = posted.FirstOrDefault(m => m.MovementType == MovementType.ProductionOutput);

        var materials = new List<ConsumedMaterial>();

        foreach (var material in order.Materials.OrderBy(m => m.ComponentProduct.Sku))
        {
            var sources = await SourcesOfAsync(order, material.ComponentProductId, consumption, cancellationToken);

            materials.Add(new ConsumedMaterial(
                material.ComponentProductId,
                material.ComponentProduct.Sku,
                material.ComponentProduct.Name,
                material.RequiredQuantity,
                material.ConsumedQuantity,
                material.UnitOfMeasure.Code,
                consumption?.Id,
                consumption?.Number,
                consumption?.ConfirmedAt,
                consumption is null ? null : await UserAsync(consumption.ConfirmedBy, cancellationToken),
                sources));
        }

        var users = await ResolveUsersAsync(
            [order.CreatedBy, output?.ConfirmedBy],
            cancellationToken);

        return new ProductionTraceResponse(
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
            order.CreatedAt,
            users.Of(order.CreatedBy),
            order.ActualStartAt,
            order.CompletedAt,
            output is null
                ? null
                : new ProductionOutput(
                    output.Id,
                    output.Number,
                    output.ConfirmedAt!.Value,
                    output.Lines.Where(l => l.ProductId == order.ProductId).Sum(l => l.Quantity),
                    order.OutputLocationId,
                    order.OutputLocation.Code,
                    users.Of(output.ConfirmedBy)),
            materials);
    }

    /// <summary>
    /// Forward traceability: which runs consumed a given material, and what those runs produced
    /// (docs/PLAN.md, section 19).
    /// </summary>
    public async Task<PagedResult<MaterialUsageEntry>> MaterialUsageAsync(
        Guid productId,
        MaterialUsageQuery query,
        CancellationToken cancellationToken)
    {
        await RequireProductAsync(productId, cancellationToken);

        var orders = db.ProductionOrders
            .Include(o => o.Product).ThenInclude(p => p.UnitOfMeasure)
            .Include(o => o.ProductionLocation)
            .Include(o => o.OutputLocation)
            .Include(o => o.Materials).ThenInclude(m => m.UnitOfMeasure)
            .Where(o => o.Materials.Any(m => m.ComponentProductId == productId));

        if (query.Status is not null)
        {
            orders = orders.Where(o => o.Status == query.Status);
        }

        if (query.From is not null)
        {
            orders = orders.Where(o => o.ActualStartAt >= query.From);
        }

        if (query.To is not null)
        {
            orders = orders.Where(o => o.ActualStartAt < query.To);
        }

        var totalCount = await orders.CountAsync(cancellationToken);

        var ascending = !string.IsNullOrWhiteSpace(query.Sort) && !query.Sort.StartsWith('-');

        var page = await (ascending
                ? orders.OrderBy(o => o.ActualStartAt).ThenBy(o => o.Number)
                : orders.OrderByDescending(o => o.ActualStartAt).ThenByDescending(o => o.Number))
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var orderIds = page.Select(o => o.Id).ToList();

        // When the material was actually taken is on the consumption document, not on the order.
        var consumedAt = await db.StockMovements
            .Where(m => m.ProductionOrderId != null
                        && orderIds.Contains(m.ProductionOrderId!.Value)
                        && m.MovementType == MovementType.Consumption
                        && m.Status == MovementStatus.Confirmed)
            .ToDictionaryAsync(m => m.ProductionOrderId!.Value, m => m.ConfirmedAt, cancellationToken);

        var users = await ResolveUsersAsync(page.Select(o => o.CreatedBy), cancellationToken);

        var items = page.Select(order =>
        {
            var material = order.Materials.Single(m => m.ComponentProductId == productId);

            return new MaterialUsageEntry(
                order.Id,
                order.Number,
                order.Status,
                material.ConsumedQuantity,
                material.UnitOfMeasure.Code,
                consumedAt.GetValueOrDefault(order.Id),
                order.ProductionLocationId,
                order.ProductionLocation.Code,
                order.ProductId,
                order.Product.Sku,
                order.Product.Name,
                order.ProducedQuantity,
                order.Product.UnitOfMeasure.Code,
                order.OutputLocationId,
                order.OutputLocation.Code,
                order.CompletedAt,
                users.Of(order.CreatedBy));
        }).ToList();

        return new PagedResult<MaterialUsageEntry>(items, query.Page, query.PageSize, totalCount);
    }

    /// <summary>
    /// The confirmed movements that brought a material into the production location before the run
    /// took it. Without batches these are candidates, not certainties — see <see cref="MaterialSource"/>.
    /// </summary>
    private async Task<IReadOnlyList<MaterialSource>> SourcesOfAsync(
        ProductionOrder order,
        Guid componentProductId,
        StockMovement? consumption,
        CancellationToken cancellationToken)
    {
        var cutoff = consumption?.ConfirmedAt;

        var inbound = db.StockMovements
            .Include(m => m.SourceLocation)
            .Where(m => m.Status == MovementStatus.Confirmed
                        && m.DestinationLocationId == order.ProductionLocationId
                        && m.Lines.Any(l => l.ProductId == componentProductId));

        if (consumption is { } posted)
        {
            // Everything that arrived up to the moment the run took the material — and not the
            // run's own document, which is what took it.
            inbound = inbound.Where(m => m.Id != posted.Id && m.ConfirmedAt <= cutoff);
        }

        var movements = await inbound
            .OrderByDescending(m => m.ConfirmedAt)
            .Take(SourceLimit)
            .Select(m => new
            {
                Movement = m,
                Quantity = m.Lines.Where(l => l.ProductId == componentProductId).Sum(l => l.Quantity)
            })
            .ToListAsync(cancellationToken);

        var users = await ResolveUsersAsync(movements.Select(m => m.Movement.ConfirmedBy), cancellationToken);

        return movements.Select(source => new MaterialSource(
            source.Movement.Id,
            source.Movement.Number,
            source.Movement.MovementType,
            source.Movement.ConfirmedAt!.Value,
            source.Quantity,
            source.Movement.SourceLocationId,
            source.Movement.SourceLocation?.Code,
            source.Movement.Reason,
            users.Of(source.Movement.ConfirmedBy))).ToList();
    }

    /// <summary>
    /// Who a movement is attributable to: whoever confirmed it, or whoever drafted it if it is
    /// somehow unconfirmed. Confirmation is the act that changed stock.
    /// </summary>
    private static Guid? PerformedBy(StockMovement movement) => movement.ConfirmedBy ?? movement.CreatedBy;

    private async Task RequireProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            throw new ProductNotFoundException(productId);
        }
    }

    private async Task<Dictionary<Guid, string?>> ResolveOrderNumbersAsync(
        IEnumerable<StockMovement> movements,
        CancellationToken cancellationToken)
    {
        var ids = movements
            .Select(m => m.ProductionOrderId)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await db.ProductionOrders
            .Where(o => ids.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => (string?)o.Number, cancellationToken);
    }

    private async Task<TraceUser> UserAsync(Guid? userId, CancellationToken cancellationToken)
        => (await ResolveUsersAsync([userId], cancellationToken)).Of(userId);

    /// <summary>Resolves the people behind a page of history in one query, names included.</summary>
    private async Task<TraceUsers> ResolveUsersAsync(
        IEnumerable<Guid?> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.OfType<Guid>().Distinct().ToList();

        if (ids.Count == 0)
        {
            return new TraceUsers([]);
        }

        // FullName is computed in C#, so the names are composed after the rows come back.
        var users = await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToListAsync(cancellationToken);

        return new TraceUsers(users.ToDictionary(
            u => u.Id,
            u => new TraceUser(u.Id, $"{u.FirstName} {u.LastName}".Trim(), u.Email)));
    }

    /// <summary>
    /// Which way the stock went. Asked about a location, the answer is relative to it; asked about
    /// a product alone, a transfer is a transfer.
    /// </summary>
    private static StockFlow FlowOf(StockMovement movement, Guid? locationId)
    {
        if (locationId is { } id)
        {
            return movement.DestinationLocationId == id ? StockFlow.In : StockFlow.Out;
        }

        return (movement.SourceLocationId, movement.DestinationLocationId) switch
        {
            (null, not null) => StockFlow.In,
            (not null, null) => StockFlow.Out,
            _ => StockFlow.Transfer
        };
    }

    /// <summary>The people behind one page of history, with a stand-in for anyone unknown.</summary>
    private sealed class TraceUsers(Dictionary<Guid, TraceUser> users)
    {
        public TraceUser Of(Guid? userId) =>
            userId is { } id && users.TryGetValue(id, out var user) ? user : new TraceUser(userId, null, null);
    }
}
