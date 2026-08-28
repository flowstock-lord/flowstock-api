using FlowStock.Domain.Common;

namespace FlowStock.Domain.Users;

public class UserNotFoundException(Guid userId)
    : DomainException("USER_NOT_FOUND", $"User '{userId}' was not found.",
        new Dictionary<string, object?> { ["userId"] = userId });

public class EmailAlreadyExistsException(string email)
    : DomainException("EMAIL_ALREADY_EXISTS", $"A user with email '{email}' already exists.",
        new Dictionary<string, object?> { ["email"] = email });

public class RoleNotFoundException(IEnumerable<string> roleNames)
    : DomainException("ROLE_NOT_FOUND", "One or more roles do not exist.",
        new Dictionary<string, object?> { ["roles"] = roleNames.ToArray() });
