using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Warehouses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// Storage locations are addressed flatly by id, the way stock and movements will address them
/// from Phase 4 on; list them per warehouse with <c>?warehouseId=</c>. Read by any authenticated
/// user, written by Admin (docs/PLAN.md, section 25).
/// </summary>
[ApiController]
[Route("api/storage-locations")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class StorageLocationsController(IStorageLocationService storageLocationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<StorageLocationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<StorageLocationResponse>>> List(
        [FromQuery] StorageLocationQuery query,
        CancellationToken cancellationToken)
        => Ok(await storageLocationService.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<StorageLocationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StorageLocationResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await storageLocationService.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<StorageLocationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StorageLocationResponse>> Create(
        CreateStorageLocationRequest request,
        CancellationToken cancellationToken)
    {
        var location = await storageLocationService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = location.Id }, location);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<StorageLocationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StorageLocationResponse>> Update(
        Guid id,
        UpdateStorageLocationRequest request,
        CancellationToken cancellationToken)
        => Ok(await storageLocationService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<StorageLocationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StorageLocationResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
        => Ok(await storageLocationService.SetActiveAsync(id, isActive: false, cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<StorageLocationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StorageLocationResponse>> Activate(Guid id, CancellationToken cancellationToken)
        => Ok(await storageLocationService.SetActiveAsync(id, isActive: true, cancellationToken));
}
