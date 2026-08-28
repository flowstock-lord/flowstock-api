namespace FlowStock.Domain.Users;

public class Role
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<UserRole> UserRoles { get; set; } = [];
}

public class UserRole
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public int RoleId { get; set; }

    public Role Role { get; set; } = null!;
}

/// <summary>
/// The four roles from docs/PLAN.md section 25. Single source of truth for seeding,
/// authorization policies and role checks — never hardcode these strings elsewhere.
/// </summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string WarehouseManager = "WarehouseManager";
    public const string ProductionManager = "ProductionManager";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All =
        [Admin, WarehouseManager, ProductionManager, Viewer];
}

/// <summary>Stable role ids, used by the seeded rows in the migration.</summary>
public static class RoleIds
{
    public const int Admin = 1;
    public const int WarehouseManager = 2;
    public const int ProductionManager = 3;
    public const int Viewer = 4;
}
