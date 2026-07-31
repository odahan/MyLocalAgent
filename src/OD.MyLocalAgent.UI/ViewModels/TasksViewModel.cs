using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions ReportJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };

    private readonly ITaskStore _taskStore;
    private readonly ITaskSchedulerService _scheduler;
    private readonly IRunnerService _runnerService;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IDesktopInteractionService _desktopInteraction;
    private readonly IMessenger _messenger;

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
    private string _statusText = "Task data has not been loaded.";

    /// <summary>
    /// Initializes a new instance of the <see cref="TasksViewModel"/> class.
    /// </summary>
    public TasksViewModel(
        ITaskStore taskStore,
        ITaskSchedulerService scheduler,
        IRunnerService runnerService,
        IIncidentReporter incidentReporter,
        IDesktopInteractionService desktopInteraction,
        IMessenger messenger)
        : base(messenger)
    {
        _taskStore = taskStore;
        _scheduler = scheduler;
        _runnerService = runnerService;
        _incidentReporter = incidentReporter;
        _desktopInteraction = desktopInteraction;
        _messenger = messenger;
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

    /// <summary>
    /// Gets the current Runner diagnostic panel height.
    /// </summary>
    public double RunnerOutputHeight => IsLargeRunnerOutput ? 620 : 190;

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
            var reports = await ReadLatestReportsAsync(cancellationToken)
                .ConfigureAwait(true);
            var rows = new List<TaskListItem>(persistedTasks.Count);
            foreach (var task in persistedTasks)
            {
                var nextRun = task.Enabled
                    ? await _scheduler
                        .GetNextRunAsync(task.Id, cancellationToken)
                        .ConfigureAwait(true)
                    : null;
                reports.TryGetValue(task.Id, out var report);
                rows.Add(
                    new TaskListItem(
                        task.Id,
                        task.Name,
                        task.Schedule,
                        nextRun?.ToLocalTime().ToString(
                            "g",
                            CultureInfo.InvariantCulture) ?? "—",
                        report?.StartedUtc.ToLocalTime().ToString(
                            "g",
                            CultureInfo.InvariantCulture) ?? "Never",
                        report?.Status ?? "—",
                        task.Enabled,
                        _scheduler.IsRegistered(task.Id)));
            }

            var selectedId = SelectedTask?.Id;
            Tasks.Clear();
            foreach (var row in rows.OrderBy(static item => item.Name))
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
            StatusText = Tasks.Count == 0
                ? "No tasks have been created."
                : $"{Tasks.Count} task(s) loaded.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
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
            await _taskStore
                .SaveAsync(updated, cancellationToken)
                .ConfigureAwait(true);
            await SynchronizeRegistrationAsync(updated, cancellationToken)
                .ConfigureAwait(true);
            _messenger.Send(new TaskSavedMessage(updated.Id));
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

        IsRunning = true;
        try
        {
            var runnerPath = _runnerService.RunnerPath;
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

    partial void OnIsLargeRunnerOutputChanged(bool value) =>
        OnPropertyChanged(nameof(RunnerOutputHeight));

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

    private static async Task<IReadOnlyDictionary<string, ReportIndexLine>>
        ReadLatestReportsAsync(CancellationToken cancellationToken)
    {
        var reports = new Dictionary<string, ReportIndexLine>(
            StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(
            LocalAppPaths.ApplicationDataDirectory,
            "reports",
            "index.jsonl");
        if (!File.Exists(path))
        {
            return reports;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        while (await reader
                   .ReadLineAsync(cancellationToken)
                   .ConfigureAwait(true) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var report = JsonSerializer.Deserialize<ReportIndexLine>(
                    line,
                    ReportJsonOptions);
                if (report is not null)
                {
                    reports[report.TaskId] = report;
                }
            }
            catch (JsonException)
            {
            }
        }

        return reports;
    }

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

    private sealed record ReportIndexLine(
        string TaskId,
        DateTimeOffset StartedUtc,
        long DurationMs,
        string Status,
        string ReportPath);
}
