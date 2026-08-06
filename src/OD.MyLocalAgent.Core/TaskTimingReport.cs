using System.Globalization;
using System.Text;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Builds a human-readable timing analysis for one task execution.
/// </summary>
public static class TaskTimingReport
{
    private const string Separator =
        "------------------------------------------------------------";

    /// <summary>
    /// Formats the timing analysis from workflow step results.
    /// </summary>
    /// <param name="startedUtc">The UTC start time.</param>
    /// <param name="endedUtc">The UTC end time.</param>
    /// <param name="entries">The per-step timing entries.</param>
    /// <returns>The formatted report text.</returns>
    public static string Build(
        DateTimeOffset startedUtc,
        DateTimeOffset endedUtc,
        IReadOnlyList<TaskTimingEntry> entries)
    {
        var totalDuration = endedUtc - startedUtc;
        var startedLocal = startedUtc.ToLocalTime();
        var endedLocal = endedUtc.ToLocalTime();

        var sb = new StringBuilder();
        sb.AppendLine(Separator);
        sb.AppendLine("TIMING ANALYSIS");
        sb.AppendLine(Separator);
        sb.AppendLine(
            $"  Started  : {startedLocal:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine(
            $"  Ended    : {endedLocal:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine(
            $"  Duration : {FormatDuration(totalDuration)}");

        if (entries.Count == 0)
        {
            sb.AppendLine(Separator);
            return sb.ToString();
        }

        sb.AppendLine();
        sb.AppendLine("  Breakdown by operation:");

        var totalMs = totalDuration.TotalMilliseconds;
        var aggregated = entries
            .GroupBy(
                static e => (e.Category, e.Name),
                static (key, group) => new
                {
                    key.Category,
                    key.Name,
                    Duration = TimeSpan.FromMilliseconds(
                        group.Sum(static e => e.Duration.TotalMilliseconds)),
                    Count = group.Count(),
                })
            .OrderByDescending(static x => x.Duration)
            .ToList();

        var labelWidth = aggregated
            .Select(static x => FormatLabel(x.Category, x.Name).Length)
            .Append("(LLM processing)".Length)
            .DefaultIfEmpty(0)
            .Max();
        labelWidth = Math.Max(labelWidth, 20);

        foreach (var item in aggregated)
        {
            var label = FormatLabel(item.Category, item.Name);
            var durationText = FormatDuration(item.Duration);
            var percent = totalMs > 0
                ? item.Duration.TotalMilliseconds / totalMs * 100.0
                : 0.0;
            var countText = item.Count > 1
                ? $" ×{item.Count}"
                : string.Empty;
            sb.AppendLine(
                $"  {label.PadRight(labelWidth)}  {durationText,10}  {percent,6:F1} %{countText}");
        }

        var measuredMs = entries
            .Where(static e => e.Category == TaskTimingCollector.ToolCategory)
            .Sum(static e => e.Duration.TotalMilliseconds);
        var llmMs = totalMs - measuredMs;
        if (llmMs > 1.0)
        {
            var llmDuration = TimeSpan.FromMilliseconds(llmMs);
            var percent = totalMs > 0 ? llmMs / totalMs * 100.0 : 0.0;
            sb.AppendLine(
                $"  {"(LLM processing)".PadRight(labelWidth)}  {FormatDuration(llmDuration),10}  {percent,6:F1} %");
        }

        var hasGuardrails = entries.Any(
            static e => e.Category == TaskTimingCollector.GuardrailCategory);
        if (hasGuardrails)
        {
            sb.AppendLine();
            sb.AppendLine("  * guardrail times are included within the tool times above.");
        }

        sb.AppendLine(Separator);
        return sb.ToString();
    }

    private static string FormatLabel(string category, string name)
    {
        if (string.Equals(category, TaskTimingCollector.AiPromptCategory, StringComparison.Ordinal))
        {
            return "ai.prompt";
        }

        if (string.Equals(category, TaskTimingCollector.GuardrailCategory, StringComparison.Ordinal))
        {
            return $"guardrail:{name}";
        }

        return $"tool:{name}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMilliseconds < 1000)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:F0} ms",
                duration.TotalMilliseconds);
        }

        if (duration.TotalSeconds < 60)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:F1} s",
                duration.TotalSeconds);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}m {1:F0}s",
            (int)duration.TotalMinutes,
            duration.Seconds);
    }
}
