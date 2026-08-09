using System.Windows;
using System.Windows.Input;

namespace OD.MyLocalAgent.UI;

/// <summary>
/// Identifies the action selected in a confirmation dialog.
/// </summary>
public enum ConfirmationDialogAction
{
    /// <summary>
    /// The dialog was canceled or closed.
    /// </summary>
    Cancel,

    /// <summary>
    /// The primary action was selected.
    /// </summary>
    Primary,

    /// <summary>
    /// The secondary action was selected.
    /// </summary>
    Secondary,
}

/// <summary>
/// Displays an application-themed modal confirmation or notice.
/// </summary>
public partial class ConfirmationDialogWindow : Window
{
    /// <summary>
    /// Initializes a new confirmation dialog or notice.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The confirmation message.</param>
    /// <param name="requiresConfirmation">
    /// Whether the dialog requires a deliberate confirmation rather than acknowledgement.
    /// </param>
    /// <param name="primaryButtonText">Optional text for the primary action.</param>
    /// <param name="cancelButtonText">Optional text for the cancel action.</param>
    /// <param name="secondaryButtonText">
    /// Optional text for a secondary action; omit it to hide that action.
    /// </param>
    /// <param name="isInformational">Whether the notice is informational rather than an error.</param>
    public ConfirmationDialogWindow(
        string title,
        string message,
        bool requiresConfirmation = true,
        string? primaryButtonText = null,
        string? cancelButtonText = null,
        string? secondaryButtonText = null,
        bool isInformational = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        InitializeComponent();
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        ConfirmButton.Content = primaryButtonText ?? "Continue";
        CancelButton.Content = cancelButtonText ?? "Cancel";
        if (!string.IsNullOrWhiteSpace(secondaryButtonText))
        {
            SecondaryButton.Content = secondaryButtonText;
            SecondaryButton.Visibility = Visibility.Visible;
        }

        if (!requiresConfirmation)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            ConfirmButton.Content = "OK";
            StatusIcon.Foreground = (System.Windows.Media.Brush)FindResource(
                isInformational ? "MutedTextBrush" : "ErrorBrush");
        }
    }

    /// <summary>
    /// Gets the action selected by the user.
    /// </summary>
    public ConfirmationDialogAction Action { get; private set; }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Action = ConfirmationDialogAction.Primary;
        DialogResult = true;
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        Action = ConfirmationDialogAction.Secondary;
        DialogResult = true;
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
