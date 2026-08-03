using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Builds content-free durable warning metadata for prompt placeholder expansion.
/// </summary>
public static class AgentPromptPlaceholderWarning
{
    /// <summary>
    /// Gets the stable warning code used by reports and diagnostics.
    /// </summary>
    public const string Code = "AGENT_PROMPT_PLACEHOLDERS_UNRESOLVED";

    /// <summary>
    /// Builds a content-free warning summary containing aggregate counts only.
    /// </summary>
    /// <param name="result">The resolution result to summarize.</param>
    /// <returns>An empty string when no warning exists; otherwise a safe warning line.</returns>
    public static string FormatSummary(AgentPromptPlaceholderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.TotalUnresolvedCount == 0)
        {
            return string.Empty;
        }

        var total = result.TotalUnresolvedCount;
        var builder = new StringBuilder();
        builder.Append("Warning: ");
        builder.Append(total);
        builder.Append(" agent prompt placeholder occurrence");
        builder.Append(total == 1 ? " was" : "s were");
        builder.Append(" left unchanged (");

        var first = true;
        AppendCount(builder, result, AgentPromptPlaceholderWarningKind.Unknown, "unknown", ref first);
        AppendCount(builder, result, AgentPromptPlaceholderWarningKind.WorkflowOnly, "workflow-only", ref first);
        AppendCount(builder, result, AgentPromptPlaceholderWarningKind.InvalidDateTimeFormat, "invalid date/time format", ref first);
        AppendCount(builder, result, AgentPromptPlaceholderWarningKind.MalformedBraces, "malformed braces", ref first);
        builder.Append(").");
        return builder.ToString();
    }

    private static void AppendCount(
        StringBuilder builder,
        AgentPromptPlaceholderResult result,
        AgentPromptPlaceholderWarningKind kind,
        string label,
        ref bool first)
    {
        var count = result.GetCount(kind);
        if (count == 0)
        {
            return;
        }

        if (!first)
        {
            builder.Append(", ");
        }

        builder.Append(count);
        builder.Append(' ');
        builder.Append(label);
        first = false;
    }
}
