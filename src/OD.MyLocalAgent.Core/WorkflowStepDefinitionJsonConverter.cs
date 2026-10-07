using System.Text.Json;
using System.Text.Json.Serialization;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Supports both the concise string syntax and the extended object syntax for workflow steps.
/// </summary>
internal sealed class WorkflowStepDefinitionJsonConverter : JsonConverter<WorkflowStepDefinition>
{
    public override WorkflowStepDefinition Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var alias = reader.GetString();
            if (string.IsNullOrWhiteSpace(alias))
            {
                throw new JsonException("A workflow alias cannot be empty.");
            }

            return new WorkflowStepDefinition
            {
                Step = alias,
                IsStringForm = true,
            };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A workflow step must be a string or an object.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        ValidateProperties(root);
        if (!root.TryGetProperty("step", out var stepElement) ||
            stepElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(stepElement.GetString()))
        {
            throw new JsonException("An object-form workflow step requires a non-empty 'step'.");
        }

        return new WorkflowStepDefinition
        {
            Step = stepElement.GetString()!,
            Id = ReadOptionalString(root, "id"),
            Args = root.TryGetProperty("args", out var args) ? args.Clone() : default,
            OnError = ReadOptionalPolicy(root),
            RetryCount = ReadOptionalInt32(root, "retryCount"),
            RetryBackoffSeconds = ReadOptionalInt32(root, "retryBackoffSeconds"),
            FallbackOutput = ReadOptionalValue(root, "fallbackOutput"),
            GenerationOptions = ReadGenerationOptions(root, options),
            Model = ReadOptionalString(root, "model"),
            ToolAliases = ReadToolAliases(root, options),
            KnowledgeAccess = ReadKnowledgeAccess(root, options),
            Next = ReadOptionalString(root, "next"),
            DecisionRoutes = ReadOptionalObject(root, "decisionRoutes"),
            ResponseSchema = ReadOptionalObject(root, "responseSchema"),
            ForEach = ReadOptionalString(root, "forEach"),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        WorkflowStepDefinition value,
        JsonSerializerOptions options)
    {
        if (value.IsStringForm &&
            value.Id is null &&
            value.Args.ValueKind == JsonValueKind.Undefined &&
            value.OnError is null &&
            value.RetryCount is null &&
            value.RetryBackoffSeconds is null &&
            value.FallbackOutput is null &&
            (value.GenerationOptions is null || value.GenerationOptions.IsEmpty) &&
            value.Model is null &&
            value.ToolAliases.Count == 0 &&
            value.KnowledgeAccess is null &&
            value.Next is null &&
            value.DecisionRoutes is null &&
            value.ResponseSchema is null &&
            value.ForEach is null)
        {
            writer.WriteStringValue(value.Step);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("step", value.Step);
        if (value.Id is not null)
        {
            writer.WriteString("id", value.Id);
        }

        if (value.Args.ValueKind != JsonValueKind.Undefined)
        {
            writer.WritePropertyName("args");
            value.Args.WriteTo(writer);
        }

        if (value.OnError is not null)
        {
            writer.WriteString("onError", value.OnError.Value.ToString());
        }

        if (value.RetryCount is not null)
        {
            writer.WriteNumber("retryCount", value.RetryCount.Value);
        }

        if (value.RetryBackoffSeconds is not null)
        {
            writer.WriteNumber("retryBackoffSeconds", value.RetryBackoffSeconds.Value);
        }

        if (value.FallbackOutput is { } fallbackOutput)
        {
            writer.WritePropertyName("fallbackOutput");
            fallbackOutput.WriteTo(writer);
        }

        if (value.GenerationOptions is { IsEmpty: false } generationOptions)
        {
            writer.WritePropertyName("generationOptions");
            JsonSerializer.Serialize(writer, generationOptions, options);
        }

        if (value.Model is not null)
        {
            writer.WriteString("model", value.Model);
        }

        if (value.ToolAliases.Count > 0)
        {
            writer.WritePropertyName("toolAliases");
            JsonSerializer.Serialize(writer, value.ToolAliases, options);
        }

        if (value.KnowledgeAccess is not null)
        {
            writer.WritePropertyName("knowledgeAccess");
            JsonSerializer.Serialize(writer, value.KnowledgeAccess, options);
        }

        if (value.Next is not null)
        {
            writer.WriteString("next", value.Next);
        }

        if (value.ResponseSchema is { } responseSchema)
        {
            writer.WritePropertyName("responseSchema");
            responseSchema.WriteTo(writer);
        }

        if (value.ForEach is not null)
        {
            writer.WriteString("forEach", value.ForEach);
        }

        if (value.DecisionRoutes is { } routes)
        {
            writer.WritePropertyName("decisionRoutes");
            routes.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void ValidateProperties(JsonElement root)
    {
        string[] allowed =
        [
            "step",
            "id",
            "args",
            "onError",
            "retryCount",
            "retryBackoffSeconds",
            "fallbackOutput",
            "generationOptions",
            "model",
            "toolAliases",
            "knowledgeAccess",
            "next",
            "decisionRoutes",
            "responseSchema",
            "forEach",
        ];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowed.Contains(
                    property.Name,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new JsonException(
                    $"Unknown workflow-step property '{property.Name}'.");
            }

            if (!seen.Add(property.Name))
            {
                throw new JsonException(
                    $"Workflow-step property '{property.Name}' is duplicated.");
            }
        }
    }

    private static JsonElement? ReadOptionalObject(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Workflow property '{propertyName}' must be an object.");
        }

        return element.Clone();
    }

    private static JsonElement? ReadOptionalValue(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) ? element.Clone() : null;

    private static IReadOnlyList<string> ReadToolAliases(JsonElement root, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty("toolAliases", out var element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Workflow property 'toolAliases' must be an array.");
        }

        return element.Deserialize<string[]>(options)
            ?? throw new JsonException("Workflow property 'toolAliases' cannot be null.");
    }

    private static KnowledgeAccessDefinition? ReadKnowledgeAccess(JsonElement root, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty("knowledgeAccess", out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Workflow property 'knowledgeAccess' must be an object.");
        }

        return element.Deserialize<KnowledgeAccessDefinition>(options)
            ?? throw new JsonException("Workflow property 'knowledgeAccess' cannot be null.");
    }

    private static GenerationOptions? ReadGenerationOptions(
        JsonElement root,
        JsonSerializerOptions options)
    {
        if (!root.TryGetProperty("generationOptions", out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                "Workflow property 'generationOptions' must be an object.");
        }

        return element.Deserialize<GenerationOptions>(options)
            ?? throw new JsonException(
                "Workflow property 'generationOptions' cannot be null.");
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Workflow property '{propertyName}' must be a string.");
        }

        return element.GetString();
    }

    private static int? ReadOptionalInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (!element.TryGetInt32(out var value))
        {
            throw new JsonException($"Workflow property '{propertyName}' must be an integer.");
        }

        return value;
    }

    private static WorkflowErrorPolicy? ReadOptionalPolicy(JsonElement root)
    {
        var value = ReadOptionalString(root, "onError");
        if (value is null)
        {
            return null;
        }

        if (!Enum.TryParse<WorkflowErrorPolicy>(value, ignoreCase: true, out var policy))
        {
            throw new JsonException(
                "Workflow property 'onError' must be StopOnError, ContinueOnError, or Retry.");
        }

        return policy;
    }
}
