using System.Net;
using System.Net.Http.Json;
using FlowStock.Application.Authentication;
using FlowStock.Application.Common;
using FlowStock.Domain.Users;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthenticationTests(FlowStockApiFactory factory)
{
    [Fact]
    public async Task Login_returns_a_token_for_a_seeded_user()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(FlowStockApiFactory.AdminEmail, FlowStockApiFactory.AdminPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        Assert.NotEmpty(login!.AccessToken);
        Assert.Contains(RoleNames.Admin, login.Roles);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_returns_401_and_the_error_envelope()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(FlowStockApiFactory.AdminEmail, "definitely-wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.InvalidCredentials, error!.Code);
    }

    [Fact]
    public async Task Login_with_an_invalid_email_fails_validation()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("not-an-email", "whatever"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.ValidationFailed, error!.Code);
    }

    [Fact]
    public async Task Me_returns_the_authenticated_user()
    {
        var client = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.AdminEmail, FlowStockApiFactory.AdminPassword);

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");

        Assert.Equal(FlowStockApiFactory.AdminEmail, me!.Email);
        Assert.Contains(RoleNames.Admin, me.Roles);
    }

    [Fact]
    public async Task Me_without_a_token_returns_401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
