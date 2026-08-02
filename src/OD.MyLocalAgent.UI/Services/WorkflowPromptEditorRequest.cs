namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Captures the current workflow supplied to the memory-only Workflow Prompt Editor.
/// </summary>
/// <param name="WorkflowText">The current workflow editor text, which must be valid JSON.</param>
public sealed record WorkflowPromptEditorRequest(string WorkflowText);
