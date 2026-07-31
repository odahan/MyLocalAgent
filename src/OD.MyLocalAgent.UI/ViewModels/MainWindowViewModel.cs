using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MahApps.Metro.IconPacks;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using OD.MyLocalAgent.UI.Messages;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Coordinates shell navigation and non-blocking notifications.
/// </summary>
public sealed partial class MainWindowViewModel :
    ObservableRecipient,
    IRecipient<EditPromptMessage>,
    IRecipient<EditTaskMessage>,
    IRecipient<IncidentNotificationMessage>,
    IRecipient<NavigateMessage>,
    IRecipient<SnackbarNotificationMessage>
{
    private readonly Func<PageRoute, object> _pageResolver;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private NavigationItemViewModel? _selectedNavigationItem;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    public MainWindowViewModel(
        IServiceProvider serviceProvider,
        IMessenger messenger,
        ISnackbarMessageQueue snackbarMessageQueue)
        : this(
            route => ResolvePage(serviceProvider, route),
            messenger,
            snackbarMessageQueue)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
    }

    internal MainWindowViewModel(
        Func<PageRoute, object> pageResolver,
        IMessenger messenger,
        ISnackbarMessageQueue snackbarMessageQueue)
        : base(messenger)
    {
        ArgumentNullException.ThrowIfNull(pageResolver);
        _pageResolver = pageResolver;
        SnackbarMessageQueue = snackbarMessageQueue;
        NavigationItems =
        [
            new(
                "Dashboard",
                PackIconMaterialKind.ViewDashboardOutline,
                PageRoute.Dashboard),
            new(
                "Tasks",
                PackIconMaterialKind.CalendarClock,
                PageRoute.Tasks),
            new(
                "Logs",
                PackIconMaterialKind.TextBoxSearchOutline,
                PageRoute.Logs),
            new(
                "Tools",
                PackIconMaterialKind.Tools,
                PageRoute.Tools),
            new(
                "Tool tester",
                PackIconMaterialKind.FlaskOutline,
                PageRoute.ToolTester),
            new(
                "AI playground",
                PackIconMaterialKind.RobotOutline,
                PageRoute.AIPlayground),
            new(
                "Trust filter",
                PackIconMaterialKind.ShieldCheckOutline,
                PageRoute.TrustFilter),
            new(
                "Settings",
                PackIconMaterialKind.CogOutline,
                PageRoute.Settings),
        ];
        IsActive = true;
        SelectedNavigationItem = NavigationItems[0];
    }

    /// <summary>
    /// Gets the shell navigation destinations.
    /// </summary>
    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    /// <summary>
    /// Gets the snackbar queue displayed by the shell.
    /// </summary>
    public ISnackbarMessageQueue SnackbarMessageQueue { get; }

    /// <inheritdoc />
    public void Receive(IncidentNotificationMessage message)
    {
        Enqueue(
            $"An incident was recorded. Correlation id: {message.CorrelationId}");
    }

    /// <inheritdoc />
    public void Receive(EditTaskMessage message)
    {
        SelectTasksNavigationItem();
        var viewModel = (TaskEditorViewModel)_pageResolver(PageRoute.TaskEditor);
        viewModel.Open(message.TaskId);
        CurrentPage = viewModel;
    }

    /// <inheritdoc />
    public void Receive(EditPromptMessage message)
    {
        SelectTasksNavigationItem();
        var viewModel = (PromptEditorViewModel)_pageResolver(PageRoute.PromptEditor);
        viewModel.Open(
            message.TaskId,
            message.GenerationOptions,
            message.UseGenerationOptionsSnapshot);
        CurrentPage = viewModel;
    }

    /// <inheritdoc />
    public void Receive(NavigateMessage message)
    {
        var item = NavigationItems.First(candidate => candidate.Route == message.Route);
        if (!ReferenceEquals(item, SelectedNavigationItem))
        {
            SelectedNavigationItem = item;
            return;
        }

        NavigateTo(item.Route);
    }

    /// <inheritdoc />
    public void Receive(SnackbarNotificationMessage message)
    {
        Enqueue(message.Message);
    }

    [RelayCommand]
    private void MinimizeWindow(Window window)
    {
        SystemCommands.MinimizeWindow(window);
    }

    [RelayCommand]
    private void MaximizeWindow(Window window)
    {
        SystemCommands.MaximizeWindow(window);
    }

    [RelayCommand]
    private void RestoreWindow(Window window)
    {
        SystemCommands.RestoreWindow(window);
    }

    [RelayCommand]
    private void CloseWindow(Window window)
    {
        SystemCommands.CloseWindow(window);
    }

    partial void OnSelectedNavigationItemChanged(
        NavigationItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        NavigateTo(value.Route);
    }

    private void NavigateTo(PageRoute route)
    {
        if (CurrentPage is IDeactivatablePage deactivatablePage)
        {
            deactivatablePage.Deactivate();
        }

        CurrentPage = _pageResolver(route);
        if (CurrentPage is IActivatablePage activatablePage)
        {
            activatablePage.Activate();
        }
    }

    private void Enqueue(string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            SnackbarMessageQueue.Enqueue(message, neverConsiderToBeDuplicate: true);
            return;
        }

        _ = dispatcher.BeginInvoke(
            () => SnackbarMessageQueue.Enqueue(
                message,
                neverConsiderToBeDuplicate: true));
    }

    private void SelectTasksNavigationItem()
    {
        var tasksItem = NavigationItems.First(
            static candidate => candidate.Route == PageRoute.Tasks);
        if (!ReferenceEquals(tasksItem, SelectedNavigationItem))
        {
            SelectedNavigationItem = tasksItem;
        }
    }

    private static object ResolvePage(
        IServiceProvider serviceProvider,
        PageRoute route)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return route switch
        {
            PageRoute.Dashboard =>
                serviceProvider.GetRequiredService<DashboardViewModel>(),
            PageRoute.Tasks =>
                serviceProvider.GetRequiredService<TasksViewModel>(),
            PageRoute.TaskEditor =>
                serviceProvider.GetRequiredService<TaskEditorViewModel>(),
            PageRoute.PromptEditor =>
                serviceProvider.GetRequiredService<PromptEditorViewModel>(),
            PageRoute.Logs =>
                serviceProvider.GetRequiredService<LogsViewModel>(),
            PageRoute.Tools =>
                serviceProvider.GetRequiredService<ToolsViewModel>(),
            PageRoute.ToolTester =>
                serviceProvider.GetRequiredService<ToolTesterViewModel>(),
            PageRoute.AIPlayground =>
                serviceProvider.GetRequiredService<AIPlaygroundViewModel>(),
            PageRoute.TrustFilter =>
                serviceProvider.GetRequiredService<TrustFilterViewModel>(),
            PageRoute.Settings =>
                serviceProvider.GetRequiredService<SettingsViewModel>(),
            _ => throw new InvalidOperationException(
                $"Unsupported navigation route '{route}'."),
        };
    }
}
