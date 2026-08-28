using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// Notifications (docs/PLAN.md, section 31). What the system noticed: a run that finished, a
/// transfer that arrived, a lot that went out of date, a run the shop floor cannot feed.
///
/// They belong to the team rather than to a person — there are no per-user inboxes in this phase —
/// so any authenticated user may read them and mark them read.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<NotificationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<NotificationResponse>>> List(
        [FromQuery] NotificationQuery query,
        CancellationToken cancellationToken)
        => Ok(await notifications.ListAsync(query, cancellationToken));

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationResponse>> MarkRead(Guid id, CancellationToken cancellationToken)
        => Ok(await notifications.MarkReadAsync(id, isRead: true, cancellationToken));

    /// <summary>Puts one back in the unread pile.</summary>
    [HttpPost("{id:guid}/unread")]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationResponse>> MarkUnread(Guid id, CancellationToken cancellationToken)
        => Ok(await notifications.MarkReadAsync(id, isRead: false, cancellationToken));

    /// <summary>
    /// Runs the checks that no single operation can raise — expired lots, draft runs the shop floor
    /// cannot feed — and records whatever is new. The API also runs this on a timer; this endpoint
    /// is for running it deliberately, and needs Admin.
    /// </summary>
    [HttpPost("scan")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<NotificationScanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationScanResponse>> Scan(CancellationToken cancellationToken)
        => Ok(await notifications.ScanAsync(cancellationToken));
}
