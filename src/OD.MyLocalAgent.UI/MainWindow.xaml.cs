using System.ComponentModel;
using System.Windows;
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
                Close();
            }
        }
        finally
        {
            _closeConfirmationPending = false;
        }
    }
}
