using FlowStock.Api.Authorization;
using FlowStock.Application.Catalog;
using FlowStock.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// Units of measure are master data: every authenticated user may read them, only Admin may
/// change them (docs/PLAN.md, section 25 — full access belongs to Admin alone).
/// </summary>
[ApiController]
[Route("api/units-of-measure")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class UnitsOfMeasureController(IUnitOfMeasureService unitOfMeasureService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<UnitOfMeasureResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UnitOfMeasureResponse>>> List(
        [FromQuery] UnitOfMeasureQuery query,
        CancellationToken cancellationToken)
        => Ok(await unitOfMeasureService.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UnitOfMeasureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UnitOfMeasureResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await unitOfMeasureService.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<UnitOfMeasureResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UnitOfMeasureResponse>> Create(
        CreateUnitOfMeasureRequest request,
        CancellationToken cancellationToken)
    {
        var unit = await unitOfMeasureService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = unit.Id }, unit);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<UnitOfMeasureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UnitOfMeasureResponse>> Update(
        Guid id,
        UpdateUnitOfMeasureRequest request,
        CancellationToken cancellationToken)
        => Ok(await unitOfMeasureService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<UnitOfMeasureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UnitOfMeasureResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
        => Ok(await unitOfMeasureService.SetActiveAsync(id, isActive: false, cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<UnitOfMeasureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UnitOfMeasureResponse>> Activate(Guid id, CancellationToken cancellationToken)
        => Ok(await unitOfMeasureService.SetActiveAsync(id, isActive: true, cancellationToken));
}
