using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.ViewModels;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Captures the current task draft supplied to the memory-only workflow helper.
/// </summary>
/// <param name="Mode">The helper mode.</param>
/// <param name="Workflow">The locally parsed and validated workflow.</param>
/// <param name="TaskId">The current draft task id.</param>
/// <param name="TaskName">The current draft task name.</param>
/// <param name="Schedule">The current draft schedule.</param>
/// <param name="Prompt">The current draft prompt.</param>
/// <param name="ErrorPolicy">The current task error policy.</param>
/// <param name="ToolGroups">The current task tool groups.</param>
/// <param name="FileSystemAccess">The current task filesystem rules.</param>
/// <param name="Safety">The current global Safety settings.</param>
public sealed record AIWorkflowHelperRequest(
    AIWorkflowHelperMode Mode,
    IReadOnlyList<WorkflowStepDefinition> Workflow,
    string TaskId,
    string TaskName,
    string Schedule,
    string Prompt,
    WorkflowErrorPolicy ErrorPolicy,
    IReadOnlyList<string> ToolGroups,
    IReadOnlyList<FileSystemAccessRule> FileSystemAccess,
    SafetyOptions Safety)
{
    /// <summary>
    /// Gets the current unsaved task generation overrides.
    /// </summary>
    public GenerationOptions? TaskGenerationOptions { get; init; }

    /// <summary>
    /// Gets the current global generation defaults used for deterministic provenance.
    /// </summary>
    public AIGenerationDefaults GenerationDefaults { get; init; } = new();
}
