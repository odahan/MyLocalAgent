using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Creates one transient prompt editor ViewModel and displays the modal editor window.
/// </summary>
public sealed class WorkflowPromptEditorDialogService : IWorkflowPromptEditorDialogService
{
    private readonly IServiceProvider _services;
    private readonly IDesktopInteractionService _desktopInteraction;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowPromptEditorDialogService"/> class.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="desktopInteraction">The desktop interaction service used for confirmations.</param>
    public WorkflowPromptEditorDialogService(
        IServiceProvider services,
        IDesktopInteractionService desktopInteraction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(desktopInteraction);
        _services = services;
        _desktopInteraction = desktopInteraction;
    }

    /// <inheritdoc />
    public WorkflowPromptEditorResult? Show(WorkflowPromptEditorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewModel = ActivatorUtilities.CreateInstance<WorkflowPromptEditorViewModel>(_services);
        viewModel.Initialize(request);
        var window = new WorkflowPromptEditorWindow
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
            if (closingByCommand || !viewModel.HasChanges)
            {
                return;
            }

            if (_desktopInteraction.Confirm(
                    "Discard prompt edits",
                    "The prompt editor contains changes that have not been applied. Discard them?"))
            {
                closingByCommand = true;
                return;
            }

            e.Cancel = true;
        }
    }
}
