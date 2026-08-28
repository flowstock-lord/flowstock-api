using FlowStock.Domain.Users;

namespace FlowStock.Application.Common;

/// <summary>Hashes and verifies user passwords. Implemented in Infrastructure.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hash, string password);
}

/// <summary>Issues signed access tokens for authenticated users.</summary>
public interface IJwtTokenGenerator
{
    AccessToken Generate(User user, IReadOnlyCollection<string> roles);
}

/// <param name="Token">The signed JWT.</param>
/// <param name="ExpiresAtUtc">Absolute expiry, UTC.</param>
public record AccessToken(string Token, DateTime ExpiresAtUtc);

/// <summary>The user behind the current request, if any. Used for audit stamping.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}
