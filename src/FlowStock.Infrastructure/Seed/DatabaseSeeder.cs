using FlowStock.Application.Common;
using FlowStock.Domain.Users;
using FlowStock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowStock.Infrastructure.Seed;

/// <summary>One seeded development user. Passwords come from configuration, never from code.</summary>
public class SeedUser
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];
}

public class SeedOptions
{
    public const string SectionName = "Seed";

    public List<SeedUser> Users { get; set; } = [];
}

/// <summary>
/// Seeds development users. Only ever called from the Development environment
/// (docs/PLAN.md, section 32) — seed credentials must never reach production.
/// </summary>
public class DatabaseSeeder(
    FlowStockDbContext db,
    IPasswordHasher passwordHasher,
    ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(IReadOnlyList<SeedUser> users, CancellationToken cancellationToken = default)
    {
        var created = 0;

        foreach (var seedUser in users)
        {
            if (string.IsNullOrWhiteSpace(seedUser.Email) || string.IsNullOrWhiteSpace(seedUser.Password))
            {
                logger.LogWarning("Skipping seed user without email or password");
                continue;
            }

            var email = User.NormalizeEmail(seedUser.Email);

            if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
            {
                continue;
            }

            var roles = await db.Roles
                .Where(r => seedUser.Roles.Contains(r.Name))
                .ToListAsync(cancellationToken);

            if (roles.Count != seedUser.Roles.Count)
            {
                logger.LogWarning("Seed user {Email} references unknown roles; skipping", email);
                continue;
            }

            db.Users.Add(new User
            {
                FirstName = seedUser.FirstName,
                LastName = seedUser.LastName,
                Email = email,
                PasswordHash = passwordHasher.Hash(seedUser.Password),
                IsActive = true,
                UserRoles = roles.Select(r => new UserRole { RoleId = r.Id }).ToList()
            });

            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} development user(s)", created);
        }
    }
}
