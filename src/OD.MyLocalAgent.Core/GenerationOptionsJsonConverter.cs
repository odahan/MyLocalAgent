using System.Text.Json;
using System.Text.Json.Serialization;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Reads and writes the deliberately small, validated set of per-step generation overrides.
/// </summary>
internal sealed class GenerationOptionsJsonConverter :
    JsonConverter<GenerationOptions>
{
    public override bool HandleNull => true;

    public override GenerationOptions Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                "'generationOptions' must be a JSON object.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        double? temperature = null;
        double? topP = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new JsonException(
                    $"Generation option '{property.Name}' is duplicated.");
            }

            if (string.Equals(
                    property.Name,
                    "temperature",
                    StringComparison.OrdinalIgnoreCase))
            {
                temperature = ReadNumber(property);
            }
            else if (string.Equals(
                         property.Name,
                         "topP",
                         StringComparison.OrdinalIgnoreCase))
            {
                topP = ReadNumber(property);
            }
            else
            {
                throw new JsonException(
                    $"Unknown generation option '{property.Name}'. Only 'temperature' and 'topP' are supported.");
            }
        }

        var result = new GenerationOptions
        {
            Temperature = temperature,
            TopP = topP,
        };
        var validation = GenerationPolicyValidation.ValidateOverrides(result);
        if (validation is not null)
        {
            throw new JsonException(validation);
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer,
        GenerationOptions value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Temperature is { } temperature)
        {
            writer.WriteNumber("temperature", temperature);
        }

        if (value.TopP is { } topP)
        {
            writer.WriteNumber("topP", topP);
        }

        writer.WriteEndObject();
    }

    private static double ReadNumber(JsonProperty property)
    {
        if (property.Value.ValueKind != JsonValueKind.Number ||
            !property.Value.TryGetDouble(out var value))
        {
            throw new JsonException(
                $"Generation option '{property.Name}' must be a non-null JSON number.");
        }

        return value;
    }
}
