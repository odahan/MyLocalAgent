using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>Supplies the current memory-only task draft to the step editor.</summary>
public sealed record WorkflowStepEditorRequest(
    string WorkflowText,
    int CaretIndex,
    WorkflowStepEditorMode Mode,
    IReadOnlyList<string> ToolGroups,
    IReadOnlyList<FileSystemAccessRule> FileSystemAccess,
    IReadOnlyList<TaskCapability> Capabilities,
    IReadOnlyList<string> KnowledgeTags,
    WorkflowErrorPolicy ErrorPolicy,
    double Temperature,
    double TopP)
{
    /// <summary>Gets the inherited context-window budget for prompt steps.</summary>
    public int ContextWindowTokens { get; init; } = 8192;

    /// <summary>Gets the origin of the inherited context-window budget.</summary>
    public OD.MyLocalAgent.Ollama.GenerationOptionSource ContextWindowSource { get; init; } = OD.MyLocalAgent.Ollama.GenerationOptionSource.Settings;

    /// <summary>Gets the configured provider/default model offered without a network request.</summary>
    public string? ConfiguredModel { get; init; }

    /// <summary>Gets the task-level model override offered without a network request.</summary>
    public string? TaskModel { get; init; }

    /// <summary>Gets a complete non-persisted task draft used for provisional validation.</summary>
    public ScheduledTask? DraftTask { get; init; }
}

/// <summary>Defines the workflow step editor operation.</summary>
public enum WorkflowStepEditorMode
{
    /// <summary>Insert immediately before the selected step.</summary>
    InsertBefore,
    /// <summary>Insert immediately after the selected step.</summary>
    InsertAfter,
    /// <summary>Edit the selected step without allowing its alias to change.</summary>
    Edit,
}
