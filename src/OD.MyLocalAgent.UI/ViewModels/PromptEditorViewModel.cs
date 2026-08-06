using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.Ollama;
using Microsoft.Extensions.Options;
using System.Globalization;

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
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;
    private readonly TimeProvider _timeProvider;
    private string? _requestedTaskId;
    private ScheduledTask? _task;
    private GenerationOptions? _generationOptionsSnapshot;
    private bool _useGenerationOptionsSnapshot;

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

    [ObservableProperty]
    private string _generationPolicyText =
        "Generation policy has not been resolved.";

    [ObservableProperty]
    private string _placeholderWarningText = string.Empty;

    [ObservableProperty]
    private bool _hasPlaceholderWarnings;

    /// <summary>
    /// Gets concise authoring help derived from the shared prompt-placeholder catalog.
    /// </summary>
    public string PlaceholderHelpText => AgentPromptPlaceholderCatalog.HelpText;

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptEditorViewModel"/> class.
    /// </summary>
    public PromptEditorViewModel(
        ITaskStore taskStore,
        ITaskSchedulerService scheduler,
        IMyLocalAgentService agentService,
        IIncidentReporter incidentReporter,
        IMessenger messenger,
        IOptionsMonitor<AIOptions>? aiOptions = null,
        TimeProvider? timeProvider = null)
    {
        _taskStore = taskStore;
        _scheduler = scheduler;
        _agentService = agentService;
        _incidentReporter = incidentReporter;
        _messenger = messenger;
        _aiOptions = aiOptions;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Opens the prompt editor for an existing task.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="generationOptions">
    /// The optional unsaved task-generation override snapshot.
    /// </param>
    /// <param name="useGenerationOptionsSnapshot">
    /// Whether prompt tests must use the supplied override snapshot.
    /// </param>
    public void Open(
        string taskId,
        GenerationOptions? generationOptions = null,
        bool useGenerationOptionsSnapshot = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        _requestedTaskId = taskId;
        _generationOptionsSnapshot = generationOptions;
        _useGenerationOptionsSnapshot = useGenerationOptionsSnapshot;
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
            ClearPlaceholderWarnings();
            UpdateGenerationPolicyText();
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
            var updated = _task with { Prompt = Prompt };
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
        ClearPlaceholderWarnings();
        TokenUsageText =
            "Token usage is not reported by the current streaming contract.";
        StatusText = "The agent is generating with the task tool whitelist.";
        try
        {
            var localNow = _timeProvider.GetLocalNow();
            var resolution = AgentPromptPlaceholderResolver.Resolve(
                Prompt,
                _task.Id,
                _task.Name,
                localNow);
            if (resolution.TotalUnresolvedCount > 0)
            {
                PlaceholderWarningText =
                    AgentPromptPlaceholderWarning.FormatSummary(resolution);
                HasPlaceholderWarnings = true;
            }

            var generationPolicy = ResolveGenerationPolicy();
            UpdateGenerationPolicyText(generationPolicy);
            await foreach (var chunk in _agentService
                               .RunPromptStreamingAsync(
                                   resolution.ResolvedPrompt,
                                   _task.Tools,
                                   ToolExecutionContext.FromTask(_task),
                                   generationPolicy,
                                   cancellationToken)
                               .ConfigureAwait(true))
            {
                Output += chunk;
                ChunkCount++;
            }

            StatusText = HasPlaceholderWarnings
                ? $"Prompt test completed with {ChunkCount} chunk(s); {resolution.TotalUnresolvedCount} placeholder occurrence(s) remained unresolved."
                : $"Prompt test completed with {ChunkCount} chunk(s).";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = HasPlaceholderWarnings
                ? $"Prompt test canceled after {ChunkCount} chunk(s); unresolved placeholder warnings remain."
                : $"Prompt test canceled after {ChunkCount} chunk(s).";
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
    private void BackToTasks()
    {
        if (_task is not null)
        {
            _messenger.Send(new EditTaskMessage(_task.Id));
            return;
        }

        _messenger.Send(new NavigateMessage(PageRoute.Tasks));
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

    private async Task ReportAsync(Exception exception, string source)
    {
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(true);
    }

    private EffectiveGenerationPolicy ResolveGenerationPolicy() =>
        GenerationPolicyResolver.Resolve(
            _aiOptions?.CurrentValue.GenerationDefaults ??
            new AIGenerationDefaults(),
            _useGenerationOptionsSnapshot
                ? _generationOptionsSnapshot
                : _task?.GenerationOptions);

    private void UpdateGenerationPolicyText(
        EffectiveGenerationPolicy? policy = null)
    {
        policy ??= ResolveGenerationPolicy();
        GenerationPolicyText =
            $"Temperature {policy.Temperature.ToString("G", CultureInfo.CurrentCulture)} ({policy.TemperatureSource}) · " +
            $"Top P {policy.TopP.ToString("G", CultureInfo.CurrentCulture)} ({policy.TopPSource}) · " +
            $"Context {policy.ContextWindowTokens.ToString("N0", CultureInfo.CurrentCulture)} tokens (Settings)";
    }

    partial void OnPromptChanged(string value)
    {
        _ = value;
        ClearPlaceholderWarnings();
    }

    private void ClearPlaceholderWarnings()
    {
        PlaceholderWarningText = string.Empty;
        HasPlaceholderWarnings = false;
    }
}
