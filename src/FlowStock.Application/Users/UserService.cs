using FlowStock.Application.Common;
using FlowStock.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Application.Users;

public interface IUserService
{
    Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken cancellationToken);

    Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken);

    Task<UserResponse> AssignRolesAsync(Guid id, AssignRolesRequest request, CancellationToken cancellationToken);

    Task<UserResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
}

public class UserService(
    IFlowStockDbContext db,
    IPasswordHasher passwordHasher,
    ILogger<UserService> logger) : IUserService
{
    public async Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken cancellationToken)
    {
        var users = db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            users = users.Where(u =>
                u.FirstName.ToLower().Contains(search) ||
                u.LastName.ToLower().Contains(search) ||
                u.Email.Contains(search));
        }

        if (query.IsActive is not null)
        {
            users = users.Where(u => u.IsActive == query.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim();
            users = users.Where(u => u.UserRoles.Any(ur => ur.Role.Name == role));
        }

        var totalCount = await users.CountAsync(cancellationToken);

        var items = await users
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<UserResponse>(
            items.Select(ToResponse).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken)
        => ToResponse(await FindAsync(id, cancellationToken));

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw new EmailAlreadyExistsException(email);
        }

        var roles = await ResolveRolesAsync(request.Roles, cancellationToken);

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = true,
            UserRoles = roles.Select(r => new UserRole { RoleId = r.Id, Role = r }).ToList()
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} created with roles {Roles}",
            user.Id, roles.Select(r => r.Name).ToArray());

        return ToResponse(user);
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(id, cancellationToken);

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(user);
    }

    public async Task<UserResponse> AssignRolesAsync(Guid id, AssignRolesRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(id, cancellationToken);
        var roles = await ResolveRolesAsync(request.Roles, cancellationToken);

        user.UserRoles.Clear();
        foreach (var role in roles)
        {
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, Role = role });
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Roles of user {UserId} set to {Roles}",
            user.Id, roles.Select(r => r.Name).ToArray());

        return ToResponse(user);
    }

    public async Task<UserResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var user = await FindAsync(id, cancellationToken);

        user.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} active flag set to {IsActive}", user.Id, isActive);

        return ToResponse(user);
    }

    private async Task<User> FindAsync(Guid id, CancellationToken cancellationToken)
        => await db.Users
               .Include(u => u.UserRoles)
               .ThenInclude(ur => ur.Role)
               .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
           ?? throw new UserNotFoundException(id);

    private async Task<List<Role>> ResolveRolesAsync(
        IReadOnlyList<string> roleNames,
        CancellationToken cancellationToken)
    {
        var requested = roleNames
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var roles = await db.Roles
            .Where(r => requested.Contains(r.Name))
            .ToListAsync(cancellationToken);

        var missing = requested
            .Except(roles.Select(r => r.Name), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new RoleNotFoundException(missing);
        }

        return roles;
    }

    private static UserResponse ToResponse(User user) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        user.Email,
        user.Phone,
        user.IsActive,
        user.UserRoles.Select(ur => ur.Role.Name).OrderBy(name => name).ToArray(),
        user.CreatedAt,
        user.UpdatedAt);
}
