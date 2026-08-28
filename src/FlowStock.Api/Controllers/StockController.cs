using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// Current inventory balances (docs/PLAN.md, section 21). Read-only by design: stock changes
/// only through a confirmed movement, so there is nothing to write here.
/// </summary>
[ApiController]
[Route("api/stock")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class StockController(IStockService stockService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<StockResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<StockResponse>>> List(
        [FromQuery] StockQuery query,
        CancellationToken cancellationToken)
        => Ok(await stockService.ListAsync(query, cancellationToken));

    /// <summary>Where one product currently sits, location by location.</summary>
    [HttpGet("{productId:guid}")]
    [ProducesResponseType<PagedResult<StockResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<StockResponse>>> ByProduct(
        Guid productId,
        [FromQuery] StockQuery query,
        CancellationToken cancellationToken)
    {
        query.ProductId = productId;

        return Ok(await stockService.ListAsync(query, cancellationToken));
    }

    /// <summary>What one location currently holds.</summary>
    [HttpGet("by-location/{locationId:guid}")]
    [ProducesResponseType<PagedResult<StockResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<StockResponse>>> ByLocation(
        Guid locationId,
        [FromQuery] StockQuery query,
        CancellationToken cancellationToken)
    {
        query.LocationId = locationId;

        return Ok(await stockService.ListAsync(query, cancellationToken));
    }
}
