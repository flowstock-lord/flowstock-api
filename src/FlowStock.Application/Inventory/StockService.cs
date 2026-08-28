using FlowStock.Application.Common;
using FlowStock.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace FlowStock.Application.Inventory;

public interface IStockService
{
    Task<PagedResult<StockResponse>> ListAsync(StockQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Reads inventory balances. Deliberately read-only: the only way stock changes is a confirmed
/// stock movement (CLAUDE.md, rule 1).
/// </summary>
public class StockService(IFlowStockDbContext db) : IStockService
{
    public async Task<PagedResult<StockResponse>> ListAsync(StockQuery query, CancellationToken cancellationToken)
    {
        var stocks = db.Stocks
            .Include(s => s.Product).ThenInclude(p => p.UnitOfMeasure)
            .Include(s => s.Location).ThenInclude(l => l.Warehouse)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            stocks = stocks.Where(s =>
                s.Product.Sku.ToLower().Contains(search) ||
                s.Product.Name.ToLower().Contains(search));
        }

        if (query.ProductId is not null)
        {
            stocks = stocks.Where(s => s.ProductId == query.ProductId);
        }

        if (query.LocationId is not null)
        {
            stocks = stocks.Where(s => s.LocationId == query.LocationId);
        }

        if (query.WarehouseId is not null)
        {
            stocks = stocks.Where(s => s.Location.WarehouseId == query.WarehouseId);
        }

        if (query.OnlyInStock == true)
        {
            stocks = stocks.Where(s => s.Quantity > 0);
        }

        var totalCount = await stocks.CountAsync(cancellationToken);

        var items = await Sort(stocks, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<StockResponse>(
            items.Select(ToResponse).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    private static IQueryable<Stock> Sort(IQueryable<Stock> stocks, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        var field = (descending ? sort![1..] : sort)?.Trim().ToLowerInvariant();

        return (field, descending) switch
        {
            ("location", false) => stocks.OrderBy(s => s.Location.Warehouse.Code)
                .ThenBy(s => s.Location.Code).ThenBy(s => s.Product.Sku),
            ("location", true) => stocks.OrderByDescending(s => s.Location.Warehouse.Code)
                .ThenByDescending(s => s.Location.Code).ThenBy(s => s.Product.Sku),
            ("quantity", false) => stocks.OrderBy(s => s.Quantity).ThenBy(s => s.Product.Sku),
            ("quantity", true) => stocks.OrderByDescending(s => s.Quantity).ThenBy(s => s.Product.Sku),
            (_, true) => stocks.OrderByDescending(s => s.Product.Sku).ThenBy(s => s.Location.Code),
            _ => stocks.OrderBy(s => s.Product.Sku).ThenBy(s => s.Location.Code)
        };
    }

    private static StockResponse ToResponse(Stock stock) => new(
        stock.Id,
        stock.ProductId,
        stock.Product.Sku,
        stock.Product.Name,
        stock.Product.UnitOfMeasure.Code,
        stock.LocationId,
        stock.Location.Code,
        stock.Location.WarehouseId,
        stock.Location.Warehouse.Code,
        stock.Quantity,
        stock.ReservedQuantity,
        stock.AvailableQuantity,
        stock.UpdatedAt);
}
