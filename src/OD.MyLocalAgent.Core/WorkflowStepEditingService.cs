using System.Text;
using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>Locates and atomically changes top-level workflow steps.</summary>
public static class WorkflowStepEditingService
{
    /// <summary>Locates the step selected by a UTF-16 caret position.</summary>
    public static WorkflowStepLocationResult Locate(string? text, int caretIndex)
    {
        var source = text ?? string.Empty;
        try
        {
            var steps = WorkflowDefinitionSerializer.Deserialize(source);
            var spans = GetSpans(source);
            if (spans.Count != steps.Count)
            {
                return WorkflowStepLocationResult.Invalid("Workflow JSON could not be mapped to top-level steps.");
            }

            if (steps.Count == 0)
            {
                return WorkflowStepLocationResult.Empty;
            }

            var caret = WorkflowCaretInsertionService.ClampCaret(source, caretIndex);
            var byteIndex = Encoding.UTF8.GetByteCount(source.AsSpan(0, caret));
            var index = 0;
            for (var candidate = spans.Count - 1; candidate >= 0; candidate--)
            {
                if (byteIndex >= spans[candidate].Start)
                {
                    index = candidate;
                    break;
                }
            }

            var span = spans[index];
            return new WorkflowStepLocationResult(
                new WorkflowStepLocation(
                    index,
                    Utf16Index(source, span.Start),
                    Utf16Index(source, span.End),
                    steps[index].Step,
                    steps[index].EffectiveId),
                null);
        }
        catch (Exception exception) when (exception is WorkflowValidationException or JsonException or ArgumentException)
        {
            return WorkflowStepLocationResult.Invalid(exception.Message);
        }
    }

    /// <summary>Inserts a step relative to an existing declaration position.</summary>
    public static WorkflowStepMutationResult Insert(
        string text,
        int? anchorIndex,
        WorkflowStepInsertionPosition position,
        WorkflowStepDefinition step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var workflow = Parse(text);
        var index = workflow.Count == 0 ? 0 : anchorIndex ?? throw new WorkflowValidationException("A workflow step must be selected before insertion.");
        if (index < 0 || index >= workflow.Count && workflow.Count != 0)
        {
            throw new WorkflowValidationException("The selected workflow step no longer exists.");
        }

        if (position == WorkflowStepInsertionPosition.After && workflow.Count > 0)
        {
            index++;
        }

        workflow.Insert(index, step);
        return CreateMutation(workflow, index, step);
    }

    /// <summary>Replaces one step while preserving its immutable Tool Map alias.</summary>
    public static WorkflowStepMutationResult Replace(string text, int targetIndex, string expectedAlias, WorkflowStepDefinition replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var workflow = Parse(text);
        VerifyTarget(workflow, targetIndex, expectedAlias);
        if (!string.Equals(replacement.Step, expectedAlias, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkflowValidationException("An existing workflow step cannot be changed to another tool. Delete it and insert a new step.");
        }

        workflow[targetIndex] = replacement;
        return CreateMutation(workflow, targetIndex, replacement);
    }

    /// <summary>Deletes exactly one step after checking its declaration-position precondition.</summary>
    public static WorkflowStepMutationResult Delete(string text, int targetIndex, string expectedAlias)
    {
        var workflow = Parse(text);
        VerifyTarget(workflow, targetIndex, expectedAlias);
        workflow.RemoveAt(targetIndex);
        var resultingIndex = workflow.Count == 0 ? -1 : Math.Min(targetIndex, workflow.Count - 1);
        return CreateMutation(workflow, resultingIndex, resultingIndex >= 0 ? workflow[resultingIndex] : null);
    }

    internal static IReadOnlyList<(int Start, int End)> GetSpans(string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(utf8);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            throw new WorkflowValidationException("Workflow JSON must contain one array and no surrounding content.");
        }

        var spans = new List<(int Start, int End)>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var start = (int)reader.TokenStartIndex;
            reader.Skip();
            spans.Add((start, (int)reader.BytesConsumed));
        }

        if (reader.TokenType != JsonTokenType.EndArray || reader.Read())
        {
            throw new WorkflowValidationException("Workflow JSON must contain one array and no surrounding content.");
        }

        return spans;
    }

    private static List<WorkflowStepDefinition> Parse(string text) => WorkflowDefinitionSerializer.Deserialize(text).ToList();

    private static void VerifyTarget(IReadOnlyList<WorkflowStepDefinition> workflow, int index, string expectedAlias)
    {
        if (index < 0 || index >= workflow.Count || !string.Equals(workflow[index].Step, expectedAlias, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkflowValidationException("The selected workflow step changed before this operation could be applied.");
        }
    }

    private static WorkflowStepMutationResult CreateMutation(IReadOnlyList<WorkflowStepDefinition> workflow, int index, WorkflowStepDefinition? affected)
    {
        var text = WorkflowDefinitionSerializer.Serialize(workflow);
        var caret = index < 0 ? Encoding.UTF8.GetByteCount(text.AsSpan(0, Math.Min(1, text.Length))) : WorkflowStepEditingService.GetSpans(text)[index].Start;
        return new WorkflowStepMutationResult(text, index, Utf16Index(text, caret), affected?.Step, affected?.EffectiveId, true, []);
    }

    private static int Utf16Index(string text, int utf8Index) => Encoding.UTF8.GetCharCount(Encoding.UTF8.GetBytes(text), 0, utf8Index);
}

/// <summary>Describes the selected top-level workflow step.</summary>
public sealed record WorkflowStepLocation(int Index, int Utf16Start, int Utf16End, string Alias, string EffectiveId);

/// <summary>Describes a location operation outcome.</summary>
public sealed record WorkflowStepLocationResult(WorkflowStepLocation? Location, string? Error)
{
    /// <summary>Gets the empty-array result.</summary>
    public static WorkflowStepLocationResult Empty { get; } = new(null, null);
    /// <summary>Creates an invalid-workflow result.</summary>
    public static WorkflowStepLocationResult Invalid(string error) => new(null, error);
}

/// <summary>Defines insertion placement relative to an anchor step.</summary>
public enum WorkflowStepInsertionPosition
{
    /// <summary>Insert immediately before the anchor.</summary>
    Before,
    /// <summary>Insert immediately after the anchor.</summary>
    After,
}

/// <summary>Describes one canonical structural workflow mutation.</summary>
public sealed record WorkflowStepMutationResult(string Text, int AffectedIndex, int CaretIndex, string? Alias, string? EffectiveId, bool CanonicalFormattingApplied, IReadOnlyList<string> Warnings);
