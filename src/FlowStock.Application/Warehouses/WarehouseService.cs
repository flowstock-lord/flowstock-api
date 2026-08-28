using FlowStock.Application.Common;
using FlowStock.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Warehouses;

public interface IWarehouseService
{
    Task<PagedResult<WarehouseResponse>> ListAsync(WarehouseQuery query, CancellationToken cancellationToken);

    Task<WarehouseResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<WarehouseResponse> CreateAsync(CreateWarehouseRequest request, CancellationToken cancellationToken);

    Task<WarehouseResponse> UpdateAsync(Guid id, UpdateWarehouseRequest request, CancellationToken cancellationToken);

    Task<WarehouseResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
}

public class WarehouseService(
    IFlowStockDbContext db,
    ILogger<WarehouseService> logger) : IWarehouseService
{
    public async Task<PagedResult<WarehouseResponse>> ListAsync(
        WarehouseQuery query,
        CancellationToken cancellationToken)
    {
        var warehouses = db.Warehouses.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            warehouses = warehouses.Where(w =>
                w.Code.ToLower().Contains(search) ||
                w.Name.ToLower().Contains(search));
        }

        if (query.WarehouseType is not null)
        {
            warehouses = warehouses.Where(w => w.WarehouseType == query.WarehouseType);
        }

        if (query.IsActive is not null)
        {
            warehouses = warehouses.Where(w => w.IsActive == query.IsActive);
        }

        var totalCount = await warehouses.CountAsync(cancellationToken);

        var items = await Sort(warehouses, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(w => new WarehouseResponse(
                w.Id,
                w.Code,
                w.Name,
                w.Description,
                w.WarehouseType,
                w.IsActive,
                w.Locations.Count,
                w.CreatedAt,
                w.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<WarehouseResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<WarehouseResponse> GetAsync(Guid id, CancellationToken cancellationToken)
        => ToResponse(await FindAsync(id, cancellationToken));

    public async Task<WarehouseResponse> CreateAsync(
        CreateWarehouseRequest request,
        CancellationToken cancellationToken)
    {
        var code = Warehouse.NormalizeCode(request.Code);

        if (await db.Warehouses.AnyAsync(w => w.Code == code, cancellationToken))
        {
            throw new WarehouseCodeAlreadyExistsException(code);
        }

        var warehouse = new Warehouse
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            WarehouseType = request.WarehouseType,
            IsActive = true
        };

        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Warehouse {WarehouseId} created with code {Code} of type {WarehouseType}",
            warehouse.Id, warehouse.Code, warehouse.WarehouseType);

        return ToResponse(warehouse);
    }

    public async Task<WarehouseResponse> UpdateAsync(
        Guid id,
        UpdateWarehouseRequest request,
        CancellationToken cancellationToken)
    {
        var warehouse = await FindAsync(id, cancellationToken);

        warehouse.Name = request.Name.Trim();
        warehouse.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        warehouse.WarehouseType = request.WarehouseType;

        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(warehouse);
    }

    public async Task<WarehouseResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var warehouse = await FindAsync(id, cancellationToken);

        warehouse.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Warehouse {WarehouseId} active flag set to {IsActive}", warehouse.Id, isActive);

        return ToResponse(warehouse);
    }

    private async Task<Warehouse> FindAsync(Guid id, CancellationToken cancellationToken)
        => await db.Warehouses
               .Include(w => w.Locations)
               .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
           ?? throw new WarehouseNotFoundException(id);

    private static IQueryable<Warehouse> Sort(IQueryable<Warehouse> warehouses, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        var field = (descending ? sort![1..] : sort)?.Trim().ToLowerInvariant();

        return (field, descending) switch
        {
            ("name", false) => warehouses.OrderBy(w => w.Name),
            ("name", true) => warehouses.OrderByDescending(w => w.Name),
            ("type", false) => warehouses.OrderBy(w => w.WarehouseType).ThenBy(w => w.Code),
            ("type", true) => warehouses.OrderByDescending(w => w.WarehouseType).ThenBy(w => w.Code),
            ("createdat", false) => warehouses.OrderBy(w => w.CreatedAt),
            ("createdat", true) => warehouses.OrderByDescending(w => w.CreatedAt),
            (_, true) => warehouses.OrderByDescending(w => w.Code),
            _ => warehouses.OrderBy(w => w.Code)
        };
    }

    private static WarehouseResponse ToResponse(Warehouse warehouse) => new(
        warehouse.Id,
        warehouse.Code,
        warehouse.Name,
        warehouse.Description,
        warehouse.WarehouseType,
        warehouse.IsActive,
        warehouse.Locations.Count,
        warehouse.CreatedAt,
        warehouse.UpdatedAt);
}
