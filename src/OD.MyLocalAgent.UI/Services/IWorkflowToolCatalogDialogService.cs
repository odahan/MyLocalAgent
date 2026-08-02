namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Displays the deterministic, memory-only Tool catalog dialog.
/// </summary>
public interface IWorkflowToolCatalogDialogService
{
    /// <summary>
    /// Shows the modal dialog and returns the accumulated session result.
    /// </summary>
    /// <param name="request">The current task draft snapshot.</param>
    /// <returns>
    /// The accumulated result, or <see langword="null"/> when the session was cancelled or
    /// produced no effective change.
    /// </returns>
    WorkflowToolCatalogResult? Show(WorkflowToolCatalogRequest request);
}
