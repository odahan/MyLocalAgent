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
    public const string Version = "2";

    /// <summary>
    /// Gets the structured placeholder templates shared by validation, execution, and authoring.
    /// </summary>
    public static IReadOnlyList<WorkflowPlaceholderDefinition> Entries { get; } =
    [
        new("{{task.id}}", "current task id"),
        new("{{task.name}}", "current task name"),
        new("{{task.prompt}}", "current task prompt"),
        new("{{previous.output}}", "previous successful step output")
        {
            SupportsStructuredSelector = true,
        },
        new("{{stepId.output}}", "a prior named step output")
        {
            RequiresPriorStepId = true,
        },
        new("{{stepId.output.property}}", "structured JSON selection")
        {
            DisplayText = "{{stepId.output.property}} and {{stepId.output.items[0].name}}",
            RequiresPriorStepId = true,
            SupportsStructuredSelector = true,
        },
        new("{{workflow.context}}", "ordered successful outputs before the current step"),
        new("{{ModelName}}", "active model name"),
        new("{{ModelSettings}}", "effective Temperature and Top P"),
        new("{{ModelContextSize}}", "declared context-window size in tokens"),
        new("{{ModelInfo}}", "active model name and effective generation settings"),
        new("{{Today}}", "local date")
        {
            DisplayText = "{{Today}} or {{Today[custom .NET format]}}",
        },
        new("{{Now}}", "local date and time")
        {
            DisplayText = "{{Now}} or {{Now[custom .NET format]}}",
        },
    ];

    /// <summary>
    /// Gets deterministic placeholder documentation for model context and user assistance.
    /// </summary>
    /// <remarks>
    /// This is the ordered text projection of <see cref="Entries"/>. The user interface never
    /// parses these sentences to recover placeholder text.
    /// </remarks>
    public static IReadOnlyList<string> Definitions { get; } =
        [.. Entries.Select(static entry => $"{entry.DisplayText} - {entry.Description}")];


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
        return DateTimePlaceholderFormatter.NormalizeFormat(format);
    }

    /// <summary>
    /// Determines whether a marker is valid workflow-only data in a prompt-only context.
    /// </summary>
    /// <param name="placeholder">The trimmed placeholder body.</param>
    /// <returns><see langword="true"/> for workflow-only task, context, or output markers.</returns>
    public static bool IsWorkflowOnlyAgentMarker(string placeholder)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        if (placeholder.Equals("task.prompt", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Equals("workflow.context", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string outputMarker = ".output";
        var markerIndex = placeholder.IndexOf(
            outputMarker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0 ||
            !WorkflowIdPattern().IsMatch(placeholder[..markerIndex]))
        {
            return false;
        }

        var suffix = placeholder[(markerIndex + outputMarker.Length)..];
        return suffix.Length == 0 || StructuredSelectorPattern().IsMatch(suffix);
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
            placeholder.Equals("workflow.context", StringComparison.OrdinalIgnoreCase) ||
            ModelPromptPlaceholderResolver.IsSupported(placeholder))
        {
            error = null;
            return true;
        }

        var dateTime = DateTimePlaceholderFormatter.Resolve(
            placeholder,
            DateTimeOffset.UnixEpoch);
        if (dateTime.Status != DateTimePlaceholderStatus.NotRecognized)
        {
            if (dateTime.Status == DateTimePlaceholderStatus.InvalidFormat)
            {
                error = $"Placeholder '{{{{{placeholder}}}}}' has an invalid date/time format.";
                return false;
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
        @"^(?:(?:\.[A-Za-z_][A-Za-z0-9_-]*)|(?:\[[0-9]+\]))+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex StructuredSelectorPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkflowIdPattern();
}
