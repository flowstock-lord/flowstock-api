using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FlowStock.Application.Common;
using Microsoft.AspNetCore.Http;

namespace FlowStock.Api;

public class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;

            var value = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
