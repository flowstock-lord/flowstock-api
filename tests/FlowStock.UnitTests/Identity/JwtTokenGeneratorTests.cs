using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FlowStock.Domain.Users;
using FlowStock.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace FlowStock.UnitTests.Identity;

public class JwtTokenGeneratorTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "FlowStock",
        Audience = "FlowStock.Api",
        Key = "unit-test-signing-key-at-least-32-chars",
        AccessTokenMinutes = 30
    };

    private static readonly DateTimeOffset Now = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    private readonly JwtTokenGenerator _generator = new(
        Microsoft.Extensions.Options.Options.Create(Options),
        new FakeTimeProvider(Now));

    private static User CreateUser() => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@flowstock.local"
    };

    [Fact]
    public void Generate_emits_identity_claims()
    {
        var token = _generator.Generate(CreateUser(), [RoleNames.Admin]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);

        Assert.Equal("11111111-1111-1111-1111-111111111111", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("ada@flowstock.local", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("Ada Lovelace", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Name).Value);
        Assert.Equal("FlowStock", jwt.Issuer);
        Assert.Contains("FlowStock.Api", jwt.Audiences);
    }

    [Fact]
    public void Generate_emits_one_claim_per_role()
    {
        var token = _generator.Generate(CreateUser(), [RoleNames.Admin, RoleNames.Viewer]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);

        var roles = jwt.Claims
            .Where(c => c.Type is ClaimTypes.Role or "role")
            .Select(c => c.Value)
            .ToArray();

        Assert.Equal([RoleNames.Admin, RoleNames.Viewer], roles);
    }

    [Fact]
    public void Generate_expires_after_the_configured_lifetime()
    {
        var token = _generator.Generate(CreateUser(), [RoleNames.Viewer]);

        Assert.Equal(Now.UtcDateTime.AddMinutes(30), token.ExpiresAtUtc);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
