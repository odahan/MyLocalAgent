namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Requests the identifier for a clone of a persisted task.
/// </summary>
public interface ITaskCloneDialogService
{
    /// <summary>
    /// Shows the clone identifier dialog.
    /// </summary>
    /// <param name="sourceTaskId">The identifier of the task being cloned.</param>
    /// <returns>The requested identifier, or <see langword="null"/> when canceled.</returns>
    string? Show(string sourceTaskId);
}
