using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Expands the closed prompt-only placeholder set in one bounded, non-recursive pass.
/// </summary>
public static class AgentPromptPlaceholderResolver
{
    /// <summary>
    /// Resolves a task-owned agent prompt against explicit identity and local time.
    /// </summary>
    /// <param name="prompt">The original prompt text.</param>
    /// <param name="taskId">The exact persisted task identifier.</param>
    /// <param name="taskName">The exact persisted task name.</param>
    /// <param name="localNow">The single local-time snapshot for this expansion.</param>
    /// <param name="modelContext">
    /// The optional active-model snapshot. Model markers remain deferred without a snapshot.
    /// </param>
    /// <returns>The expanded prompt and content-free unresolved source descriptors.</returns>
    public static AgentPromptPlaceholderResult Resolve(
        string prompt,
        string taskId,
        string taskName,
        DateTimeOffset localNow,
        ModelPromptPlaceholderContext? modelContext = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(taskId);
        ArgumentNullException.ThrowIfNull(taskName);

        StringBuilder? output = null;
        var occurrences = new List<AgentPromptPlaceholderOccurrence>();
        var position = 0;
        var copiedThrough = 0;

        while (position < prompt.Length)
        {
            if (StartsWith(prompt, position, "{{"))
            {
                var end = FindBalancedEnd(prompt, position, out var nested);
                if (end < 0)
                {
                    occurrences.Add(
                        new AgentPromptPlaceholderOccurrence(
                            AgentPromptPlaceholderWarningKind.MalformedBraces,
                            position,
                            prompt.Length - position));
                    break;
                }

                var length = end - position;
                var bodyStart = position + 2;
                var bodyLength = length - 4;
                var body = prompt.AsSpan(bodyStart, bodyLength);
                if (nested ||
                    body.Trim().Length == 0 ||
                    body.IndexOfAny('{', '}') >= 0)
                {
                    occurrences.Add(
                        new AgentPromptPlaceholderOccurrence(
                            AgentPromptPlaceholderWarningKind.MalformedBraces,
                            position,
                            length));
                    position = end;
                    continue;
                }

                var placeholder = body.Trim().ToString();
                var replacement = ResolveSupported(
                    placeholder,
                    taskId,
                    taskName,
                    localNow,
                    modelContext,
                    out var warningKind);
                if (replacement is null)
                {
                    occurrences.Add(
                        new AgentPromptPlaceholderOccurrence(
                            warningKind,
                            position,
                            length));
                }
                else
                {
                    output ??= new StringBuilder(prompt.Length);
                    output.Append(prompt, copiedThrough, position - copiedThrough);
                    output.Append(replacement);
                    copiedThrough = end;
                }

                position = end;
                continue;
            }

            if (StartsWith(prompt, position, "}}"))
            {
                occurrences.Add(
                    new AgentPromptPlaceholderOccurrence(
                        AgentPromptPlaceholderWarningKind.MalformedBraces,
                        position,
                        2));
                position += 2;
                continue;
            }

            position++;
        }

        if (output is null)
        {
            return new AgentPromptPlaceholderResult(prompt, occurrences);
        }

        output.Append(prompt, copiedThrough, prompt.Length - copiedThrough);
        return new AgentPromptPlaceholderResult(output.ToString(), occurrences);
    }

    private static string? ResolveSupported(
        string placeholder,
        string taskId,
        string taskName,
        DateTimeOffset localNow,
        ModelPromptPlaceholderContext? modelContext,
        out AgentPromptPlaceholderWarningKind warningKind)
    {
        if (placeholder.Equals("task.id", StringComparison.OrdinalIgnoreCase))
        {
            warningKind = default;
            return taskId;
        }

        if (placeholder.Equals("task.name", StringComparison.OrdinalIgnoreCase))
        {
            warningKind = default;
            return taskName;
        }

        if (ModelPromptPlaceholderResolver.IsSupported(placeholder))
        {
            warningKind = default;
            return modelContext is not null &&
                ModelPromptPlaceholderResolver.TryResolve(
                    placeholder,
                    modelContext,
                    out var modelValue)
                ? modelValue
                : $"{{{{{placeholder}}}}}";
        }

        var dateTime = DateTimePlaceholderFormatter.Resolve(placeholder, localNow);
        if (dateTime.Status == DateTimePlaceholderStatus.Resolved)
        {
            warningKind = default;
            return dateTime.Value;
        }

        if (dateTime.Status == DateTimePlaceholderStatus.InvalidFormat)
        {
            warningKind = AgentPromptPlaceholderWarningKind.InvalidDateTimeFormat;
            return null;
        }

        warningKind = WorkflowPlaceholderCatalog.IsWorkflowOnlyAgentMarker(placeholder)
            ? AgentPromptPlaceholderWarningKind.WorkflowOnly
            : AgentPromptPlaceholderWarningKind.Unknown;
        return null;
    }

    private static int FindBalancedEnd(
        string prompt,
        int start,
        out bool nested)
    {
        var depth = 1;
        nested = false;
        var position = start + 2;
        while (position < prompt.Length - 1)
        {
            if (StartsWith(prompt, position, "{{"))
            {
                nested = true;
                depth++;
                position += 2;
                continue;
            }

            if (StartsWith(prompt, position, "}}"))
            {
                depth--;
                position += 2;
                if (depth == 0)
                {
                    return position;
                }

                continue;
            }

            position++;
        }

        return -1;
    }

    private static bool StartsWith(string value, int position, string token) =>
        position <= value.Length - token.Length &&
        value.AsSpan(position, token.Length).SequenceEqual(token);
}
