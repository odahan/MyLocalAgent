using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Pages;

/// <summary>
/// Displays task management and child-runner output.
/// </summary>
public partial class TasksPage : UserControl
{
    private ScrollViewer? _runnerOutputScrollViewer;
    private double _pausedRunnerOutputOffset;
    private bool _runnerScrollPending;

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
        if (DataContext is TasksViewModel { IsShowingRunContext: false })
        {
            QueueRunnerOutputScrollUpdate();
        }
    }

    private void OnRunnerAutoScrollModeChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || !IsLoaded)
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            QueueRunnerOutputScrollUpdate();
            return;
        }

        if (GetRunnerOutputScrollViewer() is { } scrollViewer)
        {
            _pausedRunnerOutputOffset = scrollViewer.VerticalOffset;
        }
    }

    private void OnRunnerOutputScrollChanged(
        object sender,
        ScrollChangedEventArgs e)
    {
        _runnerOutputScrollViewer ??= e.OriginalSource as ScrollViewer;
        if (_runnerScrollPending ||
            RunnerAutoScrollCheckBox.IsChecked == true ||
            DataContext is not TasksViewModel { IsShowingRunContext: false })
        {
            return;
        }

        _pausedRunnerOutputOffset = e.VerticalOffset;
    }

    private void QueueRunnerOutputScrollUpdate()
    {
        if (_runnerScrollPending)
        {
            return;
        }

        _runnerScrollPending = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () =>
            {
                try
                {
                    if (!IsLoaded ||
                        DataContext is not TasksViewModel { IsShowingRunContext: false })
                    {
                        return;
                    }

                    if (RunnerAutoScrollCheckBox.IsChecked == true)
                    {
                        RunnerOutputTextBox.CaretIndex = RunnerOutputTextBox.Text.Length;
                        RunnerOutputTextBox.ScrollToEnd();
                        return;
                    }

                    if (GetRunnerOutputScrollViewer() is { } scrollViewer)
                    {
                        scrollViewer.ScrollToVerticalOffset(
                            Math.Min(
                                _pausedRunnerOutputOffset,
                                scrollViewer.ScrollableHeight));
                    }
                }
                finally
                {
                    _runnerScrollPending = false;
                }
            });
    }

    private ScrollViewer? GetRunnerOutputScrollViewer()
    {
        if (_runnerOutputScrollViewer is not null)
        {
            return _runnerOutputScrollViewer;
        }

        _ = RunnerOutputTextBox.ApplyTemplate();
        _runnerOutputScrollViewer =
            RunnerOutputTextBox.Template.FindName(
                "PART_ContentHost",
                RunnerOutputTextBox) as ScrollViewer;
        return _runnerOutputScrollViewer;
    }
}
