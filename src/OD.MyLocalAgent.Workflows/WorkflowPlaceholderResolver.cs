using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

internal static partial class WorkflowPlaceholderResolver
{
    public static JsonElement ResolveArguments(
        JsonElement arguments,
        ScheduledTask task,
        WorkflowContext context,
        string stepId)
    {
        if (arguments.ValueKind == JsonValueKind.Undefined)
        {
            return JsonSerializer.SerializeToElement(new { });
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteResolvedElement(writer, arguments, task, context, stepId);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    public static string ResolveText(
        string template,
        ScheduledTask task,
        WorkflowContext context,
        string stepId)
    {
        ArgumentNullException.ThrowIfNull(template);
        return PlaceholderPattern().Replace(
            template,
            match => ResolvePlaceholder(
                match.Groups[1].Value.Trim(),
                task,
                context,
                stepId));
    }

    private static void WriteResolvedElement(
        Utf8JsonWriter writer,
        JsonElement element,
        ScheduledTask task,
        WorkflowContext context,
        string stepId)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteResolvedElement(
                        writer,
                        property.Value,
                        task,
                        context,
                        stepId);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteResolvedElement(writer, item, task, context, stepId);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(
                    ResolveText(
                        element.GetString() ?? string.Empty,
                        task,
                        context,
                        stepId));
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string ResolvePlaceholder(
        string placeholder,
        ScheduledTask task,
        WorkflowContext context,
        string stepId)
    {
        if (string.Equals(placeholder, "task.id", StringComparison.OrdinalIgnoreCase))
        {
            return task.Id;
        }

        if (string.Equals(placeholder, "task.name", StringComparison.OrdinalIgnoreCase))
        {
            return task.Name;
        }

        if (string.Equals(placeholder, "task.prompt", StringComparison.OrdinalIgnoreCase))
        {
            return task.Prompt;
        }

        if (string.Equals(
                placeholder,
                "previous.output",
                StringComparison.OrdinalIgnoreCase))
        {
            return context.PreviousOutput ??
                throw new WorkflowPlaceholderException(placeholder, stepId);
        }

        const string outputSuffix = ".output";
        if (placeholder.EndsWith(outputSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var referencedStepId = placeholder[..^outputSuffix.Length];
            if (!string.IsNullOrWhiteSpace(referencedStepId) &&
                context.TryGetOutput(referencedStepId, out var output))
            {
                return output!;
            }
        }

        throw new WorkflowPlaceholderException(placeholder, stepId);
    }

    [GeneratedRegex(
        @"\{\{\s*([^{}]+?)\s*\}\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
