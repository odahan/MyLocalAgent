using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Creates one transient catalog ViewModel and displays the modal catalog window.
/// </summary>
public sealed class WorkflowToolCatalogDialogService : IWorkflowToolCatalogDialogService
{
    private readonly IServiceProvider _services;
    private readonly IDesktopInteractionService _desktopInteraction;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowToolCatalogDialogService"/> class.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="desktopInteraction">The desktop interaction service used for confirmations.</param>
    public WorkflowToolCatalogDialogService(
        IServiceProvider services,
        IDesktopInteractionService desktopInteraction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(desktopInteraction);
        _services = services;
        _desktopInteraction = desktopInteraction;
    }

    /// <inheritdoc />
    public WorkflowToolCatalogResult? Show(WorkflowToolCatalogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewModel = ActivatorUtilities.CreateInstance<WorkflowToolCatalogViewModel>(_services);
        viewModel.Initialize(request);
        var window = new WorkflowToolCatalogWindow
        {
            DataContext = viewModel,
            Owner = Application.Current.MainWindow,
        };
        var closingByCommand = false;
        viewModel.CloseRequested += OnCloseRequested;
        window.Closing += OnClosing;

        try
        {
            _ = window.ShowDialog();
            return viewModel.Result;
        }
        finally
        {
            viewModel.CloseRequested -= OnCloseRequested;
            window.Closing -= OnClosing;
        }

        void OnCloseRequested(object? sender, EventArgs e)
        {
            closingByCommand = true;
            window.Close();
        }

        void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (closingByCommand || !viewModel.HasEffectiveChanges)
            {
                return;
            }

            if (_desktopInteraction.Confirm(
                    "Discard catalog session",
                    "The catalog session contains insertions that have not been returned to the editor. Discard them?"))
            {
                closingByCommand = true;
                return;
            }

            e.Cancel = true;
        }
    }
}
