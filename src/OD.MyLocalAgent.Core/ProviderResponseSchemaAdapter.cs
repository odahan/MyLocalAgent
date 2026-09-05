using System.Buffers;
using System.Text.Json;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Projects application response schemas onto provider-compatible generation schemas.</summary>
internal static class ProviderResponseSchemaAdapter
{
    /// <summary>Returns the schema shape supported by the selected provider.</summary>
    public static JsonElement Adapt(string providerName, JsonElement schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        if (!string.Equals(providerName, AIOptions.OllamaProvider, StringComparison.OrdinalIgnoreCase))
        {
            return schema.Clone();
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteOllamaCompatible(writer, schema);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteOllamaCompatible(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name is "minLength" or "maxLength" or "minItems" or "maxItems")
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    WriteOllamaCompatible(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteOllamaCompatible(writer, item);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
