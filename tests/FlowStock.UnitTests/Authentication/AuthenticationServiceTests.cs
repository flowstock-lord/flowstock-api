using FlowStock.Application.Authentication;
using FlowStock.Application.Common;
using FlowStock.Domain.Users;
using FlowStock.Infrastructure.Identity;
using FlowStock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowStock.UnitTests.Authentication;

public class AuthenticationServiceTests
{
    private static readonly JwtOptions JwtOptions = new()
    {
        Issuer = "FlowStock",
        Audience = "FlowStock.Api",
        Key = "unit-test-signing-key-at-least-32-chars",
        AccessTokenMinutes = 60
    };

    private readonly PasswordHasher _hasher = new();

    private (AuthenticationService Service, FlowStockDbContext Db) CreateService(User user)
    {
        var options = new DbContextOptionsBuilder<FlowStockDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new FlowStockDbContext(options);

        var role = new Role { Id = RoleIds.Admin, Name = RoleNames.Admin };
        db.Roles.Add(role);
        user.UserRoles = [new UserRole { UserId = user.Id, RoleId = role.Id, Role = role }];
        db.Users.Add(user);
        db.SaveChanges();

        var service = new AuthenticationService(
            db,
            _hasher,
            new JwtTokenGenerator(Options.Create(JwtOptions), TimeProvider.System),
            NullLogger<AuthenticationService>.Instance);

        return (service, db);
    }

    private User CreateUser(bool isActive = true) => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@flowstock.local",
        PasswordHash = _hasher.Hash("Admin123!"),
        IsActive = isActive
    };

    [Fact]
    public async Task Login_succeeds_with_correct_credentials()
    {
        var (service, _) = CreateService(CreateUser());

        var result = await service.LoginAsync(new LoginRequest("ada@flowstock.local", "Admin123!"), default);

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.Response!.AccessToken);
        Assert.Equal([RoleNames.Admin], result.Response.Roles);
    }

    [Fact]
    public async Task Login_is_case_insensitive_about_the_email()
    {
        var (service, _) = CreateService(CreateUser());

        var result = await service.LoginAsync(new LoginRequest("  Ada@FlowStock.Local ", "Admin123!"), default);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Login_rejects_a_wrong_password()
    {
        var (service, _) = CreateService(CreateUser());

        var result = await service.LoginAsync(new LoginRequest("ada@flowstock.local", "wrong"), default);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task Login_rejects_an_unknown_email_with_the_same_code_as_a_wrong_password()
    {
        var (service, _) = CreateService(CreateUser());

        var result = await service.LoginAsync(new LoginRequest("nobody@flowstock.local", "Admin123!"), default);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task Login_rejects_a_deactivated_user()
    {
        var (service, _) = CreateService(CreateUser(isActive: false));

        var result = await service.LoginAsync(new LoginRequest("ada@flowstock.local", "Admin123!"), default);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.UserInactive, result.ErrorCode);
    }
}
