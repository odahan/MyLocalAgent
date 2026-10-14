using System.Buffers;
using System.Text;
using System.Text.Json;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Resolves workflow placeholders while preserving the shape and JSON types of step arguments.
/// </summary>
internal static class WorkflowPlaceholderResolver
{
    public static JsonElement ResolveArguments(
        JsonElement arguments,
        IReadOnlyList<ToolArgumentDefinition> argumentDefinitions,
        ScheduledTask task,
        WorkflowContext context,
        string stepId,
        TimeProvider timeProvider,
        ModelPromptPlaceholderContext modelContext,
        WorkflowForEachContext? forEachContext = null)
    {
        if (arguments.ValueKind == JsonValueKind.Undefined)
        {
            return JsonSerializer.SerializeToElement(new { });
        }

        var buffer = new ArrayBufferWriter<byte>();
        var localNow = timeProvider.GetLocalNow();
        // Rewriting the JSON tree prevents substitutions from coercing values such as numbers or arrays to text.
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteResolvedElement(
                writer,
                arguments,
                argumentDefinitions,
                task,
                context,
                stepId,
                localNow,
                modelContext,
                forEachContext);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    public static string ResolveText(
        string template,
        ScheduledTask task,
        WorkflowContext context,
        string stepId,
        DateTimeOffset localNow,
        ModelPromptPlaceholderContext modelContext,
        WorkflowForEachContext? forEachContext = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        return WorkflowPlaceholderCatalog.Replace(
            template,
            placeholder => ResolvePlaceholder(
                placeholder,
                task,
                context,
                stepId,
                localNow,
                modelContext,
                forEachContext));
    }

    private static void WriteResolvedElement(
        Utf8JsonWriter writer,
        JsonElement element,
        IReadOnlyList<ToolArgumentDefinition> argumentDefinitions,
        ScheduledTask task,
        WorkflowContext context,
        string stepId,
        DateTimeOffset localNow,
        ModelPromptPlaceholderContext modelContext,
        WorkflowForEachContext? forEachContext,
        bool allowTypedPlaceholderValues = false,
        bool preserveNestedPlaceholderStrings = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    var argumentDefinition = argumentDefinitions.FirstOrDefault(
                        argument => string.Equals(
                            argument.Name,
                            property.Name,
                            StringComparison.OrdinalIgnoreCase));
                    WriteResolvedElement(
                        writer,
                        property.Value,
                        argumentDefinitions,
                        task,
                        context,
                        stepId,
                        localNow,
                        modelContext,
                        forEachContext,
                        !preserveNestedPlaceholderStrings &&
                        (allowTypedPlaceholderValues ||
                         SupportsTypedPlaceholderValues(argumentDefinition?.Kind)),
                        preserveNestedPlaceholderStrings ||
                        argumentDefinition?.PreserveNestedPlaceholderStrings == true);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteResolvedElement(
                        writer,
                        item,
                        argumentDefinitions,
                        task,
                        context,
                        stepId,
                        localNow,
                        modelContext,
                        forEachContext,
                        allowTypedPlaceholderValues,
                        preserveNestedPlaceholderStrings);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                var template = element.GetString() ?? string.Empty;
                if (allowTypedPlaceholderValues &&
                    TryWriteResolvedJsonPlaceholder(
                        writer,
                        template,
                        task,
                        context,
                        stepId,
                        localNow,
                        modelContext,
                        forEachContext))
                {
                    break;
                }

                writer.WriteStringValue(
                    ResolveText(
                        template,
                        task,
                        context,
                        stepId,
                        localNow,
                        modelContext,
                        forEachContext));
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool SupportsTypedPlaceholderValues(WorkflowArgumentKind? kind) =>
        kind is WorkflowArgumentKind.Integer or
            WorkflowArgumentKind.Number or
            WorkflowArgumentKind.Boolean or
            WorkflowArgumentKind.BooleanOrString or
            WorkflowArgumentKind.Object;

    private static bool TryWriteResolvedJsonPlaceholder(
        Utf8JsonWriter writer,
        string template,
        ScheduledTask task,
        WorkflowContext context,
        string stepId,
        DateTimeOffset localNow,
        ModelPromptPlaceholderContext modelContext,
        WorkflowForEachContext? forEachContext)
    {
        var trimmedTemplate = template.Trim();
        var placeholders = WorkflowPlaceholderCatalog.Extract(trimmedTemplate);
        if (placeholders.Count != 1 ||
            !trimmedTemplate.StartsWith("{{", StringComparison.Ordinal) ||
            !trimmedTemplate.EndsWith("}}", StringComparison.Ordinal))
        {
            return false;
        }

        var resolved = ResolvePlaceholder(
            placeholders[0],
            task,
            context,
            stepId,
            localNow,
            modelContext,
            forEachContext);
        try
        {
            using var document = JsonDocument.Parse(resolved);
            document.RootElement.WriteTo(writer);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ResolvePlaceholder(
        string placeholder,
        ScheduledTask task,
        WorkflowContext context,
        string stepId,
        DateTimeOffset localNow,
        ModelPromptPlaceholderContext modelContext,
        WorkflowForEachContext? forEachContext)
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

        if (ModelPromptPlaceholderResolver.TryResolve(
                placeholder,
                modelContext,
                out var modelValue))
        {
            return modelValue;
        }

        if (TryResolveDateTimePlaceholder(
                placeholder,
                localNow,
                out var dateTimeValue))
        {
            return dateTimeValue;
        }

        if (string.Equals(
                placeholder,
                "workflow.context",
                StringComparison.OrdinalIgnoreCase))
        {
            return context.ToJson();
        }

        if (string.Equals(placeholder, "workflow.runId", StringComparison.OrdinalIgnoreCase))
        {
            return context.RunId;
        }

        const string forEachItemPrefix = "foreach.item";
        if (placeholder.StartsWith(forEachItemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var iteration = forEachContext ??
                throw new WorkflowPlaceholderException(placeholder, stepId);
            return ResolveJsonElementPath(
                iteration.Item,
                placeholder[forEachItemPrefix.Length..],
                placeholder,
                stepId);
        }

        if (string.Equals(placeholder, "foreach.position", StringComparison.OrdinalIgnoreCase))
        {
            var iteration = forEachContext ??
                throw new WorkflowPlaceholderException(placeholder, stepId);
            return iteration.Position.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        const string previousPrefix = "previous.output";
        if (placeholder.StartsWith(previousPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var output = context.PreviousOutput ??
                throw new WorkflowPlaceholderException(placeholder, stepId);
            return ResolveOutputPath(
                output,
                placeholder[previousPrefix.Length..],
                placeholder,
                stepId);
        }

        const string outputMarker = ".output";
        var markerIndex = placeholder.IndexOf(
            outputMarker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex > 0)
        {
            var referencedStepId = placeholder[..markerIndex];
            if (context.TryGetOutput(referencedStepId, out var output))
            {
                return ResolveOutputPath(
                    output!,
                    placeholder[(markerIndex + outputMarker.Length)..],
                    placeholder,
                    stepId);
            }
        }

        throw new WorkflowPlaceholderException(placeholder, stepId);
    }

    private static bool TryResolveDateTimePlaceholder(
        string placeholder,
        DateTimeOffset localNow,
        out string value)
    {
        var result = DateTimePlaceholderFormatter.Resolve(
            placeholder,
            localNow);
        if (result.Status == DateTimePlaceholderStatus.NotRecognized)
        {
            value = string.Empty;
            return false;
        }

        if (result.Status == DateTimePlaceholderStatus.InvalidFormat)
        {
            throw new FormatException(
                "Workflow date/time placeholder has an invalid format.");
        }

        value = result.Value!;
        return true;
    }

    private static string ResolveOutputPath(
        string output,
        string path,
        string placeholder,
        string stepId)
    {
        if (path.Length == 0)
        {
            return output;
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            return ResolveJsonElementPath(
                document.RootElement,
                path,
                placeholder,
                stepId);
        }
        catch (JsonException)
        {
            throw new WorkflowPlaceholderException(placeholder, stepId);
        }
    }

    private static string ResolveJsonElementPath(
        JsonElement element,
        string path,
        string placeholder,
        string stepId)
    {
        var current = element;
        var position = 0;
        while (position < path.Length)
        {
            if (path[position] == '.')
            {
                position++;
                var propertyStart = position;
                while (position < path.Length &&
                       path[position] is not '.' and not '[')
                {
                    position++;
                }

                if (position == propertyStart ||
                    current.ValueKind != JsonValueKind.Object ||
                    !TryGetPropertyIgnoreCase(
                        current,
                        path[propertyStart..position],
                        out current))
                {
                    throw new WorkflowPlaceholderException(placeholder, stepId);
                }

                continue;
            }

            if (path[position] == '[')
            {
                var closingBracket = path.IndexOf(']', position + 1);
                if (closingBracket < 0 ||
                    !int.TryParse(
                        path[(position + 1)..closingBracket],
                        out var index) ||
                    index < 0 ||
                    current.ValueKind != JsonValueKind.Array ||
                    index >= current.GetArrayLength())
                {
                    throw new WorkflowPlaceholderException(placeholder, stepId);
                }

                current = current[index];
                position = closingBracket + 1;
                continue;
            }

            throw new WorkflowPlaceholderException(placeholder, stepId);
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString() ?? string.Empty
            : current.GetRawText();
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
