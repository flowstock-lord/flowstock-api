using FlowStock.Application.Common;
using FlowStock.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Catalog;

public interface IUnitOfMeasureService
{
    Task<PagedResult<UnitOfMeasureResponse>> ListAsync(UnitOfMeasureQuery query, CancellationToken cancellationToken);

    Task<UnitOfMeasureResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<UnitOfMeasureResponse> CreateAsync(CreateUnitOfMeasureRequest request, CancellationToken cancellationToken);

    Task<UnitOfMeasureResponse> UpdateAsync(Guid id, UpdateUnitOfMeasureRequest request, CancellationToken cancellationToken);

    Task<UnitOfMeasureResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
}

public class UnitOfMeasureService(
    IFlowStockDbContext db,
    ILogger<UnitOfMeasureService> logger) : IUnitOfMeasureService
{
    public async Task<PagedResult<UnitOfMeasureResponse>> ListAsync(
        UnitOfMeasureQuery query,
        CancellationToken cancellationToken)
    {
        var units = db.UnitsOfMeasure.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            units = units.Where(u => u.Code.Contains(search) || u.Name.ToLower().Contains(search));
        }

        if (query.IsActive is not null)
        {
            units = units.Where(u => u.IsActive == query.IsActive);
        }

        var totalCount = await units.CountAsync(cancellationToken);

        var items = await units
            .OrderBy(u => u.Code)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<UnitOfMeasureResponse>(
            items.Select(ToResponse).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<UnitOfMeasureResponse> GetAsync(Guid id, CancellationToken cancellationToken)
        => ToResponse(await FindAsync(id, cancellationToken));

    public async Task<UnitOfMeasureResponse> CreateAsync(
        CreateUnitOfMeasureRequest request,
        CancellationToken cancellationToken)
    {
        var code = UnitOfMeasure.NormalizeCode(request.Code);

        if (await db.UnitsOfMeasure.AnyAsync(u => u.Code == code, cancellationToken))
        {
            throw new UnitOfMeasureCodeAlreadyExistsException(code);
        }

        var unit = new UnitOfMeasure
        {
            Code = code,
            Name = request.Name.Trim(),
            IsActive = true
        };

        db.UnitsOfMeasure.Add(unit);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Unit of measure {UnitOfMeasureId} created with code {Code}", unit.Id, unit.Code);

        return ToResponse(unit);
    }

    public async Task<UnitOfMeasureResponse> UpdateAsync(
        Guid id,
        UpdateUnitOfMeasureRequest request,
        CancellationToken cancellationToken)
    {
        var unit = await FindAsync(id, cancellationToken);

        unit.Name = request.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(unit);
    }

    public async Task<UnitOfMeasureResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var unit = await FindAsync(id, cancellationToken);

        unit.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Unit of measure {UnitOfMeasureId} active flag set to {IsActive}", unit.Id, isActive);

        return ToResponse(unit);
    }

    private async Task<UnitOfMeasure> FindAsync(Guid id, CancellationToken cancellationToken)
        => await db.UnitsOfMeasure.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
           ?? throw new UnitOfMeasureNotFoundException(id);

    private static UnitOfMeasureResponse ToResponse(UnitOfMeasure unit) => new(
        unit.Id,
        unit.Code,
        unit.Name,
        unit.IsActive,
        unit.CreatedAt,
        unit.UpdatedAt);
}
