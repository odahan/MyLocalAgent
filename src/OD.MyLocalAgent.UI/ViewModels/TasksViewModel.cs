using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Manages persisted tasks, scheduler registrations, and child-runner executions.
/// </summary>
public sealed partial class TasksViewModel :
    ObservableRecipient,
    IActivatablePage,
    IRecipient<TaskSavedMessage>
{
    private readonly ITaskStore _taskStore;
    private readonly ITaskSchedulerService _scheduler;
    private readonly IRunnerService _runnerService;
    private readonly IRunLogCatalog _runLogCatalog;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IDesktopInteractionService _desktopInteraction;
    private readonly IMessenger _messenger;
    private readonly WorkspacePaths _workspacePaths;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditPromptCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(RegisterOrUnregisterCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunNowCommand))]
    private TaskListItem? _selectedTask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditPromptCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(RegisterOrUnregisterCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunNowCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _runnerOutput = "Select an enabled task and choose Run now.";

    [ObservableProperty]
    private string _runContext = "No workflow context has been captured yet.";

    [ObservableProperty]
    private bool _isShowingRunContext;

    [ObservableProperty]
    private bool _isDebugMode;

    [ObservableProperty]
    private bool _isLargeRunnerOutput;

    [ObservableProperty]
    private bool _isRunnerAutoScrollEnabled = true;

    [ObservableProperty]
    private string _statusText = "Task data has not been loaded.";

    [ObservableProperty]
    private string _taskLoadErrorText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="TasksViewModel"/> class.
    /// </summary>
    public TasksViewModel(
        ITaskStore taskStore,
        ITaskSchedulerService scheduler,
        IRunnerService runnerService,
        IRunLogCatalog runLogCatalog,
        IIncidentReporter incidentReporter,
        IDesktopInteractionService desktopInteraction,
        IMessenger messenger,
        WorkspacePaths workspacePaths)
        : base(messenger)
    {
        _taskStore = taskStore;
        _scheduler = scheduler;
        _runnerService = runnerService;
        _runLogCatalog = runLogCatalog;
        _incidentReporter = incidentReporter;
        _desktopInteraction = desktopInteraction;
        _messenger = messenger;
        _workspacePaths = workspacePaths;
        _taskStore.TasksChanged += OnTasksChanged;
        IsActive = true;
    }

    /// <summary>
    /// Gets the task rows displayed by the management grid.
    /// </summary>
    public ObservableCollection<TaskListItem> Tasks { get; } = [];

    /// <summary>
    /// Gets the currently selected Runner diagnostic text.
    /// </summary>
    public string DisplayedRunText => IsShowingRunContext ? RunContext : RunnerOutput;

    /// <summary>
    /// Gets the current Runner diagnostic panel title.
    /// </summary>
    public string RunnerDisplayTitle => IsShowingRunContext
        ? "Workflow context"
        : "Runner output";

    /// <summary>
    /// Gets the context toggle button label.
    /// </summary>
    public string RunContextButtonText => IsShowingRunContext
        ? "Show runner output"
        : "Show workflow context";

    /// <inheritdoc />
    public void Activate()
    {
        if (!RefreshCommand.IsRunning)
        {
            RefreshCommand.Execute(null);
        }
    }

    /// <inheritdoc />
    public void Receive(TaskSavedMessage message) =>
        QueueRefresh();

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var persistedTasks = await _taskStore
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(true);
            var latestRuns = GetLatestRunsByTask();
            var rows = new List<TaskListItem>(persistedTasks.Count);
            foreach (var task in persistedTasks)
            {
                var nextRun = task.Enabled
                    ? await _scheduler
                        .GetNextRunAsync(task.Id, cancellationToken)
                        .ConfigureAwait(true)
                    : null;
                latestRuns.TryGetValue(task.Id, out var latestRun);
                rows.Add(
                    new TaskListItem(
                        task.Id,
                        task.Name,
                        task.Schedule,
                        nextRun?.ToLocalTime().ToString(
                            "g",
                            CultureInfo.InvariantCulture) ?? "—",
                        latestRun?.StartedUtc.ToLocalTime().ToString(
                            "g",
                            CultureInfo.InvariantCulture) ?? "Never",
                        FormatRunStatus(latestRun),
                        task.Enabled,
                        _scheduler.IsRegistered(task.Id)));
            }

            var selectedId = SelectedTask?.Id;
            Tasks.Clear();
            foreach (var row in rows
                         .OrderBy(static item => !item.IsEnabled)
                         .ThenBy(static item => item.Name))
            {
                Tasks.Add(row);
            }

            SelectedTask = selectedId is null
                ? Tasks.FirstOrDefault()
                : Tasks.FirstOrDefault(
                    item => string.Equals(
                        item.Id,
                        selectedId,
                        StringComparison.OrdinalIgnoreCase));
            TaskLoadErrorText = string.Empty;
            StatusText = Tasks.Count == 0
                ? "No tasks have been created."
                : $"{Tasks.Count} task(s) loaded.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TaskLoadErrorText =
                "No task files were deleted. One or more task files could not be loaded." +
                Environment.NewLine + Environment.NewLine +
                exception.Message +
                Environment.NewLine + Environment.NewLine +
                "Open the Tasks folder, move or fix the invalid JSON file, then choose Refresh.";
            StatusText = "Task loading failed. Review the error shown above the task list.";
            _messenger.Send(
                new SnackbarNotificationMessage(
                    "Tasks could not be loaded. The task files are still present."));
            await ReportAsync(exception, "Tasks refresh").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NewTask() =>
        _messenger.Send(new EditTaskMessage(null));

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void EditTask()
    {
        if (SelectedTask is not null)
        {
            _messenger.Send(new EditTaskMessage(SelectedTask.Id));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void EditPrompt()
    {
        if (SelectedTask is not null)
        {
            _messenger.Send(new EditPromptMessage(SelectedTask.Id));
        }
    }

    [RelayCommand]
    private void OpenTasksFolder() =>
        _desktopInteraction.OpenFolder(_workspacePaths.TasksDirectory);

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private async Task DeleteTaskAsync(CancellationToken cancellationToken)
    {
        var task = SelectedTask;
        if (task is null ||
            !_desktopInteraction.Confirm(
                "Delete task",
                $"Delete '{task.Name}' and remove its scheduler registration?"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _scheduler
                .UnregisterAsync(task.Id, cancellationToken)
                .ConfigureAwait(true);
            _ = await _taskStore
                .DeleteAsync(task.Id, cancellationToken)
                .ConfigureAwait(true);
            _messenger.Send(
                new SnackbarNotificationMessage(
                    $"Task '{task.Name}' was deleted."));
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
            await ReportAsync(exception, "Task deletion").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(
        TaskListItem? item,
        CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var task = await _taskStore
                .GetAsync(item.Id, cancellationToken)
                .ConfigureAwait(true);
            if (task is null)
            {
                StatusText = $"Task '{item.Id}' no longer exists.";
                return;
            }

            var updated = task with { Enabled = !task.Enabled };
            var persistedTask = await _taskStore
                .SaveAsync(updated, cancellationToken)
                .ConfigureAwait(true);
            await SynchronizeRegistrationAsync(persistedTask, cancellationToken)
                .ConfigureAwait(true);
            _messenger.Send(new TaskSavedMessage(persistedTask.Id));
            _messenger.Send(
                new SnackbarNotificationMessage(
                    updated.Enabled
                        ? $"Task '{updated.Name}' enabled."
                        : $"Task '{updated.Name}' disabled."));
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
            await ReportAsync(exception, "Task enabled-state update")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeRegistration))]
    private async Task RegisterOrUnregisterAsync(
        CancellationToken cancellationToken)
    {
        var item = SelectedTask;
        if (item is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (item.IsRegistered)
            {
                await _scheduler
                    .UnregisterAsync(item.Id, cancellationToken)
                    .ConfigureAwait(true);
            }
            else
            {
                var task = await _taskStore
                    .GetAsync(item.Id, cancellationToken)
                    .ConfigureAwait(true)
                    ?? throw new TaskValidationException(
                        $"Task '{item.Id}' no longer exists.");
                await _scheduler
                    .RegisterAsync(task, cancellationToken)
                    .ConfigureAwait(true);
            }

            _messenger.Send(
                new SnackbarNotificationMessage(
                    item.IsRegistered
                        ? $"Task '{item.Name}' unregistered."
                        : $"Task '{item.Name}' registered."));
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
            await ReportAsync(exception, "Task registration update")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(
        CanExecute = nameof(CanRunNow),
        IncludeCancelCommand = true)]
    private async Task RunNowAsync(CancellationToken cancellationToken)
    {
        var item = SelectedTask;
        if (item is null)
        {
            return;
        }

        string runnerPath;
        try
        {
            runnerPath = _runnerService.RunnerPath;
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
            await ReportAsync(exception, "Task Run now").ConfigureAwait(true);
            return;
        }

        IsRunning = true;
        try
        {
            RunnerOutput =
                $"Starting runner for '{item.Name}'…{Environment.NewLine}" +
                $"Executable: {runnerPath}{Environment.NewLine}{Environment.NewLine}";
            RunContext =
                $"Task: {item.Name} ({item.Id}){Environment.NewLine}" +
                "Waiting for the first successful workflow step…";
            IsShowingRunContext = false;
            StatusText = $"Running '{item.Name}' in the child runner.";
            var exitCode = await _runnerService
                .RunTaskAsync(
                    item.Id,
                    AppendRunnerOutput,
                    UpdateRunContext,
                    IsDebugMode,
                    cancellationToken)
                .ConfigureAwait(true);
            AppendRunnerOutput($"Runner exited with code {exitCode}.");
            StatusText = exitCode == 0
                ? $"Task '{item.Name}' completed successfully."
                : $"Task '{item.Name}' failed with runner exit code {exitCode}.";
            _messenger.Send(
                new TaskExecutionCompletedMessage(item.Id, exitCode));
            _messenger.Send(new SnackbarNotificationMessage(StatusText));
            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppendRunnerOutput("Execution canceled. The child process was terminated.");
            StatusText = $"Task '{item.Name}' was canceled.";
        }
        catch (Exception exception)
        {
            AppendRunnerOutput($"Runner error: {exception.Message}");
            StatusText = exception.Message;
            await ReportAsync(exception, "Task Run now").ConfigureAwait(true);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanUseSelection() =>
        SelectedTask is not null && !IsBusy;

    private bool CanChangeRegistration() =>
        SelectedTask is not null &&
        !IsBusy &&
        (SelectedTask.IsRegistered || SelectedTask.IsEnabled);

    private bool CanRunNow() =>
        SelectedTask is { IsEnabled: true } && !IsRunning;

    [RelayCommand]
    private void ToggleRunContext() =>
        IsShowingRunContext = !IsShowingRunContext;

    partial void OnRunnerOutputChanged(string value)
    {
        if (!IsShowingRunContext)
        {
            OnPropertyChanged(nameof(DisplayedRunText));
        }
    }

    partial void OnRunContextChanged(string value)
    {
        if (IsShowingRunContext)
        {
            OnPropertyChanged(nameof(DisplayedRunText));
        }
    }

    partial void OnIsShowingRunContextChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayedRunText));
        OnPropertyChanged(nameof(RunnerDisplayTitle));
        OnPropertyChanged(nameof(RunContextButtonText));
    }

    private void AppendRunnerOutput(string line)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RunnerOutput += line + Environment.NewLine;
            return;
        }

        _ = dispatcher.BeginInvoke(
            () => RunnerOutput += line + Environment.NewLine);
    }

    private void UpdateRunContext(string contextJson)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RunContext = contextJson;
            return;
        }

        _ = dispatcher.BeginInvoke(() => RunContext = contextJson);
    }

    private async Task SynchronizeRegistrationAsync(
        ScheduledTask task,
        CancellationToken cancellationToken)
    {
        if (task.Enabled)
        {
            await _scheduler
                .RegisterAsync(task, cancellationToken)
                .ConfigureAwait(true);
        }
        else
        {
            await _scheduler
                .UnregisterAsync(task.Id, cancellationToken)
                .ConfigureAwait(true);
        }
    }

    private IReadOnlyDictionary<string, RunLogRunInfo> GetLatestRunsByTask()
    {
        var latestRuns = new Dictionary<string, RunLogRunInfo>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var run in _runLogCatalog.GetRuns())
        {
            _ = latestRuns.TryAdd(run.TaskId, run);
        }

        return latestRuns;
    }

    private static string FormatRunStatus(RunLogRunInfo? run) =>
        run switch
        {
            null => "Never run",
            { EndedUtc: null } or { ExitCode: null } => "In progress",
            { ExitCode: 0 } => "Success",
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"Failed ({run.ExitCode})"),
        };

    private void OnTasksChanged(
        object? sender,
        TaskStoreChangedEventArgs e) =>
        QueueRefresh();

    private void QueueRefresh()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(
            () =>
            {
                if (!RefreshCommand.IsRunning)
                {
                    RefreshCommand.Execute(null);
                }
            });
    }

    private async Task ReportAsync(Exception exception, string source)
    {
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(true);
    }

}
