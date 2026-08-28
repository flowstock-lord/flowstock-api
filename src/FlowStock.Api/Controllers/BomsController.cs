using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// Bills of materials (docs/PLAN.md, section 21). Readable by every authenticated user; managing
/// them is a production responsibility, so writes need Admin or ProductionManager
/// (docs/PLAN.md, section 25).
///
/// A published version is immutable apart from its labelling: a changed recipe is a new version,
/// so the orders built from an older one can still show what they actually used.
/// </summary>
[ApiController]
[Route("api/boms")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class BomsController(IBillOfMaterialService billOfMaterialService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<BillOfMaterialResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<BillOfMaterialResponse>>> List(
        [FromQuery] BillOfMaterialQuery query,
        CancellationToken cancellationToken)
        => Ok(await billOfMaterialService.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BillOfMaterialResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BillOfMaterialResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await billOfMaterialService.GetAsync(id, cancellationToken));

    /// <summary>What producing <c>quantity</c> of the product would consume, per this version.</summary>
    [HttpGet("{id:guid}/requirements")]
    [ProducesResponseType<MaterialRequirementsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MaterialRequirementsResponse>> Requirements(
        Guid id,
        [FromQuery] MaterialRequirementsQuery query,
        CancellationToken cancellationToken)
        => Ok(await billOfMaterialService.CalculateRequirementsAsync(id, query.Quantity, cancellationToken));

    /// <summary>Publishes the next version of a product's recipe and makes it the active one.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Production)]
    [ProducesResponseType<BillOfMaterialResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BillOfMaterialResponse>> Create(
        CreateBillOfMaterialRequest request,
        CancellationToken cancellationToken)
    {
        var bom = await billOfMaterialService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = bom.Id }, bom);
    }

    /// <summary>Renames or re-describes a version. The components never change.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Production)]
    [ProducesResponseType<BillOfMaterialResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BillOfMaterialResponse>> Update(
        Guid id,
        UpdateBillOfMaterialRequest request,
        CancellationToken cancellationToken)
        => Ok(await billOfMaterialService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.Production)]
    [ProducesResponseType<BillOfMaterialResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BillOfMaterialResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
        => Ok(await billOfMaterialService.SetActiveAsync(id, isActive: false, cancellationToken));

    /// <summary>Puts an older version back in force, standing the current one down.</summary>
    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.Production)]
    [ProducesResponseType<BillOfMaterialResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BillOfMaterialResponse>> Activate(Guid id, CancellationToken cancellationToken)
        => Ok(await billOfMaterialService.SetActiveAsync(id, isActive: true, cancellationToken));
}
