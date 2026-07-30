namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Opens the reusable modal AI Workflow Helper window.
/// </summary>
public interface IAIWorkflowHelperDialogService
{
    /// <summary>
    /// Shows the helper for one task draft.
    /// </summary>
    /// <param name="request">The memory-only helper request.</param>
    /// <returns>The applied authoring result, or <see langword="null"/>.</returns>
    AIWorkflowHelperResult? Show(AIWorkflowHelperRequest request);
}
