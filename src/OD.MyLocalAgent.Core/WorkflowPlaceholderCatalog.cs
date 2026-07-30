using System.Globalization;
using System.Text.RegularExpressions;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides the shared workflow-placeholder syntax used by validation, execution, and authoring.
/// </summary>
public static partial class WorkflowPlaceholderCatalog
{
    /// <summary>
    /// Gets the version of the placeholder contract supplied to workflow assistants.
    /// </summary>
    public const string Version = "1";

    /// <summary>
    /// Gets deterministic placeholder documentation for model context and user assistance.
    /// </summary>
    public static IReadOnlyList<string> Definitions { get; } =
    [
        "{{task.id}} - current task id",
        "{{task.name}} - current task name",
        "{{task.prompt}} - current task prompt",
        "{{previous.output}} - previous successful step output",
        "{{stepId.output}} - a prior named step output",
        "{{stepId.output.property}} and {{stepId.output.items[0].name}} - structured JSON selection",
        "{{workflow.context}} - ordered successful outputs before the current step",
        "{{Today}} or {{Today[custom .NET format]}} - local date",
        "{{Now}} or {{Now[custom .NET format]}} - local date and time",
    ];

    /// <summary>
    /// Replaces every placeholder in a string through the supplied resolver.
    /// </summary>
    /// <param name="input">The text containing placeholders.</param>
    /// <param name="resolver">The function that resolves the trimmed placeholder body.</param>
    /// <returns>The replaced text.</returns>
    public static string Replace(string input, Func<string, string> resolver)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(resolver);
        return PlaceholderPattern().Replace(
            input,
            match => resolver(match.Groups[1].Value.Trim()));
    }

    /// <summary>
    /// Extracts trimmed placeholder bodies in declaration order.
    /// </summary>
    /// <param name="input">The text to inspect.</param>
    /// <returns>The placeholder bodies.</returns>
    public static IReadOnlyList<string> Extract(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return PlaceholderPattern()
            .Matches(input)
            .Select(static match => match.Groups[1].Value.Trim())
            .ToArray();
    }

    /// <summary>
    /// Normalizes the documented filename-friendly date/time format aliases.
    /// </summary>
    /// <param name="format">The user-entered .NET custom format.</param>
    /// <returns>The normalized .NET custom format.</returns>
    public static string NormalizeDateTimeFormat(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format
            .Replace("YYYY", "yyyy", StringComparison.Ordinal)
            .Replace("YY", "yy", StringComparison.Ordinal)
            .Replace("DD", "dd", StringComparison.Ordinal)
            .Replace("hh", "HH", StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether a placeholder body is statically valid at a workflow position.
    /// </summary>
    /// <param name="placeholder">The trimmed placeholder body.</param>
    /// <param name="availableStepIds">The output identifiers declared before the current step.</param>
    /// <param name="error">The deterministic validation error when invalid.</param>
    /// <returns><see langword="true"/> when the placeholder is valid.</returns>
    public static bool IsValid(
        string placeholder,
        IReadOnlySet<string> availableStepIds,
        out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placeholder);
        ArgumentNullException.ThrowIfNull(availableStepIds);

        if (placeholder.Equals("task.id", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Equals("task.name", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Equals("task.prompt", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Equals("workflow.context", StringComparison.OrdinalIgnoreCase))
        {
            error = null;
            return true;
        }

        if (DateTimePlaceholderPattern().IsMatch(placeholder))
        {
            var open = placeholder.IndexOf('[');
            if (open >= 0)
            {
                var format = placeholder[(open + 1)..^1];
                if (string.IsNullOrWhiteSpace(format) || format.Length > 100)
                {
                    error = $"Placeholder '{{{{{placeholder}}}}}' has an invalid date/time format.";
                    return false;
                }

                try
                {
                    _ = DateTimeOffset.UnixEpoch.ToString(
                        NormalizeDateTimeFormat(format),
                        CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    error = $"Placeholder '{{{{{placeholder}}}}}' has an invalid date/time format.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        if (TryValidateOutputReference(
                placeholder,
                "previous",
                allowWithoutDeclaration: true,
                out error))
        {
            return true;
        }

        var outputMarker = placeholder.IndexOf(
            ".output",
            StringComparison.OrdinalIgnoreCase);
        if (outputMarker <= 0)
        {
            error = $"Placeholder '{{{{{placeholder}}}}}' is unknown.";
            return false;
        }

        var stepId = placeholder[..outputMarker];
        if (!availableStepIds.Contains(stepId))
        {
            error = $"Placeholder '{{{{{placeholder}}}}}' references output '{stepId}' before it is available.";
            return false;
        }

        return TryValidateOutputReference(
            placeholder,
            stepId,
            allowWithoutDeclaration: false,
            out error);
    }

    private static bool TryValidateOutputReference(
        string placeholder,
        string prefix,
        bool allowWithoutDeclaration,
        out string? error)
    {
        var expected = prefix + ".output";
        if (!placeholder.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
        {
            error = allowWithoutDeclaration
                ? null
                : $"Placeholder '{{{{{placeholder}}}}}' is unknown.";
            return false;
        }

        var suffix = placeholder[expected.Length..];
        if (suffix.Length == 0 || StructuredSelectorPattern().IsMatch(suffix))
        {
            error = null;
            return true;
        }

        error = $"Placeholder '{{{{{placeholder}}}}}' has an invalid structured selector.";
        return false;
    }

    [GeneratedRegex(
        @"\{\{\s*([^{}]+?)\s*\}\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(
        @"^(Today|Now)(?:\[[^\[\]]+\])?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DateTimePlaceholderPattern();

    [GeneratedRegex(
        @"^(?:(?:\.[A-Za-z_][A-Za-z0-9_-]*)|(?:\[[0-9]+\]))+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex StructuredSelectorPattern();
}
