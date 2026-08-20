namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Captures the current workflow supplied to the memory-only Workflow Prompt Editor.
/// </summary>
/// <param name="WorkflowText">The current workflow editor text, which must be valid JSON.</param>
/// <param name="EnabledToolGroups">The task groups whose agent tools may be selected.</param>
/// <param name="KnowledgeTags">The task Knowledge scope that a prompt step may narrow.</param>
public sealed record WorkflowPromptEditorRequest(
    string WorkflowText,
    IReadOnlyList<string>? EnabledToolGroups = null,
    IReadOnlyList<string>? KnowledgeTags = null);
