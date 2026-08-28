using FlowStock.Application.Common;

namespace FlowStock.Application.Users;

public record UserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Password,
    IReadOnlyList<string> Roles);

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    string? Phone);

public record AssignRolesRequest(IReadOnlyList<string> Roles);

/// <summary>Filters for GET /api/users.</summary>
public class UserQuery : PagedQuery
{
    /// <summary>Case-insensitive match against name or email.</summary>
    public string? Search { get; set; }

    public bool? IsActive { get; set; }

    public string? Role { get; set; }
}
