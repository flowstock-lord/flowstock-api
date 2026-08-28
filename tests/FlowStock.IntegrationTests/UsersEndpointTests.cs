using System.Net;
using System.Net.Http.Json;
using FlowStock.Application.Common;
using FlowStock.Application.Users;
using FlowStock.Domain.Users;
using FlowStock.IntegrationTests.Infrastructure;

namespace FlowStock.IntegrationTests;

[Collection(ApiCollection.Name)]
public class UsersEndpointTests(FlowStockApiFactory factory)
{
    private Task<HttpClient> AdminClient() =>
        factory.AuthenticatedClientAsync(FlowStockApiFactory.AdminEmail, FlowStockApiFactory.AdminPassword);

    [Fact]
    public async Task Admin_can_list_users()
    {
        var client = await AdminClient();

        var page = await client.GetFromJsonAsync<PagedResult<UserResponse>>("/api/users?page=1&pageSize=10");

        Assert.NotNull(page);
        Assert.True(page!.TotalCount >= 4, "the four seeded users should be present");
    }

    [Fact]
    public async Task Viewer_is_forbidden_from_the_users_api()
    {
        var client = await factory.AuthenticatedClientAsync(
            FlowStockApiFactory.ViewerEmail, FlowStockApiFactory.ViewerPassword);

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_to_the_users_api_returns_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_a_user_who_can_then_log_in()
    {
        var client = await AdminClient();
        var email = $"picker-{Guid.NewGuid():N}@flowstock.local";

        var response = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "Order", "Picker", email, "+7 700 000 00 00", "Picker123!", [RoleNames.WarehouseManager]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(email, created!.Email);
        Assert.Equal([RoleNames.WarehouseManager], created.Roles);
        Assert.True(created.IsActive);

        var newUserClient = await factory.AuthenticatedClientAsync(email, "Picker123!");
        var me = await newUserClient.GetFromJsonAsync<FlowStock.Application.Authentication.CurrentUserResponse>("/api/auth/me");
        Assert.Equal(email, me!.Email);
    }

    [Fact]
    public async Task Creating_a_user_with_a_duplicate_email_returns_the_domain_error()
    {
        var client = await AdminClient();

        var response = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "Another", "Admin", FlowStockApiFactory.AdminEmail, null, "Another123!", [RoleNames.Admin]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("EMAIL_ALREADY_EXISTS", error!.Code);
    }

    [Fact]
    public async Task Creating_a_user_with_an_unknown_role_returns_the_domain_error()
    {
        var client = await AdminClient();

        var response = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "Ghost", "Role", $"ghost-{Guid.NewGuid():N}@flowstock.local", null, "Ghost123!", ["Wizard"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("ROLE_NOT_FOUND", error!.Code);
    }

    [Fact]
    public async Task A_deactivated_user_can_no_longer_log_in()
    {
        var client = await AdminClient();
        var email = $"temp-{Guid.NewGuid():N}@flowstock.local";

        var created = await (await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
                "Temp", "Worker", email, null, "TempPass123!", [RoleNames.Viewer])))
            .Content.ReadFromJsonAsync<UserResponse>();

        var deactivate = await client.PostAsync($"/api/users/{created!.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new FlowStock.Application.Authentication.LoginRequest(email, "TempPass123!"));

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var error = await login.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.UserInactive, error!.Code);
    }
}
