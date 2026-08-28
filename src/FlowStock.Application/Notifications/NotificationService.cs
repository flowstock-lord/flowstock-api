using FlowStock.Application.Common;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Notifications;
using FlowStock.Domain.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Notifications;

/// <summary>
/// The narrow half of notifications, for the modules that raise them. Inventory and production
/// know how to say "this happened"; they know nothing about inboxes, filters or scans.
/// </summary>
public interface INotificationRaiser
{
    /// <summary>
    /// Records a notification unless its event has already been recorded. Runs inside whatever
    /// transaction the caller opened, so an operation that rolls back tells nobody anything.
    /// </summary>
    Task<bool> RaiseAsync(Notification notification, CancellationToken cancellationToken);
}

public interface INotificationService : INotificationRaiser
{
    Task<PagedResult<NotificationResponse>> ListAsync(
        NotificationQuery query,
        CancellationToken cancellationToken);

    Task<NotificationResponse> MarkReadAsync(Guid id, bool isRead, CancellationToken cancellationToken);

    Task<NotificationScanResponse> ScanAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Notifications (docs/PLAN.md, section 31). Nothing here changes stock: a notification is a record
/// of something the system noticed, and the plan is explicit that this stays application-level —
/// no broker, no queue, no external transport until there is a real requirement.
///
/// Two kinds of thing raise one. An operation raises its own as it happens — a completed run, a
/// received transfer — inside the transaction that did the work. A condition that no single
/// operation causes, such as a lot going out of date, is found by <see cref="ScanAsync"/>, which
/// the API runs on a timer and an administrator can run by hand.
/// </summary>
public class NotificationService(
    IFlowStockDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task<PagedResult<NotificationResponse>> ListAsync(
        NotificationQuery query,
        CancellationToken cancellationToken)
    {
        var notifications = db.Notifications.AsQueryable();

        if (query.Type is not null)
        {
            notifications = notifications.Where(n => n.Type == query.Type);
        }

        if (query.IsRead is not null)
        {
            notifications = notifications.Where(n => n.IsRead == query.IsRead);
        }

        if (query.ProductId is not null)
        {
            notifications = notifications.Where(n => n.ProductId == query.ProductId);
        }

        if (query.ProductionOrderId is not null)
        {
            notifications = notifications.Where(n => n.ProductionOrderId == query.ProductionOrderId);
        }

        if (query.From is not null)
        {
            notifications = notifications.Where(n => n.OccurredAt >= query.From);
        }

        if (query.To is not null)
        {
            notifications = notifications.Where(n => n.OccurredAt < query.To);
        }

        var totalCount = await notifications.CountAsync(cancellationToken);

        var ascending = !string.IsNullOrWhiteSpace(query.Sort) && !query.Sort.StartsWith('-');

        var page = await (ascending
                ? notifications.OrderBy(n => n.OccurredAt).ThenBy(n => n.Id)
                : notifications.OrderByDescending(n => n.OccurredAt).ThenByDescending(n => n.Id))
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationResponse>(
            await DescribeAsync(page, cancellationToken),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<NotificationResponse> MarkReadAsync(
        Guid id,
        bool isRead,
        CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                           ?? throw new NotificationNotFoundException(id);

        notification.IsRead = isRead;
        notification.ReadAt = isRead ? timeProvider.GetUtcNow().UtcDateTime : null;
        notification.ReadBy = isRead ? currentUser.UserId : null;

        await db.SaveChangesAsync(cancellationToken);

        return (await DescribeAsync([notification], cancellationToken))[0];
    }

    public async Task<bool> RaiseAsync(Notification notification, CancellationToken cancellationToken)
    {
        // The event key is what makes raising idempotent: the same event recorded once, however
        // many times an operation is retried or a scan runs.
        if (await db.Notifications.AnyAsync(n => n.EventKey == notification.EventKey, cancellationToken))
        {
            return false;
        }

        notification.OccurredAt = notification.OccurredAt == default
            ? timeProvider.GetUtcNow().UtcDateTime
            : notification.OccurredAt;

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Notification raised: {Type} ({EventKey})", notification.Type, notification.EventKey);

        return true;
    }

    /// <summary>
    /// Looks for the conditions no single operation reports: lots that have gone out of date with
    /// stock still on them, and draft production runs the shop floor cannot feed.
    ///
    /// A run that has already been planned is not short — planning reserved exactly what it needs,
    /// and that reservation is what its availability now reflects.
    /// </summary>
    public async Task<NotificationScanResponse> ScanAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var expired = await ScanExpiredBatchesAsync(today, now, cancellationToken);
        var shortages = await ScanProductionShortagesAsync(now, cancellationToken);

        if (expired + shortages > 0)
        {
            logger.LogInformation(
                "Notification scan raised {Raised} notification(s): {Expired} expired batch(es), {Shortages} shortage(s)",
                expired + shortages, expired, shortages);
        }

        return new NotificationScanResponse(expired + shortages, expired, shortages, now);
    }

    private async Task<int> ScanExpiredBatchesAsync(
        DateOnly today,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Only lots that still hold something: a lot that has been used up is nobody's problem.
        var expired = await db.Batches
            .Include(b => b.Product)
            .Where(b => b.ExpiryDate != null
                        && b.ExpiryDate < today
                        && db.Stocks.Any(s => s.BatchId == b.Id && s.Quantity > 0))
            .ToListAsync(cancellationToken);

        var raised = 0;

        foreach (var batch in expired)
        {
            var onHand = await db.Stocks
                .Where(s => s.BatchId == batch.Id)
                .SumAsync(s => s.Quantity, cancellationToken);

            var notification = new Notification
            {
                Type = NotificationType.ExpiredBatch,
                EventKey = Notification.KeyFor(NotificationType.ExpiredBatch, batch.Id),
                Message =
                    $"Batch {batch.Number} of {batch.Product.Sku} expired on {batch.ExpiryDate:yyyy-MM-dd} " +
                    $"with {onHand} still in stock.",
                OccurredAt = now,
                ProductId = batch.ProductId,
                BatchId = batch.Id
            };

            if (await RaiseAsync(notification, cancellationToken))
            {
                raised++;
            }
        }

        return raised;
    }

    private async Task<int> ScanProductionShortagesAsync(DateTime now, CancellationToken cancellationToken)
    {
        var drafts = await db.ProductionOrders
            .Include(o => o.ProductionLocation)
            .Include(o => o.Materials).ThenInclude(m => m.ComponentProduct)
            .Include(o => o.Materials).ThenInclude(m => m.UnitOfMeasure)
            .Where(o => o.Status == ProductionOrderStatus.Draft)
            .ToListAsync(cancellationToken);

        var raised = 0;

        foreach (var order in drafts)
        {
            foreach (var material in order.Materials)
            {
                var available = await db.Stocks
                    .Where(s => s.ProductId == material.ComponentProductId
                                && s.LocationId == order.ProductionLocationId
                                && (material.BatchId == null || s.BatchId == material.BatchId))
                    .SumAsync(s => s.Quantity - s.ReservedQuantity, cancellationToken);

                if (available >= material.RequiredQuantity)
                {
                    continue;
                }

                var notification = new Notification
                {
                    Type = NotificationType.ProductionShortage,
                    EventKey = Notification.KeyFor(
                        NotificationType.ProductionShortage, order.Id, material.ComponentProductId),
                    Message =
                        $"Production order {order.Number} needs {material.RequiredQuantity} " +
                        $"{material.UnitOfMeasure.Code} of {material.ComponentProduct.Sku} at " +
                        $"{order.ProductionLocation.Code}, where {available} is available.",
                    OccurredAt = now,
                    ProductId = material.ComponentProductId,
                    BatchId = material.BatchId,
                    LocationId = order.ProductionLocationId,
                    ProductionOrderId = order.Id
                };

                if (await RaiseAsync(notification, cancellationToken))
                {
                    raised++;
                }
            }
        }

        return raised;
    }

    /// <summary>
    /// Fills in the names behind the ids a notification carries, in one query per kind, so a list
    /// reads without a lookup per row.
    /// </summary>
    private async Task<IReadOnlyList<NotificationResponse>> DescribeAsync(
        IReadOnlyList<Notification> notifications,
        CancellationToken cancellationToken)
    {
        if (notifications.Count == 0)
        {
            return [];
        }

        var productIds = Ids(notifications, n => n.ProductId);
        var batchIds = Ids(notifications, n => n.BatchId);
        var locationIds = Ids(notifications, n => n.LocationId);
        var orderIds = Ids(notifications, n => n.ProductionOrderId);
        var movementIds = Ids(notifications, n => n.StockMovementId);

        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Name })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var batches = await db.Batches
            .Where(b => batchIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Number })
            .ToDictionaryAsync(b => b.Id, b => b.Number, cancellationToken);

