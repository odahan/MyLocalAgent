using System.Text.RegularExpressions;
using System.Windows;

namespace OD.MyLocalAgent.UI;

/// <summary>
/// Collects and validates a new task identifier before cloning.
/// </summary>
public partial class TaskCloneDialogWindow : Window
{
    private static readonly Regex TaskIdPattern = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskCloneDialogWindow"/> class.
    /// </summary>
    /// <param name="sourceTaskId">The identifier of the task being cloned.</param>
    public TaskCloneDialogWindow(string sourceTaskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTaskId);
        InitializeComponent();
        TaskIdTextBox.Text = $"{sourceTaskId}-copy";
        TaskIdTextBox.SelectAll();
        TaskIdTextBox.Focus();
        ValidateTaskId();
    }

    /// <summary>Gets the accepted task identifier.</summary>
    public string TaskId { get; private set; } = string.Empty;

    private void TaskIdTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        ValidateTaskId();

    private void CloneButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateTaskId())
        {
            return;
        }

        TaskId = TaskIdTextBox.Text.Trim();
        DialogResult = true;
    }

    private bool ValidateTaskId()
    {
        var id = TaskIdTextBox.Text.Trim();
        var isValid = TaskIdPattern.IsMatch(id);
        ValidationText.Text = isValid ? string.Empty : "The id must match [a-z0-9-]+.";
        CloneButton.IsEnabled = isValid;
        return isValid;
    }
}
