using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Traceability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// Traceability (docs/PLAN.md, sections 19 and 39). Read-only by design: these endpoints only
/// read the transaction history back, they are never another way to change stock.
///
/// Open to any authenticated user, like the rest of the audit trail.
/// </summary>
[ApiController]
[Route("api/traceability")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class TraceabilityController(ITraceabilityService traceability) : ControllerBase
{
    /// <summary>
    /// Where a product came from and where it went: every confirmed movement that touched it,
    /// newest first, with who did it and when. Filter with <c>locationId</c> to read the history
    /// of one location, and the direction is then relative to that location.
    /// </summary>
    [HttpGet("products/{productId:guid}/history")]
    [ProducesResponseType<PagedResult<ProductHistoryEntry>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductHistoryEntry>>> ProductHistory(
        Guid productId,
        [FromQuery] ProductHistoryQuery query,
        CancellationToken cancellationToken)
        => Ok(await traceability.ProductHistoryAsync(productId, query, cancellationToken));

    /// <summary>
    /// Forward traceability: which production runs consumed this material, and what they produced.
    /// </summary>
    [HttpGet("products/{productId:guid}/usage")]
    [ProducesResponseType<PagedResult<MaterialUsageEntry>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<MaterialUsageEntry>>> MaterialUsage(
        Guid productId,
        [FromQuery] MaterialUsageQuery query,
        CancellationToken cancellationToken)
        => Ok(await traceability.MaterialUsageAsync(productId, query, cancellationToken));

    /// <summary>
    /// Backward traceability: what one production run was made of — recipe version, materials,
    /// the movements that consumed them, where those materials came from, and where the finished
    /// goods went.
    /// </summary>
    [HttpGet("production-orders/{productionOrderId:guid}")]
    [ProducesResponseType<ProductionTraceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductionTraceResponse>> ProductionTrace(
        Guid productionOrderId,
        CancellationToken cancellationToken)
        => Ok(await traceability.ProductionTraceAsync(productionOrderId, cancellationToken));
}
