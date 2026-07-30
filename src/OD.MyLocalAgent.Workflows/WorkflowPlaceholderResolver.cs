using System.Buffers;
using System.Globalization;
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
        string stepId,
        TimeProvider timeProvider)
    {
        if (arguments.ValueKind == JsonValueKind.Undefined)
        {
            return JsonSerializer.SerializeToElement(new { });
        }

        var buffer = new ArrayBufferWriter<byte>();
        var localNow = timeProvider.GetLocalNow();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteResolvedElement(
                writer,
                arguments,
                task,
                context,
                stepId,
                localNow);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    public static string ResolveText(
        string template,
        ScheduledTask task,
        WorkflowContext context,
        string stepId,
        DateTimeOffset localNow)
    {
        ArgumentNullException.ThrowIfNull(template);
        return PlaceholderPattern().Replace(
            template,
            match => ResolvePlaceholder(
                match.Groups[1].Value.Trim(),
                task,
                context,
                stepId,
                localNow));
    }

    private static void WriteResolvedElement(
        Utf8JsonWriter writer,
        JsonElement element,
        ScheduledTask task,
        WorkflowContext context,
        string stepId,
        DateTimeOffset localNow)
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
                        stepId,
                        localNow);
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
                        task,
                        context,
                        stepId,
                        localNow);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(
                    ResolveText(
                        element.GetString() ?? string.Empty,
                        task,
                        context,
                        stepId,
                        localNow));
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
        string stepId,
        DateTimeOffset localNow)
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
        var match = DateTimePlaceholderPattern().Match(placeholder);
        if (!match.Success)
        {
            value = string.Empty;
            return false;
        }

        var name = match.Groups["name"].Value;
        var format = match.Groups["format"].Success
            ? NormalizeFriendlyDateTimeFormat(match.Groups["format"].Value)
            : string.Equals(name, "Today", StringComparison.OrdinalIgnoreCase)
                ? "yyyy-MM-dd"
                : "yyyy-MM-ddTHH:mm:sszzz";
        if (string.IsNullOrWhiteSpace(format) || format.Length > 100)
        {
            throw new FormatException(
                $"Workflow date/time placeholder '{placeholder}' has an invalid format.");
        }

        value = localNow.ToString(format, CultureInfo.InvariantCulture);
        return true;
    }

    private static string NormalizeFriendlyDateTimeFormat(string format) =>
        format
            .Replace("YYYY", "yyyy", StringComparison.Ordinal)
            .Replace("YY", "yy", StringComparison.Ordinal)
            .Replace("DD", "dd", StringComparison.Ordinal)
            .Replace("hh", "HH", StringComparison.Ordinal);

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
            var current = document.RootElement;
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
        catch (JsonException)
        {
            throw new WorkflowPlaceholderException(placeholder, stepId);
        }
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

    [GeneratedRegex(
        @"\{\{\s*([^{}]+?)\s*\}\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(
        @"^(?<name>Today|Now)(?:\[(?<format>[^\[\]]*)\])?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DateTimePlaceholderPattern();
}
