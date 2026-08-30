using System.Windows;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Displays the modal task-cloning identifier dialog.
/// </summary>
public sealed class TaskCloneDialogService : ITaskCloneDialogService
{
    /// <inheritdoc />
    public string? Show(string sourceTaskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTaskId);
        var dialog = new TaskCloneDialogWindow(sourceTaskId)
        {
            Owner = Application.Current?.MainWindow,
        };
        return dialog.ShowDialog() == true ? dialog.TaskId : null;
    }
}
