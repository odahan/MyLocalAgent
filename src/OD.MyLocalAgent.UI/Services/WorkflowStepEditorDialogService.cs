using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>Creates and hosts the memory-only workflow step editor window.</summary>
public sealed class WorkflowStepEditorDialogService : IWorkflowStepEditorDialogService
{
    private readonly IServiceProvider _services;
    private readonly IDesktopInteractionService _desktopInteraction;

    /// <summary>Initializes the dialog service.</summary>
    public WorkflowStepEditorDialogService(IServiceProvider services, IDesktopInteractionService desktopInteraction)
    {
        _services = services;
        _desktopInteraction = desktopInteraction;
    }

    /// <inheritdoc />
    public WorkflowStepEditorResult? Show(WorkflowStepEditorRequest request)
    {
        var viewModel = ActivatorUtilities.CreateInstance<WorkflowStepEditorViewModel>(_services);
        viewModel.Initialize(request);
        var window = new WorkflowStepEditorWindow { DataContext = viewModel, Owner = Application.Current.MainWindow };
        using var lifetime = new CancellationTokenSource();
        var closingByCommand = false;
        var windowLoaded = false;
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.DecisionSettingsRequested += (_, _) =>
        {
            var settings = new Window
            {
                Title = "Decision models — Settings", Owner = window, Width = 1000, Height = 800,
                Content = new OD.MyLocalAgent.UI.Pages.SettingsPage { DataContext = _services.GetRequiredService<SettingsViewModel>() },
            };
            _ = settings.ShowDialog();
        };
        window.Loaded += async (_, _) =>
        {
            windowLoaded = true;
            await viewModel.LoadModelChoicesAsync(lifetime.Token).ConfigureAwait(true);
        };
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        window.Closed += (_, _) =>
        {
            lifetime.Cancel();
            viewModel.CancelDecisionTest();
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
        window.Closing += (_, eventArgs) =>
        {
            if (closingByCommand || !viewModel.HasEffectiveChanges || _desktopInteraction.Confirm("Discard workflow step changes", "Discard the changes made to this workflow step?"))
            {
                return;
            }
            eventArgs.Cancel = true;
        };
        _ = window.ShowDialog();
        return viewModel.Result;

        void OnCloseRequested(object? sender, EventArgs eventArgs)
        {
            if (!viewModel.IsApplyCloseRequested && viewModel.HasEffectiveChanges && !_desktopInteraction.Confirm("Discard workflow step changes", "Discard the changes made to this workflow step?"))
            {
                return;
            }
            closingByCommand = true;
            window.Close();
        }

        async void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
        {
            if (!windowLoaded ||
                eventArgs.PropertyName != nameof(WorkflowStepEditorViewModel.IsPromptSelected) ||
                !viewModel.IsPromptSelected ||
                viewModel.IsLoadingModels)
            {
                return;
            }

            await viewModel.LoadModelChoicesAsync(lifetime.Token).ConfigureAwait(true);
        }
    }
}
