using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Warehouses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// Warehouses are master data, like the catalogue: readable by every authenticated user, writable
/// by Admin only (docs/PLAN.md, section 25). They are deactivated, never deleted — stock history
/// addresses them forever.
/// </summary>
[ApiController]
[Route("api/warehouses")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class WarehousesController(IWarehouseService warehouseService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<WarehouseResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<WarehouseResponse>>> List(
        [FromQuery] WarehouseQuery query,
        CancellationToken cancellationToken)
        => Ok(await warehouseService.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WarehouseResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await warehouseService.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WarehouseResponse>> Create(
        CreateWarehouseRequest request,
        CancellationToken cancellationToken)
    {
        var warehouse = await warehouseService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = warehouse.Id }, warehouse);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WarehouseResponse>> Update(
        Guid id,
        UpdateWarehouseRequest request,
        CancellationToken cancellationToken)
        => Ok(await warehouseService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WarehouseResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
        => Ok(await warehouseService.SetActiveAsync(id, isActive: false, cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WarehouseResponse>> Activate(Guid id, CancellationToken cancellationToken)
        => Ok(await warehouseService.SetActiveAsync(id, isActive: true, cancellationToken));
}
