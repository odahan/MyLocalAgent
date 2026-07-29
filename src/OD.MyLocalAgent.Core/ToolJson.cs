using System.Text.Json;

namespace OD.MyLocalAgent.Core;

internal static class ToolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);
}
