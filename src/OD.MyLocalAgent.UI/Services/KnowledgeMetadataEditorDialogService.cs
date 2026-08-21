using System.Windows;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>Creates and displays the modal Knowledge metadata editor.</summary>
public sealed class KnowledgeMetadataEditorDialogService : IKnowledgeMetadataEditorDialogService
{
    private readonly IDesktopInteractionService _desktopInteraction;

    /// <summary>Initializes the metadata dialog service.</summary>
    /// <param name="desktopInteraction">The desktop service used for discard confirmation.</param>
    public KnowledgeMetadataEditorDialogService(IDesktopInteractionService desktopInteraction) =>
        _desktopInteraction = desktopInteraction;

    /// <inheritdoc />
    public KnowledgeMetadataEditorResult? Show(KnowledgeMetadataEditorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewModel = new KnowledgeMetadataEditorViewModel();
        viewModel.Initialize(request);
        var window = new KnowledgeMetadataEditorWindow
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

        void OnCloseRequested(object? sender, EventArgs eventArgs)
        {
            closingByCommand = true;
            window.Close();
        }

        void OnClosing(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
        {
            if (closingByCommand || !viewModel.HasChanges)
            {
                return;
            }

            if (_desktopInteraction.Confirm(
                    "Discard metadata changes",
                    "The metadata editor contains unapplied changes. Discard them?"))
            {
                closingByCommand = true;
                return;
            }

            eventArgs.Cancel = true;
        }
    }
}
