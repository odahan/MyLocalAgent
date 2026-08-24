using System.Text;
using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Inserts one workflow step at a caret position, preferring structured JSON modification.
/// </summary>
/// <remarks>
/// This class is pure and free of user-interface dependencies. Callers supply plain text and a
/// UTF-16 caret index; all UTF-16/UTF-8 mapping and element-span analysis happens here.
/// </remarks>
public static class WorkflowCaretInsertionService
{
    /// <summary>
    /// Gets the exact warning returned when the workflow text could not be parsed.
    /// </summary>
    public const string FallbackWarning =
        "Workflow JSON is invalid. The snippet was inserted as text; add or repair JSON separators manually.";

    /// <summary>
    /// Inserts a step into workflow text at the supplied caret position.
    /// </summary>
    /// <param name="text">The current workflow text, which may be invalid JSON.</param>
    /// <param name="caretIndex">The current UTF-16 caret index, clamped by this method.</param>
    /// <param name="step">The step to insert.</param>
    /// <returns>The deterministic insertion outcome.</returns>
    public static WorkflowCaretInsertionResult Insert(
        string? text,
        int caretIndex,
        WorkflowStepDefinition step)
    {
        ArgumentNullException.ThrowIfNull(step);

        var source = text ?? string.Empty;
        var caret = ClampCaret(source, caretIndex);

        return TryInsertStructured(source, caret, step, out var structured)
            ? structured
            : InsertVerbatim(source, caret, step);
    }

    /// <summary>
    /// Clamps a caret index into the text and away from the inside of a surrogate pair.
    /// </summary>
    /// <param name="text">The text the caret belongs to.</param>
    /// <param name="caretIndex">The requested UTF-16 caret index.</param>
    /// <returns>A caret index on a valid scalar boundary.</returns>
    public static int ClampCaret(string? text, int caretIndex)
    {
        var source = text ?? string.Empty;
        var caret = Math.Clamp(caretIndex, 0, source.Length);
        if (caret > 0 && caret < source.Length &&
            char.IsLowSurrogate(source[caret]) &&
            char.IsHighSurrogate(source[caret - 1]))
        {
            caret--;
        }

        return caret;
    }

    private static bool TryInsertStructured(
        string source,
        int caret,
        WorkflowStepDefinition step,
        out WorkflowCaretInsertionResult result)
    {
        result = null!;
        IReadOnlyList<WorkflowStepDefinition> workflow;
        IReadOnlyList<(int Start, int End)> spans;
        try
        {
            workflow = WorkflowDefinitionSerializer.Deserialize(source);
            spans = WorkflowStepEditingService.GetSpans(source);
        }
        catch (Exception exception)
            when (exception is WorkflowValidationException or JsonException or ArgumentException)
        {
            return false;
        }

        var caretByte = Encoding.UTF8.GetByteCount(source.AsSpan(0, caret));
        var location = WorkflowStepEditingService.Locate(source, caret);
        var mutation = WorkflowStepEditingService.Insert(
            source,
            location.Location?.Index,
            spans.Count > 0 && caretByte <= spans[0].Start
                ? WorkflowStepInsertionPosition.Before
                : WorkflowStepInsertionPosition.After,
            step);

        result = new WorkflowCaretInsertionResult
        {
            Text = mutation.Text,
            CaretIndex = mutation.CaretIndex,
            InsertedIndex = mutation.AffectedIndex,
            IsStructured = true,
            CanonicalFormattingApplied = true,
        };
        return true;
    }

    private static WorkflowCaretInsertionResult InsertVerbatim(
        string source,
        int caret,
        WorkflowStepDefinition step)
    {
        var snippet = JsonSerializer.Serialize(step, ScheduledTaskSerializer.Options);
        var indent = LeadingWhitespace(source, caret);
        if (indent.Length > 0)
        {
            var newLine = snippet.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            snippet = snippet.Replace(newLine, newLine + indent, StringComparison.Ordinal);
        }

        return new WorkflowCaretInsertionResult
        {
            Text = string.Concat(source.AsSpan(0, caret), snippet, source.AsSpan(caret)),
            CaretIndex = caret,
            InsertedIndex = -1,
            IsStructured = false,
            CanonicalFormattingApplied = false,
            Warning = FallbackWarning,
        };
    }

    private static string LeadingWhitespace(string source, int caret)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(caret - 1, 0)) + 1;
        if (caret == 0)
        {
            lineStart = 0;
        }

        var end = lineStart;
        while (end < source.Length && end < caret && (source[end] == ' ' || source[end] == '\t'))
        {
            end++;
        }

        return source[lineStart..end];
    }

}
