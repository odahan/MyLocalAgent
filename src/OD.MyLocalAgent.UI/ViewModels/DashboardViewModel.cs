using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents task, schedule, execution, and provider-health summaries.
/// </summary>
public sealed partial class DashboardViewModel :
    ObservableRecipient,
    IRecipient<TaskExecutionCompletedMessage>,
    IRecipient<TaskSavedMessage>,
    IActivatablePage
{
    private static readonly JsonSerializerOptions ReportJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };

    private readonly ITaskStore _taskStore;
    private readonly ITaskSchedulerService _scheduler;
    private readonly ILlmClientFactory _llmClientFactory;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IDesktopInteractionService _desktopInteraction;
    private readonly IMessenger _messenger;
    private readonly IOptionsMonitor<FileSystemOptions> _fileSystemOptions;

    [ObservableProperty]
    private int _taskCount;

    [ObservableProperty]
    private int _enabledTaskCount;

    [ObservableProperty]
    private string _providerName = "Not checked";

    [ObservableProperty]
    private string _providerHealthText = "Checking provider…";

    [ObservableProperty]
    private string _providerHealthState = "Checking";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardViewModel"/> class.
    /// </summary>
    public DashboardViewModel(
        ITaskStore taskStore,
        ITaskSchedulerService scheduler,
        ILlmClientFactory llmClientFactory,
        IIncidentReporter incidentReporter,
        IDesktopInteractionService desktopInteraction,
        IMessenger messenger,
        IOptionsMonitor<FileSystemOptions> fileSystemOptions)
        : base(messenger)
    {
        _taskStore = taskStore;
        _scheduler = scheduler;
        _llmClientFactory = llmClientFactory;
        _incidentReporter = incidentReporter;
        _desktopInteraction = desktopInteraction;
        _messenger = messenger;
        _fileSystemOptions = fileSystemOptions;
        _taskStore.TasksChanged += OnTasksChanged;
        IsActive = true;
    }

    /// <summary>
    /// Gets the five closest scheduled executions.
    /// </summary>
    public ObservableCollection<NextRunItem> NextRuns { get; } = [];

    /// <summary>
    /// Gets the ten most recent execution-index entries.
    /// </summary>
    public ObservableCollection<ExecutionHistoryItem> RecentExecutions { get; } = [];

    /// <inheritdoc />
    public void Activate()
    {
        if (!RefreshCommand.IsRunning)
        {
            RefreshCommand.Execute(null);
        }
    }

    /// <inheritdoc />
    public void Receive(TaskExecutionCompletedMessage message) =>
        QueueRefresh();

    /// <inheritdoc />
    public void Receive(TaskSavedMessage message) =>
        QueueRefresh();

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var tasks = await _taskStore
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(true);
            TaskCount = tasks.Count;
            EnabledTaskCount = tasks.Count(static task => task.Enabled);
            await RefreshNextRunsAsync(tasks, cancellationToken).ConfigureAwait(true);
            await RefreshExecutionsAsync(cancellationToken).ConfigureAwait(true);
            await RefreshHealthCoreAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await ReportAsync(exception, "Dashboard refresh").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshHealthCoreAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ProviderHealthState = "Unhealthy";
            ProviderHealthText = exception.Message;
            await ReportAsync(exception, "Dashboard provider health")
                .ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ResyncSchedulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _scheduler
                .ResyncAllAsync(cancellationToken)
                .ConfigureAwait(true);
            var message = result.Failures.Count == 0
                ? $"Schedules synchronized: {result.Registered} registered, {result.Unregistered} removed."
                : $"Schedule synchronization completed with {result.Failures.Count} failure(s).";
            _messenger.Send(new SnackbarNotificationMessage(message));
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await ReportAsync(exception, "Dashboard schedule resynchronization")
                .ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void NewTask()
    {
        _messenger.Send(new EditTaskMessage(null));
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        _desktopInteraction.OpenFolder(LocalAppPaths.LogsDirectory);
    }

    private async Task RefreshNextRunsAsync(
        IReadOnlyList<ScheduledTask> tasks,
        CancellationToken cancellationToken)
    {
        var upcoming = new List<(ScheduledTask Task, DateTimeOffset When)>();
        foreach (var task in tasks.Where(static task => task.Enabled))
        {
            var nextRun = await _scheduler
                .GetNextRunAsync(task.Id, cancellationToken)
                .ConfigureAwait(true);
            if (nextRun is not null)
            {
                upcoming.Add((task, nextRun.Value));
            }
        }

        NextRuns.Clear();
        foreach (var item in upcoming
                     .OrderBy(static item => item.When)
                     .Take(5))
        {
            NextRuns.Add(
                new NextRunItem(
                    item.Task.Id,
                    item.Task.Name,
                    item.When.ToLocalTime().ToString(
                        "ddd, dd MMM yyyy HH:mm",
                        CultureInfo.InvariantCulture)));
        }
    }

    private async Task RefreshExecutionsAsync(
        CancellationToken cancellationToken)
    {
        var reportIndexPath = Path.Combine(
            _fileSystemOptions.CurrentValue.RootPath,
            "reports",
            "index.jsonl");
        var recent = new Queue<ReportIndexLine>(10);
        if (File.Exists(reportIndexPath))
        {
            await using var stream = new FileStream(
                reportIndexPath,
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
                    var entry = JsonSerializer.Deserialize<ReportIndexLine>(
                        line,
                        ReportJsonOptions);
                    if (entry is null)
                    {
                        continue;
                    }

                    if (recent.Count == 10)
                    {
                        _ = recent.Dequeue();
                    }

                    recent.Enqueue(entry);
                }
                catch (JsonException)
                {
                }
            }
        }

        RecentExecutions.Clear();
        foreach (var entry in recent.Reverse())
        {
            RecentExecutions.Add(
                new ExecutionHistoryItem(
                    entry.TaskId,
                    entry.StartedUtc.ToLocalTime().ToString(
                        "g",
                        CultureInfo.InvariantCulture),
                    TimeSpan.FromMilliseconds(entry.DurationMs).ToString(
                        @"hh\:mm\:ss",
                        CultureInfo.InvariantCulture),
                    entry.Status,
                    entry.ReportPath));
        }
    }

    private async Task RefreshHealthCoreAsync(
        CancellationToken cancellationToken)
    {
        ProviderHealthState = "Checking";
        ProviderHealthText = "Checking provider…";
        var provider = _llmClientFactory.Create();
        ProviderName = provider.ProviderName;
        var health = await provider
            .CheckHealthAsync(cancellationToken)
            .ConfigureAwait(true);
        var healthy = health.IsReachable && health.ModelAvailable;
        ProviderHealthState = healthy ? "Healthy" : "Unhealthy";
        ProviderHealthText = healthy
            ? $"Healthy · {health.LatencyMs} ms"
            : health.Error ?? "Provider or configured model is unavailable.";
    }

    private void OnTasksChanged(
        object? sender,
        TaskStoreChangedEventArgs e)
    {
        QueueRefresh();
    }

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
        ProviderHealthState = "Unhealthy";
        ProviderHealthText = exception.Message;
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(true);
    }

    private sealed record ReportIndexLine(
        string TaskId,
        DateTimeOffset StartedUtc,
        double DurationMs,
        string Status,
        string ReportPath);
}
