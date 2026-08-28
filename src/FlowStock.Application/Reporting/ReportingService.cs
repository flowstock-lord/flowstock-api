using FlowStock.Application.Common;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Production;
using Microsoft.EntityFrameworkCore;

namespace FlowStock.Application.Reporting;

public interface IReportingService
{
    Task<PagedResult<CurrentStockRow>> CurrentStockAsync(
        CurrentStockQuery query,
        CancellationToken cancellationToken);

    Task<PagedResult<WarehouseStockRow>> StockByWarehouseAsync(
        WarehouseStockQuery query,
        CancellationToken cancellationToken);

    Task<PagedResult<MovementHistoryRow>> MovementHistoryAsync(
        MovementHistoryQuery query,
        CancellationToken cancellationToken);

    Task<PagedResult<ProductionHistoryRow>> ProductionHistoryAsync(
        ProductionHistoryQuery query,
        CancellationToken cancellationToken);

    Task<PagedResult<MaterialConsumptionRow>> MaterialConsumptionAsync(
        ProductionTotalsQuery query,
        CancellationToken cancellationToken);

    Task<PagedResult<FinishedGoodsRow>> FinishedGoodsAsync(
        ProductionTotalsQuery query,
        CancellationToken cancellationToken);

    Task<PagedResult<AdjustmentRow>> AdjustmentsAsync(
        AdjustmentReportQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// The seven reports of docs/PLAN.md, section 30. Every one of them is a read over balances,
/// confirmed movements and production orders — a report is never another way to change stock, and
/// never a second version of the truth (CLAUDE.md, rule 1).
///
/// Two rules run through all of them. Only confirmed movements count: a draft has not happened and
/// a cancelled one never did. And quantities are only ever summed within one product, because a
/// product has exactly one unit of measure — kilograms and pieces are not a total.
///
/// The totalled reports filter and sort on the grouping rather than on the finished row: a
/// database can compare a sum, but not a record the query has projected.
/// </summary>
public class ReportingService(IFlowStockDbContext db) : IReportingService
{
    /// <summary>What every product holds right now, across all its locations and lots.</summary>
    public async Task<PagedResult<CurrentStockRow>> CurrentStockAsync(
        CurrentStockQuery query,
        CancellationToken cancellationToken)
    {
        var balances = db.Stocks.AsQueryable();

        if (query.WarehouseId is not null)
        {
            balances = balances.Where(s => s.Location.WarehouseId == query.WarehouseId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            balances = balances.Where(s =>
                s.Product.Sku.ToLower().Contains(search) ||
                s.Product.Name.ToLower().Contains(search));
        }

        if (query.ProductType is not null)
        {
            balances = balances.Where(s => s.Product.ProductType == query.ProductType);
        }

        var grouped = balances.GroupBy(s => new
        {
            s.ProductId,
            s.Product.Sku,
            ProductName = s.Product.Name,
            s.Product.ProductType,
            UnitOfMeasureCode = s.Product.UnitOfMeasure.Code
        });

        if (query.OnlyInStock)
        {
            grouped = grouped.Where(g => g.Sum(s => s.Quantity) > 0);
        }

        var (field, descending) = SortField(query.Sort);

        var ordered = (field, descending) switch
        {
            ("quantity", false) => grouped.OrderBy(g => g.Sum(s => s.Quantity)).ThenBy(g => g.Key.Sku),
            ("quantity", true) => grouped.OrderByDescending(g => g.Sum(s => s.Quantity)).ThenBy(g => g.Key.Sku),
            (_, true) => grouped.OrderByDescending(g => g.Key.Sku),
            _ => grouped.OrderBy(g => g.Key.Sku)
        };

        var rows = ordered.Select(g => new CurrentStockRow(
            g.Key.ProductId,
            g.Key.Sku,
            g.Key.ProductName,
            g.Key.ProductType,
            g.Key.UnitOfMeasureCode,
            g.Sum(s => s.Quantity),
            g.Sum(s => s.ReservedQuantity),
            g.Sum(s => s.Quantity) - g.Sum(s => s.ReservedQuantity),
            g.Count(),
            g.Count(s => s.BatchId != null)));

        return await PageAsync(rows, query, cancellationToken);
    }

    /// <summary>The same balances, split by the warehouse that holds them.</summary>
    public async Task<PagedResult<WarehouseStockRow>> StockByWarehouseAsync(
        WarehouseStockQuery query,
        CancellationToken cancellationToken)
    {
        var balances = db.Stocks.AsQueryable();

        if (query.WarehouseId is not null)
        {
            balances = balances.Where(s => s.Location.WarehouseId == query.WarehouseId);
        }

        if (query.ProductId is not null)
        {
            balances = balances.Where(s => s.ProductId == query.ProductId);
        }

        if (query.ProductType is not null)
        {
            balances = balances.Where(s => s.Product.ProductType == query.ProductType);
        }

        var grouped = balances.GroupBy(s => new
        {
            s.Location.WarehouseId,
            WarehouseCode = s.Location.Warehouse.Code,
            WarehouseName = s.Location.Warehouse.Name,
            s.ProductId,
            s.Product.Sku,
            ProductName = s.Product.Name,
            UnitOfMeasureCode = s.Product.UnitOfMeasure.Code
        });

        if (query.OnlyInStock)
        {
            grouped = grouped.Where(g => g.Sum(s => s.Quantity) > 0);
        }

        var (field, descending) = SortField(query.Sort);

        var ordered = (field, descending) switch
        {
            ("sku", false) => grouped.OrderBy(g => g.Key.Sku).ThenBy(g => g.Key.WarehouseCode),
            ("sku", true) => grouped.OrderByDescending(g => g.Key.Sku).ThenBy(g => g.Key.WarehouseCode),
            ("quantity", false) => grouped.OrderBy(g => g.Sum(s => s.Quantity)).ThenBy(g => g.Key.Sku),
            ("quantity", true) => grouped.OrderByDescending(g => g.Sum(s => s.Quantity)).ThenBy(g => g.Key.Sku),
            (_, true) => grouped.OrderByDescending(g => g.Key.WarehouseCode).ThenBy(g => g.Key.Sku),
            _ => grouped.OrderBy(g => g.Key.WarehouseCode).ThenBy(g => g.Key.Sku)
        };

        var rows = ordered.Select(g => new WarehouseStockRow(
            g.Key.WarehouseId,
            g.Key.WarehouseCode,
            g.Key.WarehouseName,
            g.Key.ProductId,
            g.Key.Sku,
            g.Key.ProductName,
            g.Key.UnitOfMeasureCode,
            g.Sum(s => s.Quantity),
            g.Sum(s => s.ReservedQuantity),
            g.Sum(s => s.Quantity) - g.Sum(s => s.ReservedQuantity),
            g.Count()));

        return await PageAsync(rows, query, cancellationToken);
    }

    /// <summary>
    /// The movement journal read line by line: one row per product per document, so what came in
    /// and what went out can be read off without opening every document.
    /// </summary>
    public async Task<PagedResult<MovementHistoryRow>> MovementHistoryAsync(
        MovementHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var lines = db.StockMovementLines
            .Where(l => l.StockMovement.Status == MovementStatus.Confirmed);

        lines = ApplyPeriod(lines, query);

        if (query.MovementType is not null)
        {
            lines = lines.Where(l => l.StockMovement.MovementType == query.MovementType);
        }

        if (query.LocationId is { } locationId)
        {
            lines = lines.Where(l =>
                l.StockMovement.SourceLocationId == locationId ||
                l.StockMovement.DestinationLocationId == locationId);
        }

        if (query.WarehouseId is { } warehouseId)
        {
            lines = lines.Where(l =>
                (l.StockMovement.SourceLocation != null &&
                 l.StockMovement.SourceLocation.WarehouseId == warehouseId) ||
                (l.StockMovement.DestinationLocation != null &&
                 l.StockMovement.DestinationLocation.WarehouseId == warehouseId));
        }

        if (query.BatchId is not null)
        {
            lines = lines.Where(l => l.BatchId == query.BatchId);
        }

        var rows = Chronological(lines, query.Sort).Select(l => new MovementHistoryRow(
            l.StockMovementId,
            l.StockMovement.Number,
            l.StockMovement.MovementType,
            l.StockMovement.ConfirmedAt!.Value,
            l.ProductId,
            l.Product.Sku,
            l.Product.Name,
            l.BatchId,
            l.Batch!.Number,
            l.Quantity,
            l.UnitOfMeasure.Code,
            l.StockMovement.SourceLocationId,
            l.StockMovement.SourceLocation!.Code,
            l.StockMovement.DestinationLocationId,
            l.StockMovement.DestinationLocation!.Code,
            l.StockMovement.Reason,
            l.StockMovement.ProductionOrderId,
            l.StockMovement.ConfirmedBy));

        return await PageAsync(rows, query, cancellationToken);
    }

    /// <summary>Every production run, with what it planned, what it yielded and when.</summary>
    public async Task<PagedResult<ProductionHistoryRow>> ProductionHistoryAsync(
        ProductionHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var orders = db.ProductionOrders.AsQueryable();

        if (query.ProductId is not null)
        {
            orders = orders.Where(o => o.ProductId == query.ProductId);
        }

        if (query.Status is not null)
        {
            orders = orders.Where(o => o.Status == query.Status);
        }

        if (query.From is not null)
        {
            orders = orders.Where(o => o.CreatedAt >= query.From);
        }

        if (query.To is not null)
        {
            orders = orders.Where(o => o.CreatedAt < query.To);
        }

        var (field, descending) = SortField(query.Sort);

        orders = (field, descending) switch
        {
            ("number", false) => orders.OrderBy(o => o.Number),
            ("number", true) => orders.OrderByDescending(o => o.Number),
            ("createdat", false) => orders.OrderBy(o => o.CreatedAt).ThenBy(o => o.Number),
            ("completedat", false) => orders.OrderBy(o => o.CompletedAt).ThenBy(o => o.Number),
            ("completedat", true) => orders.OrderByDescending(o => o.CompletedAt).ThenByDescending(o => o.Number),
            _ => orders.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number)
        };

        var totalCount = await orders.CountAsync(cancellationToken);

        var page = await orders
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(o => new
            {
                Order = o,
                o.Product.Sku,
                ProductName = o.Product.Name,
                UnitOfMeasureCode = o.Product.UnitOfMeasure.Code,
                OutputBatchNumber = o.OutputBatch != null ? o.OutputBatch.Number : null
            })
            .ToListAsync(cancellationToken);

        var items = page.Select(row => new ProductionHistoryRow(
            row.Order.Id,
            row.Order.Number,
            row.Order.Status,
            row.Order.ProductId,
            row.Sku,
            row.ProductName,
            row.UnitOfMeasureCode,
            row.Order.PlannedQuantity,
            row.Order.ProducedQuantity,
            // A yield only means something once the run has delivered; before that it would read 0%.
            row.Order.Status == ProductionOrderStatus.Completed && row.Order.PlannedQuantity > 0
                ? Math.Round(row.Order.ProducedQuantity * 100m / row.Order.PlannedQuantity, 2,
                    MidpointRounding.AwayFromZero)
                : null,
            row.Order.OutputBatchId,
            row.OutputBatchNumber,
            row.Order.CreatedAt,
            row.Order.ActualStartAt,
            row.Order.CompletedAt)).ToList();

        return new PagedResult<ProductionHistoryRow>(items, query.Page, query.PageSize, totalCount);
    }

    /// <summary>How much of each material production has actually taken off the shelf.</summary>
    public async Task<PagedResult<MaterialConsumptionRow>> MaterialConsumptionAsync(
        ProductionTotalsQuery query,
        CancellationToken cancellationToken)
    {
        var grouped = ProductionLines(MovementType.Consumption, query)
            .GroupBy(l => new
            {
                l.ProductId,
                l.Product.Sku,
                ProductName = l.Product.Name,
                UnitOfMeasureCode = l.UnitOfMeasure.Code
            });

        var (field, descending) = SortField(query.Sort);

        var ordered = (field, descending) switch
        {
            ("sku", false) => grouped.OrderBy(g => g.Key.Sku),
            ("sku", true) => grouped.OrderByDescending(g => g.Key.Sku),
            ("quantity", false) => grouped.OrderBy(g => g.Sum(l => l.Quantity)).ThenBy(g => g.Key.Sku),
            // Most consumed first: what a totals report is opened for.
            _ => grouped.OrderByDescending(g => g.Sum(l => l.Quantity)).ThenBy(g => g.Key.Sku)
        };

        var rows = ordered.Select(g => new MaterialConsumptionRow(
            g.Key.ProductId,
            g.Key.Sku,
            g.Key.ProductName,
            g.Key.UnitOfMeasureCode,
            g.Sum(l => l.Quantity),
            g.Count(),
            g.Min(l => l.StockMovement.ConfirmedAt),
            g.Max(l => l.StockMovement.ConfirmedAt)));

        return await PageAsync(rows, query, cancellationToken);
    }

    /// <summary>How much of each product production has made.</summary>
    public async Task<PagedResult<FinishedGoodsRow>> FinishedGoodsAsync(
        ProductionTotalsQuery query,
        CancellationToken cancellationToken)
    {
        var grouped = ProductionLines(MovementType.ProductionOutput, query)
            .GroupBy(l => new
            {
                l.ProductId,
                l.Product.Sku,
                ProductName = l.Product.Name,
                UnitOfMeasureCode = l.UnitOfMeasure.Code
            });

        var (field, descending) = SortField(query.Sort);

        var ordered = (field, descending) switch
        {
            ("sku", false) => grouped.OrderBy(g => g.Key.Sku),
            ("sku", true) => grouped.OrderByDescending(g => g.Key.Sku),
            ("quantity", false) => grouped.OrderBy(g => g.Sum(l => l.Quantity)).ThenBy(g => g.Key.Sku),
            // Most produced first.
            _ => grouped.OrderByDescending(g => g.Sum(l => l.Quantity)).ThenBy(g => g.Key.Sku)
        };

        var rows = ordered.Select(g => new FinishedGoodsRow(
            g.Key.ProductId,
            g.Key.Sku,
            g.Key.ProductName,
            g.Key.UnitOfMeasureCode,
            g.Sum(l => l.Quantity),
            g.Count(),
            g.Min(l => l.StockMovement.ConfirmedAt),
            g.Max(l => l.StockMovement.ConfirmedAt)));

        return await PageAsync(rows, query, cancellationToken);
    }

    /// <summary>
    /// Every confirmed correction of a counted quantity. A surplus has a destination and a
    /// shortage has a source, which is what tells the two apart.
    /// </summary>
    public async Task<PagedResult<AdjustmentRow>> AdjustmentsAsync(
        AdjustmentReportQuery query,
        CancellationToken cancellationToken)
    {
        var lines = db.StockMovementLines
            .Where(l => l.StockMovement.Status == MovementStatus.Confirmed
                        && l.StockMovement.MovementType == MovementType.Adjustment);

        lines = ApplyPeriod(lines, query);

        if (query.IsSurplus is { } surplus)
        {
            lines = surplus
                ? lines.Where(l => l.StockMovement.DestinationLocationId != null)
                : lines.Where(l => l.StockMovement.SourceLocationId != null);
        }

        if (query.LocationId is { } locationId)
        {
            lines = lines.Where(l =>
                l.StockMovement.SourceLocationId == locationId ||
                l.StockMovement.DestinationLocationId == locationId);
        }

        if (query.WarehouseId is { } warehouseId)
        {
            lines = lines.Where(l =>
                (l.StockMovement.SourceLocation != null &&
                 l.StockMovement.SourceLocation.WarehouseId == warehouseId) ||
                (l.StockMovement.DestinationLocation != null &&
                 l.StockMovement.DestinationLocation.WarehouseId == warehouseId));
        }

        var rows = Chronological(lines, query.Sort).Select(l => new AdjustmentRow(
            l.StockMovementId,
            l.StockMovement.Number,
            l.StockMovement.ConfirmedAt!.Value,
            l.ProductId,
            l.Product.Sku,
            l.Product.Name,
            l.BatchId,
            l.Batch!.Number,
            l.StockMovement.DestinationLocationId != null,
            l.Quantity,
            l.UnitOfMeasure.Code,
            l.StockMovement.DestinationLocationId != null
                ? l.StockMovement.DestinationLocationId!.Value
                : l.StockMovement.SourceLocationId!.Value,
            l.StockMovement.DestinationLocationId != null
                ? l.StockMovement.DestinationLocation!.Code
                : l.StockMovement.SourceLocation!.Code,
            l.StockMovement.DestinationLocationId != null
                ? l.StockMovement.DestinationLocation!.WarehouseId
                : l.StockMovement.SourceLocation!.WarehouseId,
            l.StockMovement.DestinationLocationId != null
                ? l.StockMovement.DestinationLocation!.Warehouse.Code
                : l.StockMovement.SourceLocation!.Warehouse.Code,
            l.StockMovement.Reason,
            l.StockMovement.ConfirmedBy));

        return await PageAsync(rows, query, cancellationToken);
    }

    /// <summary>
    /// The confirmed lines of one production movement type over a period, and optionally at one
    /// location — the end of the document that the type actually uses: materials leave the shop
    /// floor, finished goods arrive at the output location.
    /// </summary>
    private IQueryable<StockMovementLine> ProductionLines(
        MovementType movementType,
        ProductionTotalsQuery query)
    {
        var lines = db.StockMovementLines
            .Where(l => l.StockMovement.Status == MovementStatus.Confirmed
                        && l.StockMovement.MovementType == movementType);

        lines = ApplyPeriod(lines, query);

        if (query.LocationId is { } locationId)
        {
            lines = movementType == MovementType.Consumption
                ? lines.Where(l => l.StockMovement.SourceLocationId == locationId)
                : lines.Where(l => l.StockMovement.DestinationLocationId == locationId);
        }

        return lines;
    }

    /// <summary>Newest first, unless the caller asked for the other way round.</summary>
    private static IQueryable<StockMovementLine> Chronological(IQueryable<StockMovementLine> lines, string? sort)
        => !string.IsNullOrWhiteSpace(sort) && !sort.StartsWith('-')
            ? lines.OrderBy(l => l.StockMovement.ConfirmedAt).ThenBy(l => l.StockMovement.Number)
            : lines.OrderByDescending(l => l.StockMovement.ConfirmedAt)
                .ThenByDescending(l => l.StockMovement.Number);

    /// <summary>The product and period filters every history report shares.</summary>
    private static IQueryable<StockMovementLine> ApplyPeriod(
        IQueryable<StockMovementLine> lines,
        HistoryReportQuery query)
    {
        if (query.ProductId is not null)
        {
            lines = lines.Where(l => l.ProductId == query.ProductId);
        }

        if (query.From is not null)
        {
            lines = lines.Where(l => l.StockMovement.ConfirmedAt >= query.From);
        }

        if (query.To is not null)
        {
            lines = lines.Where(l => l.StockMovement.ConfirmedAt < query.To);
        }

        return lines;
    }

    private static (string? Field, bool Descending) SortField(string? sort)
    {
        var descending = sort?.StartsWith('-') == true;

        return ((descending ? sort![1..] : sort)?.Trim().ToLowerInvariant(), descending);
    }

    private static async Task<PagedResult<T>> PageAsync<T>(
        IQueryable<T> rows,
        PagedQuery query,
        CancellationToken cancellationToken)
    {
        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, query.Page, query.PageSize, totalCount);
    }
}
