using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Extracts and rewrites eligible text arguments already present in a workflow deterministically.
/// </summary>
/// <remarks>
/// This class is pure and free of user-interface dependencies. It never adds, deletes,
/// reorders, renames, or re-types a step, and never touches unrelated arguments, identifiers,
/// policies, retries, generation options, models, or tool permissions.
/// </remarks>
public static class WorkflowPromptEditingService
{
    private const string PromptArgument = "prompt";
    private const string IncludeContextArgument = "includeContext";

    /// <summary>
    /// Normalizes CRLF and lone CR sequences to LF.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The LF-canonical text.</returns>
    /// <remarks>This is the only permitted prompt-text transformation.</remarks>
    public static string NormalizeLineEndings(string? text) =>
        text is null
            ? string.Empty
            : text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);

    /// <summary>
    /// Extracts every <c>ai.prompt</c> step in declaration order.
    /// </summary>
    /// <param name="workflow">The parsed workflow.</param>
    /// <returns>The editable prompt entries.</returns>
    public static IReadOnlyList<WorkflowPromptEntry> Extract(
        IReadOnlyList<WorkflowStepDefinition> workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var entries = new List<WorkflowPromptEntry>();
        var priorIds = new List<string>();

        for (var position = 0; position < workflow.Count; position++)
        {
            var step = workflow[position];
            var definition = ToolMap.Find(step.Step);
            var editableTextArguments = definition?.ArgumentDefinitions
                .Where(static argument =>
                    argument.Kind == WorkflowArgumentKind.String &&
                    !argument.IsExcludedFromWorkflowTextEditor)
                .ToArray() ?? [];

            foreach (var argument in editableTextArguments)
            {
                var hasArgument = TryGetArgument(step, argument.Name, out var textValue);
                if (!hasArgument)
                {
                    continue;
                }

                var hasText = hasArgument &&
                    textValue.ValueKind == JsonValueKind.String;
                var rawText = hasText ? textValue.GetString() ?? string.Empty : string.Empty;
                var supportsContext = IsPromptStep(step) &&
                    string.Equals(argument.Name, PromptArgument, StringComparison.OrdinalIgnoreCase);
                entries.Add(new WorkflowPromptEntry
                {
                    Alias = definition!.Alias,
                    ArgumentName = argument.Name,
                    SupportsIncludeContext = supportsContext,
                    Position = position,
                    ExplicitId = step.Id,
                    EffectiveId = step.EffectiveId,
                    PromptText = NormalizeLineEndings(rawText),
                    IncludeContext = supportsContext &&
                        TryGetArgument(step, IncludeContextArgument, out var context) &&
                        context.ValueKind == JsonValueKind.True,
                    RequiresLineEndingNormalization = rawText.Contains('\r', StringComparison.Ordinal),
                    IsRepairCase = !hasText,
                    OnError = step.OnError,
                    RetryCount = step.RetryCount,
                    RetryBackoffSeconds = step.RetryBackoffSeconds,
                    GenerationOptions = step.GenerationOptions,
                    Model = step.Model,
                    ToolAliases = step.ToolAliases,
                    KnowledgeAccess = step.KnowledgeAccess,
                    PriorStepIds = [.. priorIds],
                });
            }

            priorIds.Add(step.EffectiveId);
        }

        return entries;
    }

    /// <summary>
    /// Applies targeted prompt edits and returns the canonical workflow text.
    /// </summary>
    /// <param name="workflow">The parsed workflow.</param>
    /// <param name="edits">The edits keyed by zero-based workflow position.</param>
    /// <returns>The canonical JSON array.</returns>
    /// <exception cref="WorkflowValidationException">
    /// Thrown when an edit targets a position that is not an <c>ai.prompt</c> step.
    /// </exception>
    public static string Apply(
        IReadOnlyList<WorkflowStepDefinition> workflow,
        IReadOnlyList<WorkflowPromptEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(edits);

        var updated = workflow.ToList();
        foreach (var edit in edits)
        {
            if (edit.Position < 0 || edit.Position >= updated.Count ||
                !TryGetArgument(updated[edit.Position], edit.ArgumentName, out _) ||
                ToolMap.Find(updated[edit.Position].Step)?.ArgumentDefinitions.Any(
                    argument =>
                        argument.Kind == WorkflowArgumentKind.String &&
                        !argument.IsExcludedFromWorkflowTextEditor &&
                        string.Equals(
                        argument.Name,
                        edit.ArgumentName,
                        StringComparison.OrdinalIgnoreCase)) != true)
            {
                throw new WorkflowValidationException(
                    $"Workflow position {edit.Position} does not expose editable text argument '{edit.ArgumentName}'.");
            }

            var step = updated[edit.Position];
            updated[edit.Position] = step with
            {
                IsStringForm = false,
                Args = RewriteArguments(step, edit),
            };
        }

        return WorkflowDefinitionSerializer.Serialize(updated);
    }

    /// <summary>
    /// Determines whether a prompt already carries prior output or accumulated context.
    /// </summary>
    /// <param name="promptText">The prompt working text.</param>
    /// <param name="priorStepIds">The effective identifiers declared before the step.</param>
    /// <returns><see langword="true"/> when a duplication warning applies.</returns>
    public static bool ReferencesWorkflowOutput(
        string? promptText,
        IReadOnlyList<string> priorStepIds)
    {
        ArgumentNullException.ThrowIfNull(priorStepIds);
        if (string.IsNullOrEmpty(promptText))
        {
            return false;
        }

        foreach (var placeholder in WorkflowPlaceholderCatalog.Extract(promptText))
        {
            if (placeholder.Equals("workflow.context", StringComparison.OrdinalIgnoreCase) ||
                IsOutputReference(placeholder, "previous"))
            {
                return true;
            }

            if (priorStepIds.Any(id => IsOutputReference(placeholder, id)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOutputReference(string placeholder, string stepId) =>
        placeholder.StartsWith($"{stepId}.output", StringComparison.OrdinalIgnoreCase);

    private static bool IsPromptStep(WorkflowStepDefinition step) =>
        string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetArgument(
        WorkflowStepDefinition step,
        string name,
        out JsonElement value)
    {
        value = default;
        if (step.Args.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in step.Args.EnumerateObject())
        {
            if (property.NameEquals(name))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static JsonElement RewriteArguments(
        WorkflowStepDefinition step,
        WorkflowPromptEdit edit)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            var textWritten = false;
            writer.WriteStartObject();
            if (step.Args.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in step.Args.EnumerateObject())
                {
                    if (property.Name.Equals(edit.ArgumentName, StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteString(
                            property.Name,
                            NormalizeLineEndings(edit.PromptText));
                        textWritten = true;
                        continue;
                    }

                    property.WriteTo(writer);
                }
            }

            if (!textWritten)
            {
                writer.WriteString(edit.ArgumentName, NormalizeLineEndings(edit.PromptText));
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
