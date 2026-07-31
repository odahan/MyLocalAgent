using System.Windows;
using System.Windows.Input;

namespace OD.MyLocalAgent.UI;

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
    public ConfirmationDialogWindow(
        string title,
        string message,
        bool requiresConfirmation = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        InitializeComponent();
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        if (!requiresConfirmation)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            ConfirmButton.Content = "OK";
            StatusIcon.Foreground = (System.Windows.Media.Brush)FindResource(
                "ErrorBrush");
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
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
