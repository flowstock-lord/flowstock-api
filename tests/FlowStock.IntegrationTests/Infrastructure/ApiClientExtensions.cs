using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlowStock.Application.Authentication;

namespace FlowStock.IntegrationTests.Infrastructure;

public static class ApiClientExtensions
{
    /// <summary>Logs in and returns a client whose requests carry the resulting bearer token.</summary>
    public static async Task<HttpClient> AuthenticatedClientAsync(
        this FlowStockApiFactory factory,
        string email,
        string password)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        return client;
    }
}