        var locations = await db.StorageLocations
            .Where(l => locationIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Code })
            .ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);

        var orders = await db.ProductionOrders
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Number })
            .ToDictionaryAsync(o => o.Id, o => o.Number, cancellationToken);

        var movements = await db.StockMovements
            .Where(m => movementIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Number })
            .ToDictionaryAsync(m => m.Id, m => m.Number, cancellationToken);

        return notifications.Select(n =>
        {
            var product = n.ProductId is { } productId ? products.GetValueOrDefault(productId) : null;

            return new NotificationResponse(
                n.Id,
                n.Type,
                n.Message,
                n.OccurredAt,
                n.ProductId,
                product?.Sku,
                product?.Name,
                n.BatchId,
                n.BatchId is { } batchId ? batches.GetValueOrDefault(batchId) : null,
                n.LocationId,
                n.LocationId is { } locationId ? locations.GetValueOrDefault(locationId) : null,
                n.ProductionOrderId,
                n.ProductionOrderId is { } orderId ? orders.GetValueOrDefault(orderId) : null,
                n.StockMovementId,
                n.StockMovementId is { } movementId ? movements.GetValueOrDefault(movementId) : null,
                n.IsRead,
                n.ReadAt,
                n.ReadBy,
                n.CreatedAt);
        }).ToList();
    }

    private static List<Guid> Ids(IEnumerable<Notification> notifications, Func<Notification, Guid?> id) =>
        notifications.Select(id).OfType<Guid>().Distinct().ToList();
}
