using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Creates one transient helper ViewModel and displays the shared modal window.
/// </summary>
public sealed class AIWorkflowHelperDialogService : IAIWorkflowHelperDialogService
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIWorkflowHelperDialogService"/> class.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    public AIWorkflowHelperDialogService(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public AIWorkflowHelperResult? Show(AIWorkflowHelperRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewModel = ActivatorUtilities.CreateInstance<AIWorkflowHelperViewModel>(_services);
        viewModel.Initialize(request);
        var window = new AIWorkflowHelperWindow
        {
            DataContext = viewModel,
            Owner = Application.Current.MainWindow,
        };
        viewModel.CloseRequested += OnCloseRequested;
        if (request.Mode == AIWorkflowHelperMode.Explain)
        {
            window.ContentRendered += OnContentRendered;
        }

        try
        {
            _ = window.ShowDialog();
            return viewModel.Result;
        }
        finally
        {
            viewModel.CloseRequested -= OnCloseRequested;
            window.ContentRendered -= OnContentRendered;
            viewModel.Shutdown();
        }

        void OnCloseRequested(object? sender, EventArgs e) => window.Close();

        void OnContentRendered(object? sender, EventArgs e)
        {
            window.ContentRendered -= OnContentRendered;
            viewModel.GenerateCommand.Execute(null);
        }
    }
}
