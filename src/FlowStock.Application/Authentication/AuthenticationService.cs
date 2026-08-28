using FlowStock.Application.Common;
using FlowStock.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Authentication;

public interface IAuthenticationService
{
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}

public class AuthenticationService(
    IFlowStockDbContext db,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator tokenGenerator,
    ILogger<AuthenticationService> logger) : IAuthenticationService
{
    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);

        var user = await db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            // Never reveal whether the email exists.
            logger.LogWarning("Authentication failed: unknown email {Email}", email);
            return LoginResult.Failure(ErrorCodes.InvalidCredentials);
        }

        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            logger.LogWarning("Authentication failed: wrong password for user {UserId}", user.Id);
            return LoginResult.Failure(ErrorCodes.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Authentication failed: inactive user {UserId}", user.Id);
            return LoginResult.Failure(ErrorCodes.UserInactive);
        }

        var roles = user.UserRoles.Select(ur => ur.Role.Name).OrderBy(name => name).ToArray();
        var token = tokenGenerator.Generate(user, roles);

        logger.LogInformation("User {UserId} authenticated with roles {Roles}", user.Id, roles);

        return LoginResult.Success(new LoginResponse(
            token.Token,
            token.ExpiresAtUtc,
            user.Id,
            user.Email,
            user.FullName,
            roles));
    }
}
