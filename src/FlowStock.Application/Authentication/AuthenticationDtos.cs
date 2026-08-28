namespace FlowStock.Application.Authentication;

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    Guid UserId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles);

public record CurrentUserResponse(
    Guid UserId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles);

/// <summary>
/// Login outcome. Failures are returned rather than thrown: the exception middleware maps
/// domain errors to 400, while a rejected login must answer 401.
/// </summary>
public record LoginResult(LoginResponse? Response, string? ErrorCode)
{
    public bool Succeeded => Response is not null;

    public static LoginResult Success(LoginResponse response) => new(response, null);

    public static LoginResult Failure(string errorCode) => new(null, errorCode);
}
