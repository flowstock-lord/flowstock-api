using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowStock.IntegrationTests.Infrastructure;

/// <summary>Mirrors the API's JSON contract: camelCase properties and enums by name.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
