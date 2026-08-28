using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// Production orders (docs/PLAN.md, sections 15 to 18). Readable by every authenticated user —
/// production history is part of the audit trail — while running an order needs Admin or
/// ProductionManager (docs/PLAN.md, section 25).
///
/// The workflow is <c>Draft → Planned → InProgress → Completed</c>. Each step posts its own
/// confirmed stock movements, so stock never changes outside the inventory history.
/// </summary>
[ApiController]
[Route("api/production-orders")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class ProductionOrdersController(IProductionOrderService productionOrders) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ProductionOrderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductionOrderResponse>>> List(
        [FromQuery] ProductionOrderQuery query,
        CancellationToken cancellationToken)
        => Ok(await productionOrders.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductionOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductionOrderResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await productionOrders.GetAsync(id, cancellationToken));

    /// <summary>Writes down a run as a draft, with the materials scaled from the recipe.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Production)]
    [ProducesResponseType<ProductionOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductionOrderResponse>> Create(
        CreateProductionOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await productionOrders.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Reserves the materials at the production location. Nothing leaves stock yet.</summary>
    [HttpPost("{id:guid}/plan")]
    [Authorize(Policy = Policies.Production)]
    [ProducesResponseType<ProductionOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductionOrderResponse>> Plan(Guid id, CancellationToken cancellationToken)
        => Ok(await productionOrders.PlanAsync(id, cancellationToken));

    /// <summary>Starts the run: the reserved materials are consumed by a confirmed movement.</summary>
    [HttpPost("{id:guid}/start")]
    [Authorize(Policy = Policies.Production)]
    [ProducesResponseType<ProductionOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductionOrderResponse>> Start(Guid id, CancellationToken cancellationToken)
        => Ok(await productionOrders.StartAsync(id, cancellationToken));

    /// <summary>Books the finished goods into the output location and closes the run.</summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = Policies.Production)]
    [ProducesResponseType<ProductionOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductionOrderResponse>> Complete(
        Guid id,
        CompleteProductionOrderRequest request,
        CancellationToken cancellationToken)
        => Ok(await productionOrders.CompleteAsync(id, request, cancellationToken));

    /// <summary>
    /// Abandons a run that has not consumed anything yet, releasing its reservations. A started
    /// run is corrected with compensating stock movements instead.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.Production)]
    [ProducesResponseType<ProductionOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductionOrderResponse>> Cancel(
        Guid id,
        CancelProductionOrderRequest request,
        CancellationToken cancellationToken)
        => Ok(await productionOrders.CancelAsync(id, request, cancellationToken));
}
