using FlowStock.Application.Common;
using FlowStock.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace FlowStock.Infrastructure.Identity;

/// <summary>
/// PBKDF2 hashing from ASP.NET Core Identity, without pulling in the Identity stores or schema.
/// Plain-text passwords never leave this class.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(new User(), password);

    public bool Verify(string hash, string password)
    {
        var result = _hasher.VerifyHashedPassword(new User(), hash, password);

        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
