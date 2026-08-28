using FlowStock.Domain.Common;

namespace FlowStock.Domain.Users;

public class User : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>Stored normalized (trimmed, lower-case) and unique across users.</summary>
    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
