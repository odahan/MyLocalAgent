namespace OD.MyLocalAgent.UI.Services;

/// <summary>Displays the deterministic workflow step editor.</summary>
public interface IWorkflowStepEditorDialogService
{
    /// <summary>Shows a modal editor and returns one atomic draft change.</summary>
    WorkflowStepEditorResult? Show(WorkflowStepEditorRequest request);
}
