using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FlowStock.Application.Authentication;
using FlowStock.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthenticationService authenticationService) : ControllerBase
{
    /// <summary>Exchanges credentials for a JWT access token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginAsync(request, cancellationToken);

        if (!result.Succeeded)
        {
            var message = result.ErrorCode == ErrorCodes.UserInactive
                ? "This account is deactivated."
                : "Invalid email or password.";

            return Unauthorized(new ErrorResponse(result.ErrorCode!, message));
        }

        return Ok(result.Response);
    }

    /// <summary>Returns the authenticated user described by the current token.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<CurrentUserResponse> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Ok(new CurrentUserResponse(
            Guid.Parse(userId!),
            User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty,
            User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(JwtRegisteredClaimNames.Name) ?? string.Empty,
            User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).OrderBy(role => role).ToArray()));
    }
}
