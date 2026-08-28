using FlowStock.Api.Authorization;
using FlowStock.Application.Common;
using FlowStock.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.Admin)]
public class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<UserResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserResponse>>> List(
        [FromQuery] UserQuery query,
        CancellationToken cancellationToken)
        => Ok(await userService.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await userService.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserResponse>> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserResponse>> Update(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
        => Ok(await userService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/roles")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserResponse>> AssignRoles(
        Guid id,
        AssignRolesRequest request,
        CancellationToken cancellationToken)
        => Ok(await userService.AssignRolesAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
        => Ok(await userService.SetActiveAsync(id, isActive: false, cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserResponse>> Activate(Guid id, CancellationToken cancellationToken)
        => Ok(await userService.SetActiveAsync(id, isActive: true, cancellationToken));
}
