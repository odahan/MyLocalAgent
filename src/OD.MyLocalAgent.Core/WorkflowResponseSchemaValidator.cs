using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>Validates the portable Stage 29 JSON response-schema subset and its instances.</summary>
public static class WorkflowResponseSchemaValidator
{
    private const int MaximumSchemaBytes = 65_536;
    private const int MaximumDepth = 16;
    private static readonly HashSet<string> AllowedKeywords = new(StringComparer.Ordinal)
    {
        "type", "properties", "required", "additionalProperties", "enum", "const",
        "minimum", "maximum", "minLength", "maxLength", "minItems", "maxItems", "items", "description",
    };

    /// <summary>Validates a persisted response schema.</summary>
    public static void ValidateSchema(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new WorkflowValidationException("Workflow responseSchema must be a JSON object.");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(schema.GetRawText()) > MaximumSchemaBytes)
        {
            throw new WorkflowValidationException($"Workflow responseSchema exceeds {MaximumSchemaBytes} UTF-8 bytes.");
        }

        ValidateSchemaNode(schema, "$", 0, requireObject: true);
    }

    /// <summary>Validates one model response and returns its normalized JSON text.</summary>
    public static string ValidateResponse(string response, JsonElement schema)
    {
        ArgumentNullException.ThrowIfNull(response);
        try
        {
            using var document = JsonDocument.Parse(response);
            ValidateInstance(document.RootElement, schema, "$", 0);
            return document.RootElement.GetRawText();
        }
        catch (JsonException exception)
        {
            var failure = new WorkflowStructuredResponseException(
                $"The AI response is not valid JSON. {exception.Message}", exception);
            failure.CaptureResponse(response, string.IsNullOrWhiteSpace(response) ? "EmptyResponse" : "InvalidJson");
            throw failure;
        }
        catch (WorkflowStructuredResponseException exception)
        {
            exception.CaptureResponse(response, "SchemaMismatch");
            throw;
        }
    }

    private static void ValidateSchemaNode(JsonElement schema, string path, int depth, bool requireObject = false)
    {
        if (depth > MaximumDepth)
        {
            throw new WorkflowValidationException($"Workflow responseSchema exceeds the maximum nesting depth at '{path}'.");
        }

        foreach (var property in schema.EnumerateObject())
        {
            if (!AllowedKeywords.Contains(property.Name))
            {
                throw new WorkflowValidationException($"Workflow responseSchema keyword '{property.Name}' at '{path}' is unsupported.");
            }
        }

        var type = ReadSchemaType(schema, path);
        if (requireObject && !string.Equals(type, "object", StringComparison.Ordinal))
        {
            throw new WorkflowValidationException("Workflow responseSchema root type must be 'object'.");
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            if (properties.ValueKind != JsonValueKind.Object)
            {
                throw new WorkflowValidationException($"Workflow responseSchema properties at '{path}' must be an object.");
            }

            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new WorkflowValidationException($"Workflow responseSchema property '{property.Name}' must contain a schema object.");
                }

                ValidateSchemaNode(property.Value, $"{path}.{property.Name}", depth + 1);
            }
        }

        if (schema.TryGetProperty("required", out var required))
        {
            if (required.ValueKind != JsonValueKind.Array || required.EnumerateArray().Any(static item => item.ValueKind != JsonValueKind.String))
            {
                throw new WorkflowValidationException($"Workflow responseSchema required at '{path}' must be an array of strings.");
            }

            var names = required.EnumerateArray().Select(static item => item.GetString()!).ToArray();
            if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            {
                throw new WorkflowValidationException($"Workflow responseSchema required at '{path}' must contain unique non-empty names.");
            }
        }

        ValidateOptionalKind(schema, "additionalProperties", JsonValueKind.True, JsonValueKind.False, path);
        ValidateOptionalKind(schema, "minimum", JsonValueKind.Number, path);
        ValidateOptionalKind(schema, "maximum", JsonValueKind.Number, path);
        ValidateOptionalNonNegativeInteger(schema, "minLength", path);
        ValidateOptionalNonNegativeInteger(schema, "maxLength", path);
        ValidateOptionalNonNegativeInteger(schema, "minItems", path);
        ValidateOptionalNonNegativeInteger(schema, "maxItems", path);
        if (schema.TryGetProperty("items", out var items))
        {
            if (items.ValueKind != JsonValueKind.Object)
            {
                throw new WorkflowValidationException($"Workflow responseSchema items at '{path}' must be an object.");
            }

            ValidateSchemaNode(items, $"{path}[]", depth + 1);
        }

        if (schema.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind != JsonValueKind.Array)
        {
            throw new WorkflowValidationException($"Workflow responseSchema enum at '{path}' must be an array.");
        }
    }

    private static string ReadSchemaType(JsonElement schema, string path)
    {
        if (!schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            throw new WorkflowValidationException($"Workflow responseSchema at '{path}' requires a string type.");
        }

        var value = type.GetString()!;
        string[] allowed = ["object", "array", "string", "number", "integer", "boolean", "null"];
        if (!allowed.Contains(value, StringComparer.Ordinal))
        {
            throw new WorkflowValidationException($"Workflow responseSchema type '{value}' at '{path}' is unsupported.");
        }

        return value;
    }

    private static void ValidateInstance(JsonElement value, JsonElement schema, string path, int depth)
    {
        if (depth > MaximumDepth)
        {
            throw new WorkflowStructuredResponseException($"The AI response exceeds the schema depth at '{path}'.");
        }

        var type = schema.GetProperty("type").GetString()!;
        var typeMatches = type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => false,
        };
        if (!typeMatches)
        {
            throw new WorkflowStructuredResponseException($"The AI response value at '{path}' must be {type}.");
        }

        if (schema.TryGetProperty("enum", out var enumValues) && !enumValues.EnumerateArray().Any(candidate => JsonElement.DeepEquals(candidate, value)))
        {
            throw new WorkflowStructuredResponseException($"The AI response value at '{path}' is outside the allowed enum.");
        }

        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(constant, value))
        {
            throw new WorkflowStructuredResponseException($"The AI response value at '{path}' does not match const.");
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            ValidateObject(value, schema, path, depth);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var itemsCount = value.GetArrayLength();
            ValidateRange(itemsCount, schema, "minItems", "maxItems", path);
            if (schema.TryGetProperty("items", out var items))
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    ValidateInstance(item, items, $"{path}[{index++}]", depth + 1);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            var length = value.GetString()!.Length;
            ValidateRange(length, schema, "minLength", "maxLength", path);
        }
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            ValidateRange(number, schema, "minimum", "maximum", path);
        }
    }

    private static void ValidateObject(JsonElement value, JsonElement schema, string path, int depth)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var item in required.EnumerateArray())
            {
                var name = item.GetString()!;
                if (!value.TryGetProperty(name, out _))
                {
                    throw new WorkflowStructuredResponseException($"The AI response is missing required property '{path}.{name}'.");
                }
            }
        }

        var hasProperties = schema.TryGetProperty("properties", out var properties);
        var allowAdditional = !schema.TryGetProperty("additionalProperties", out var additional) || additional.GetBoolean();
        foreach (var property in value.EnumerateObject())
        {
            if (hasProperties && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                ValidateInstance(property.Value, propertySchema, $"{path}.{property.Name}", depth + 1);
            }
            else if (!allowAdditional)
            {
                throw new WorkflowStructuredResponseException($"The AI response property '{path}.{property.Name}' is not allowed.");
            }
        }
    }

    private static void ValidateRange<T>(T value, JsonElement schema, string minimumName, string maximumName, string path)
        where T : struct, IComparable<T>
    {
        if (schema.TryGetProperty(minimumName, out var minimum) && minimum.TryGetDecimal(out var minimumDecimal) && Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) < minimumDecimal ||
            schema.TryGetProperty(maximumName, out var maximum) && maximum.TryGetDecimal(out var maximumDecimal) && Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) > maximumDecimal)
        {
            throw new WorkflowStructuredResponseException($"The AI response value at '{path}' is outside the allowed range.");
        }
    }

    private static void ValidateOptionalKind(JsonElement schema, string name, JsonValueKind kind, string path) =>
        ValidateOptionalKind(schema, name, [kind], path);

    private static void ValidateOptionalKind(JsonElement schema, string name, JsonValueKind first, JsonValueKind second, string path) =>
        ValidateOptionalKind(schema, name, [first, second], path);

    private static void ValidateOptionalKind(JsonElement schema, string name, IReadOnlyList<JsonValueKind> kinds, string path)
    {
        if (schema.TryGetProperty(name, out var value) && !kinds.Contains(value.ValueKind))
        {
            throw new WorkflowValidationException($"Workflow responseSchema keyword '{name}' at '{path}' has an invalid type.");
        }
    }

    private static void ValidateOptionalNonNegativeInteger(JsonElement schema, string name, string path)
    {
        if (schema.TryGetProperty(name, out var value) && (!value.TryGetInt32(out var number) || number < 0))
        {
            throw new WorkflowValidationException($"Workflow responseSchema keyword '{name}' at '{path}' must be a non-negative integer.");
        }
    }
}

/// <summary>Represents an AI response that does not satisfy its declared workflow schema.</summary>
public sealed class WorkflowStructuredResponseException : Exception
{
    /// <summary>Gets bounded, single-line JSON diagnostics for a rejected response, when available.</summary>
    public string? ResponseDiagnostics { get; private set; }

    /// <summary>Initializes the exception.</summary>
    public WorkflowStructuredResponseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    internal void CaptureResponse(string response, string failureKind)
    {
        const int maximumHeadCharacters = 1536;
        const int maximumTailCharacters = 512;
        var headLength = Math.Min(response.Length, maximumHeadCharacters);
        var tailLength = Math.Min(response.Length - headLength, maximumTailCharacters);
        ResponseDiagnostics = JsonSerializer.Serialize(new
        {
            failureKind,
            responseCharacters = response.Length,
            responseSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response))),
            excerptTruncated = response.Length > headLength + tailLength,
            responseHead = response[..headLength],
            responseTail = tailLength == 0 ? string.Empty : response[^tailLength..],
        });
    }
}
