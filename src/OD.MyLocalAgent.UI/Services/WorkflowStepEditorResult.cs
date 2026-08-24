using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>Describes one atomic workflow-step editor outcome.</summary>
public sealed record WorkflowStepEditorResult(
    string WorkflowText,
    int CaretIndex,
    IReadOnlyList<string> AcceptedToolGroups,
    IReadOnlyList<TaskCapability> AcceptedCapabilities,
    IReadOnlyList<FileSystemAccessRule> AcceptedFileSystemAccess,
    string Summary)
{
    /// <summary>Gets the affected declaration position.</summary>
    public int AffectedIndex { get; init; } = -1;

    /// <summary>Gets the content-free operation kind.</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>Gets the affected immutable alias.</summary>
    public string? Alias { get; init; }

    /// <summary>Gets the affected effective id.</summary>
    public string? EffectiveId { get; init; }

    /// <summary>Gets content-free execution warnings.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Gets whether the complete workflow was canonically formatted.</summary>
    public bool CanonicalFormattingApplied { get; init; }
}
