using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// Batches, the lots goods arrive and are made in (docs/PLAN.md, section 20). Readable by every
/// authenticated user; registering one is a warehouse operation, not catalogue maintenance, so
/// writes need Admin or WarehouseManager (docs/PLAN.md, section 25).
///
/// A batch identifies goods, it does not hold them: how much of a lot is left and where is a stock
/// balance, read from <c>/api/stock?batchId=</c>, and it changes only through confirmed movements.
/// </summary>
[ApiController]
[Route("api/batches")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class BatchesController(IBatchService batchService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<BatchResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<BatchResponse>>> List(
        [FromQuery] BatchQuery query,
        CancellationToken cancellationToken)
        => Ok(await batchService.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BatchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await batchService.GetAsync(id, cancellationToken));

    /// <summary>
    /// Registers a lot that has arrived, before the receipt that books it in. Only a batch-tracked
    /// product has lots.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Policies.Warehouse)]
    [ProducesResponseType<BatchResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchResponse>> Create(
        CreateBatchRequest request,
        CancellationToken cancellationToken)
    {
        var batch = await batchService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = batch.Id }, batch);
    }

    /// <summary>
    /// Corrects what is known about a lot — supplier, dates, notes. The number and the product
    /// never change: history named them.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Warehouse)]
    [ProducesResponseType<BatchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchResponse>> Update(
        Guid id,
        UpdateBatchRequest request,
        CancellationToken cancellationToken)
        => Ok(await batchService.UpdateAsync(id, request, cancellationToken));
}
