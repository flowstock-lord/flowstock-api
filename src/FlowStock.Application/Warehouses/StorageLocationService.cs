using FlowStock.Application.Common;
using FlowStock.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Warehouses;

public interface IStorageLocationService
{
    Task<PagedResult<StorageLocationResponse>> ListAsync(
        StorageLocationQuery query,
        CancellationToken cancellationToken);

    Task<StorageLocationResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<StorageLocationResponse> CreateAsync(
        CreateStorageLocationRequest request,
        CancellationToken cancellationToken);

    Task<StorageLocationResponse> UpdateAsync(
        Guid id,
        UpdateStorageLocationRequest request,
        CancellationToken cancellationToken);

    Task<StorageLocationResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
}

public class StorageLocationService(
    IFlowStockDbContext db,
    ILogger<StorageLocationService> logger) : IStorageLocationService
{
    public async Task<PagedResult<StorageLocationResponse>> ListAsync(
        StorageLocationQuery query,
        CancellationToken cancellationToken)
    {
        var locations = db.StorageLocations
            .Include(l => l.Warehouse)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            locations = locations.Where(l =>
                l.Code.ToLower().Contains(search) ||
                l.Name.ToLower().Contains(search));
        }

        if (query.WarehouseId is not null)
        {
            locations = locations.Where(l => l.WarehouseId == query.WarehouseId);
        }

        if (query.IsActive is not null)
        {
            locations = locations.Where(l => l.IsActive == query.IsActive);
        }

        var totalCount = await locations.CountAsync(cancellationToken);

        var items = await Sort(locations, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<StorageLocationResponse>(
            items.Select(ToResponse).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<StorageLocationResponse> GetAsync(Guid id, CancellationToken cancellationToken)
        => ToResponse(await FindAsync(id, cancellationToken));

    public async Task<StorageLocationResponse> CreateAsync(
        CreateStorageLocationRequest request,
        CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses
                            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId, cancellationToken)
                        ?? throw new WarehouseNotFoundException(request.WarehouseId);

        if (!warehouse.IsActive)
        {
            throw new WarehouseInactiveException(warehouse.Id, warehouse.Code);
        }

        var code = StorageLocation.NormalizeCode(request.Code);

        // Codes are unique inside a warehouse, not globally: A-01 may exist in several warehouses.
        if (await db.StorageLocations.AnyAsync(
                l => l.WarehouseId == warehouse.Id && l.Code == code, cancellationToken))
        {
            throw new LocationCodeAlreadyExistsException(warehouse.Id, code);
        }

        var location = new StorageLocation
        {
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Code = code,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true
        };

        db.StorageLocations.Add(location);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Storage location {LocationId} created as {WarehouseCode}/{Code}",
            location.Id, warehouse.Code, location.Code);

        return ToResponse(location);
    }

    public async Task<StorageLocationResponse> UpdateAsync(
        Guid id,
        UpdateStorageLocationRequest request,
        CancellationToken cancellationToken)
    {
        var location = await FindAsync(id, cancellationToken);

        location.Name = request.Name.Trim();
        location.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(location);
    }

    public async Task<StorageLocationResponse> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var location = await FindAsync(id, cancellationToken);

        location.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Storage location {LocationId} active flag set to {IsActive}", location.Id, isActive);

        return ToResponse(location);
    }

    private async Task<StorageLocation> FindAsync(Guid id, CancellationToken cancellationToken)
        => await db.StorageLocations
               .Include(l => l.Warehouse)
               .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
           ?? throw new LocationNotFoundException(id);

    private static IQueryable<StorageLocation> Sort(IQueryable<StorageLocation> locations, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        var field = (descending ? sort![1..] : sort)?.Trim().ToLowerInvariant();

        return (field, descending) switch
        {
            ("name", false) => locations.OrderBy(l => l.Name),
            ("name", true) => locations.OrderByDescending(l => l.Name),
            ("createdat", false) => locations.OrderBy(l => l.CreatedAt),
            ("createdat", true) => locations.OrderByDescending(l => l.CreatedAt),
            (_, true) => locations.OrderByDescending(l => l.Warehouse.Code).ThenByDescending(l => l.Code),
            _ => locations.OrderBy(l => l.Warehouse.Code).ThenBy(l => l.Code)
        };
    }

    private static StorageLocationResponse ToResponse(StorageLocation location) => new(
        location.Id,
        location.WarehouseId,
        location.Warehouse.Code,
        location.Code,
        location.Name,
        location.Description,
        location.IsActive,
        location.CreatedAt,
        location.UpdatedAt);
}
