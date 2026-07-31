using System.Windows;
using System.Windows.Controls;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Pages;

/// <summary>
/// Displays task management and child-runner output.
/// </summary>
public partial class TasksPage : UserControl
{
    private double? _previousWindowHeight;

    /// <summary>
    /// Initializes a new instance of the <see cref="TasksPage"/> class.
    /// </summary>
    public TasksPage()
    {
        InitializeComponent();
    }

    private void OnRunnerOutputTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (DataContext is not TasksViewModel { IsDebugMode: true })
        {
            RunnerOutputTextBox.ScrollToEnd();
        }
    }

    private void OnLargeRunnerOutputModeChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox)
        {
            return;
        }

        var isLarge = checkBox.IsChecked == true;

        var window = Window.GetWindow(this);
        if (window is null || window.WindowState == WindowState.Maximized)
        {
            return;
        }

        if (isLarge)
        {
            _previousWindowHeight ??= window.Height;
            window.Height = Math.Max(window.Height, 1_180);
        }
        else if (_previousWindowHeight is { } previousHeight)
        {
            window.Height = previousHeight;
            _previousWindowHeight = null;
        }
    }
}
