using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// Stock movements: the only way inventory ever changes (docs/PLAN.md, section 21).
///
/// Everyone authenticated may read the movement history — it is the audit trail. Creating,
/// confirming and cancelling movements is a warehouse operation, so it needs Admin or
/// WarehouseManager (docs/PLAN.md, section 25). There is no update and no delete: a draft is
/// cancelled, and a confirmed movement is corrected by a compensating one.
/// </summary>
[ApiController]
[Route("api/stock-movements")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class StockMovementsController(IStockMovementService stockMovementService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<StockMovementResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<StockMovementResponse>>> List(
        [FromQuery] StockMovementQuery query,
        CancellationToken cancellationToken)
        => Ok(await stockMovementService.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<StockMovementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockMovementResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await stockMovementService.GetAsync(id, cancellationToken));

    /// <summary>Creates a draft. Stock is untouched until the movement is confirmed.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Warehouse)]
    [ProducesResponseType<StockMovementResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockMovementResponse>> Create(
        CreateStockMovementRequest request,
        CancellationToken cancellationToken)
    {
        var movement = await stockMovementService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = movement.Id }, movement);
    }

    /// <summary>Applies the whole document to stock, atomically.</summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = Policies.Warehouse)]
    [ProducesResponseType<StockMovementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockMovementResponse>> Confirm(Guid id, CancellationToken cancellationToken)
        => Ok(await stockMovementService.ConfirmAsync(id, cancellationToken));

    /// <summary>Closes a draft that will never happen. Confirmed movements cannot be cancelled.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.Warehouse)]
    [ProducesResponseType<StockMovementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockMovementResponse>> Cancel(
        Guid id,
        CancelStockMovementRequest request,
        CancellationToken cancellationToken)
        => Ok(await stockMovementService.CancelAsync(id, request, cancellationToken));
}
