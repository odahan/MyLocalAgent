using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Messages;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Edits and streams a selected task's agent prompt.
/// </summary>
public sealed partial class PromptEditorViewModel : ObservableObject
{
    private readonly ITaskStore _taskStore;
    private readonly ITaskSchedulerService _scheduler;
    private readonly IMyLocalAgentService _agentService;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IMessenger _messenger;
    private string? _requestedTaskId;
    private ScheduledTask? _task;

    [ObservableProperty]
    private string _taskId = string.Empty;

    [ObservableProperty]
    private string _taskName = string.Empty;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _output = "Test output will stream here.";

    [ObservableProperty]
    private int _chunkCount;

    [ObservableProperty]
    private string _tokenUsageText =
        "Token usage is not reported by the current streaming contract.";

    [ObservableProperty]
    private string _statusText = "Select Test the prompt to run the agent.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isStreaming;

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptEditorViewModel"/> class.
    /// </summary>
    public PromptEditorViewModel(
        ITaskStore taskStore,
        ITaskSchedulerService scheduler,
        IMyLocalAgentService agentService,
        IIncidentReporter incidentReporter,
        IMessenger messenger)
    {
        _taskStore = taskStore;
        _scheduler = scheduler;
        _agentService = agentService;
        _incidentReporter = incidentReporter;
        _messenger = messenger;
    }

    /// <summary>
    /// Opens the prompt editor for an existing task.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    public void Open(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        _requestedTaskId = taskId;
        if (!LoadCommand.IsRunning)
        {
            LoadCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            _task = await _taskStore
                .GetAsync(_requestedTaskId!, cancellationToken)
                .ConfigureAwait(true)
                ?? throw new TaskValidationException(
                    $"Task '{_requestedTaskId}' no longer exists.");
            TaskId = _task.Id;
            TaskName = _task.Name;
            Prompt = _task.Prompt;
            Output = "Test output will stream here.";
            ChunkCount = 0;
            StatusText = "Prompt loaded.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (TaskValidationException exception)
        {
            StatusText = exception.Message;
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
            await ReportAsync(exception, "Prompt editor load").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_task is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updated = _task with { Prompt = Prompt.Trim() };
            ScheduledTaskValidator.Validate(updated);
            await _taskStore
                .SaveAsync(updated, cancellationToken)
                .ConfigureAwait(true);
            await SynchronizeRegistrationAsync(updated, cancellationToken)
                .ConfigureAwait(true);
            _task = updated;
            StatusText = "Prompt saved and scheduler registration synchronized.";
            _messenger.Send(new TaskSavedMessage(updated.Id));
            _messenger.Send(
                new SnackbarNotificationMessage(
                    $"Prompt for '{updated.Name}' saved."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (TaskValidationException exception)
        {
            StatusText = exception.Message;
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
            await ReportAsync(exception, "Prompt editor save").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task TestPromptAsync(CancellationToken cancellationToken)
    {
        if (_task is null)
        {
            StatusText = "Load a task before testing its prompt.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Prompt))
        {
            StatusText = "Enter a prompt before testing.";
            return;
        }

        IsStreaming = true;
        Output = string.Empty;
        ChunkCount = 0;
        TokenUsageText =
            "Token usage is not reported by the current streaming contract.";
        StatusText = "The agent is generating with the task tool whitelist.";
        try
        {
            await foreach (var chunk in _agentService
                               .RunPromptStreamingAsync(
                                   Prompt,
                                   _task.Tools,
                                   cancellationToken)
                               .ConfigureAwait(true))
            {
                Output += chunk;
                ChunkCount++;
            }

            StatusText = $"Prompt test completed with {ChunkCount} chunk(s).";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = $"Prompt test canceled after {ChunkCount} chunk(s).";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
            await ReportAsync(exception, "Prompt editor test").ConfigureAwait(true);
        }
        finally
        {
            IsStreaming = false;
        }
    }

    [RelayCommand]
    private void BackToTasks() =>
        _messenger.Send(new NavigateMessage(PageRoute.Tasks));

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

    private async Task ReportAsync(Exception exception, string source)
    {
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(true);
    }
}
