using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI;

/// <summary>
/// Hosts the application navigation shell.
/// </summary>
public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closeConfirmationPending;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void NavigationListBox_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox ||
            e.OriginalSource is not DependencyObject origin ||
            ItemsControl.ContainerFromElement(listBox, origin) is not ListBoxItem
            {
                IsSelected: true,
                DataContext: NavigationItemViewModel item,
            } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.Receive(new NavigateMessage(item.Route));
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeApproved)
        {
            return;
        }

        e.Cancel = true;
        if (_closeConfirmationPending)
        {
            return;
        }

        _closeConfirmationPending = true;
        try
        {
            if (DataContext is not MainWindowViewModel viewModel ||
                await viewModel.CanCloseAsync().ConfigureAwait(true))
            {
                _closeApproved = true;

                // Close() must not run while the Closing event is still on the stack,
                // otherwise WPF ignores the request and the window stays open.
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, Close);
            }
        }
        finally
        {
            _closeConfirmationPending = false;
        }
    }
}
