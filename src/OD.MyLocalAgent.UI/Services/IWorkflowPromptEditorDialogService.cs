namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Displays the deterministic, memory-only Workflow Prompt Editor dialog.
/// </summary>
public interface IWorkflowPromptEditorDialogService
{
    /// <summary>
    /// Shows the modal dialog and returns the single applied result.
    /// </summary>
    /// <param name="request">The current workflow snapshot.</param>
    /// <returns>
    /// The applied result, or <see langword="null"/> when the session was cancelled.
    /// </returns>
    WorkflowPromptEditorResult? Show(WorkflowPromptEditorRequest request);
}
