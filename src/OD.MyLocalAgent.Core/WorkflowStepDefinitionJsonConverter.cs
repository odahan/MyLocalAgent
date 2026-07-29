using System.Text.Json;
using System.Text.Json.Serialization;

namespace OD.MyLocalAgent.Core;

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
            value.RetryBackoffSeconds is null)
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

        writer.WriteEndObject();
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
