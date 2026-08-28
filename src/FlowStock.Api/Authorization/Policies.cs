using FlowStock.Domain.Users;
using Microsoft.AspNetCore.Authorization;

namespace FlowStock.Api.Authorization;

/// <summary>
/// Role policies from docs/PLAN.md, section 25. Authorization is enforced here, on the backend.
/// </summary>
public static class Policies
{
    public const string Admin = nameof(Admin);
    public const string Warehouse = nameof(Warehouse);
    public const string Production = nameof(Production);
    public const string AnyAuthenticated = nameof(AnyAuthenticated);

    public static AuthorizationBuilder AddFlowStockPolicies(this AuthorizationBuilder builder) => builder
        .AddPolicy(Admin, policy => policy.RequireRole(RoleNames.Admin))
        .AddPolicy(Warehouse, policy => policy.RequireRole(RoleNames.Admin, RoleNames.WarehouseManager))
        .AddPolicy(Production, policy => policy.RequireRole(RoleNames.Admin, RoleNames.ProductionManager))
        .AddPolicy(AnyAuthenticated, policy => policy.RequireAuthenticatedUser());
}
