using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nook.Api.Jobs;

/// <summary>
/// How job payloads and results are written as JSON: camelCase names and enums by name, as the API writes
/// its responses, so a result reads the same in the jobs endpoints as anywhere else.
/// </summary>
public static class JobJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Reads a payload or result; JSON that holds null is an error, since a job always stores a value.</summary>
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException($"The job's JSON holds null where a {typeof(T).Name} was expected.");
}
