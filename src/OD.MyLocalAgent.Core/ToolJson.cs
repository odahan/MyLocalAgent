using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Holds the common JSON settings used for tool inputs, outputs, and diagnostics.
/// </summary>
internal static class ToolJson
{
    public static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);
}
