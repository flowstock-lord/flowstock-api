using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// The basic reports of docs/PLAN.md, section 30. All read-only, all derived from balances,
/// confirmed movements and production orders — a report never changes stock and never becomes a
/// second version of the truth.
///
/// Open to any authenticated user, like the rest of the audit trail.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class ReportsController(IReportingService reporting) : ControllerBase
{
    /// <summary>What every product holds right now, across all its locations and lots.</summary>
    [HttpGet("current-stock")]
    [ProducesResponseType<PagedResult<CurrentStockRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<CurrentStockRow>>> CurrentStock(
        [FromQuery] CurrentStockQuery query,
        CancellationToken cancellationToken)
        => Ok(await reporting.CurrentStockAsync(query, cancellationToken));

    /// <summary>The same balances, split by the warehouse that holds them.</summary>
    [HttpGet("stock-by-warehouse")]
    [ProducesResponseType<PagedResult<WarehouseStockRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<WarehouseStockRow>>> StockByWarehouse(
        [FromQuery] WarehouseStockQuery query,
        CancellationToken cancellationToken)
        => Ok(await reporting.StockByWarehouseAsync(query, cancellationToken));

    /// <summary>The movement journal read line by line, over a period.</summary>
    [HttpGet("movement-history")]
    [ProducesResponseType<PagedResult<MovementHistoryRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<MovementHistoryRow>>> MovementHistory(
        [FromQuery] MovementHistoryQuery query,
        CancellationToken cancellationToken)
        => Ok(await reporting.MovementHistoryAsync(query, cancellationToken));

    /// <summary>Every production run, with what it planned, what it yielded and when.</summary>
    [HttpGet("production-history")]
    [ProducesResponseType<PagedResult<ProductionHistoryRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductionHistoryRow>>> ProductionHistory(
        [FromQuery] ProductionHistoryQuery query,
        CancellationToken cancellationToken)
        => Ok(await reporting.ProductionHistoryAsync(query, cancellationToken));

    /// <summary>How much of each material production has taken off the shelf.</summary>
    [HttpGet("material-consumption")]
    [ProducesResponseType<PagedResult<MaterialConsumptionRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<MaterialConsumptionRow>>> MaterialConsumption(
        [FromQuery] ProductionTotalsQuery query,
        CancellationToken cancellationToken)
        => Ok(await reporting.MaterialConsumptionAsync(query, cancellationToken));

    /// <summary>How much of each product production has made.</summary>
    [HttpGet("finished-goods")]
    [ProducesResponseType<PagedResult<FinishedGoodsRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<FinishedGoodsRow>>> FinishedGoods(
        [FromQuery] ProductionTotalsQuery query,
        CancellationToken cancellationToken)
        => Ok(await reporting.FinishedGoodsAsync(query, cancellationToken));

    /// <summary>Every confirmed correction of a counted quantity, surplus or shortage.</summary>
    [HttpGet("adjustments")]
    [ProducesResponseType<PagedResult<AdjustmentRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<AdjustmentRow>>> Adjustments(
        [FromQuery] AdjustmentReportQuery query,
        CancellationToken cancellationToken)
        => Ok(await reporting.AdjustmentsAsync(query, cancellationToken));
}
