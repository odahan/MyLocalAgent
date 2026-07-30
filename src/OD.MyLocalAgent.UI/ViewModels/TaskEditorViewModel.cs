using System.Globalization;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Edits and validates scheduled task definitions.
/// </summary>
public sealed partial class TaskEditorViewModel : ObservableObject
{
    private readonly ITaskStore _taskStore;
    private readonly ITaskSchedulerService _scheduler;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IDesktopInteractionService _desktopInteraction;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _timeProvider;
    private string? _requestedTaskId;
    private string? _originalTaskId;

    [ObservableProperty]
    private string _editorTitle = "New task";

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _schedule = "0 7 * * *";

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private WorkflowErrorPolicy _selectedErrorPolicy =
        WorkflowErrorPolicy.StopOnError;

    [ObservableProperty]
    private bool _useGmail;

    [ObservableProperty]
    private bool _useCalendar;

    [ObservableProperty]
    private bool _useDropbox;

    [ObservableProperty]
    private bool _useFileSystem;

    [ObservableProperty]
    private bool _useHttp;

    [ObservableProperty]
    private bool _useWeb;

    /// <summary>
    /// Gets the task-specific absolute authorized locations.
    /// </summary>
    public ObservableCollection<FileSystemAccessItem> AuthorizedLocations { get; } = [];

    /// <summary>
    /// Gets a value indicating whether local-path authorization is relevant.
    /// </summary>
    public bool ShowAuthorizedLocations => UseFileSystem || UseDropbox;

    [ObservableProperty]
    private string _workflowText = "[]";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPromptCommand))]
    private bool _isIdReadOnly;

    [ObservableProperty]
    private bool _isCronValid;

    [ObservableProperty]
    private string _cronDescription = "Daily at 07:00";

    [ObservableProperty]
    private string _cronPreviewText = string.Empty;

    [ObservableProperty]
    private CronSchedulePreset? _selectedCronPreset;

    [ObservableProperty]
    private string _validationText = "Complete the task definition, then save.";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskEditorViewModel"/> class.
    /// </summary>
    public TaskEditorViewModel(
        ITaskStore taskStore,
        ITaskSchedulerService scheduler,
        IIncidentReporter incidentReporter,
        IDesktopInteractionService desktopInteraction,
        IMessenger messenger,
        TimeProvider timeProvider)
    {
        _taskStore = taskStore;
        _scheduler = scheduler;
        _incidentReporter = incidentReporter;
        _desktopInteraction = desktopInteraction;
        _messenger = messenger;
        _timeProvider = timeProvider;
        ErrorPolicies = Enum.GetValues<WorkflowErrorPolicy>();
        UpdateCronPreview();
    }

    /// <summary>
    /// Gets the supported task-level workflow error policies.
    /// </summary>
    public IReadOnlyList<WorkflowErrorPolicy> ErrorPolicies { get; }

    /// <summary>
    /// Gets the common scheduler patterns that can be applied without writing cron syntax.
    /// </summary>
    public IReadOnlyList<CronSchedulePreset> CronPresets { get; } =
    [
        new("Every 5 minutes", "*/5 * * * *"),
        new("Every 15 minutes", "*/15 * * * *"),
        new("Every hour", "0 * * * *"),
        new("Every day at 07:00", "0 7 * * *"),
        new("Every day at 09:00", "0 9 * * *"),
        new("Weekdays at 09:00", "0 9 * * 1-5"),
        new("Every Monday at 09:00", "0 9 * * 1"),
        new("First day of each month at 09:00", "0 9 1 * *"),
    ];

    /// <summary>
    /// Opens the editor for a new or existing task.
    /// </summary>
    /// <param name="taskId">The existing task identifier, or <see langword="null"/>.</param>
    public void Open(string? taskId)
    {
        _requestedTaskId = taskId;
        if (!LoadCommand.IsRunning)
        {
            LoadCommand.Execute(null);
        }
    }

    partial void OnScheduleChanged(string value) =>
        UpdateCronPreview();

    partial void OnSelectedCronPresetChanged(CronSchedulePreset? value)
    {
        if (value is not null &&
            !string.Equals(
                Schedule,
                value.Expression,
                StringComparison.Ordinal))
        {
            Schedule = value.Expression;
        }
    }

    partial void OnUseFileSystemChanged(bool value) =>
        OnPropertyChanged(nameof(ShowAuthorizedLocations));

    partial void OnUseDropboxChanged(bool value) =>
        OnPropertyChanged(nameof(ShowAuthorizedLocations));

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var task = string.IsNullOrWhiteSpace(_requestedTaskId)
                ? null
                : await _taskStore
                    .GetAsync(_requestedTaskId, cancellationToken)
                    .ConfigureAwait(true);
            if (_requestedTaskId is not null && task is null)
            {
                throw new TaskValidationException(
                    $"Task '{_requestedTaskId}' no longer exists.");
            }

            _originalTaskId = task?.Id;
            EditorTitle = task is null ? "New task" : $"Edit task · {task.Name}";
            Id = task?.Id ?? string.Empty;
            Name = task?.Name ?? string.Empty;
            Schedule = task?.Schedule ?? "0 7 * * *";
            Prompt = task?.Prompt ?? string.Empty;
            IsEnabled = task?.Enabled ?? true;
            SelectedErrorPolicy =
                task?.ErrorPolicy ?? WorkflowErrorPolicy.StopOnError;
            UseGmail = task?.Tools.Contains(
                ToolGroups.Gmail,
                StringComparer.OrdinalIgnoreCase) == true;
            UseCalendar = task?.Tools.Contains(
                ToolGroups.Calendar,
                StringComparer.OrdinalIgnoreCase) == true;
            UseDropbox = task?.Tools.Contains(
                ToolGroups.Dropbox,
                StringComparer.OrdinalIgnoreCase) == true;
            UseFileSystem = task?.Tools.Contains(
                ToolGroups.FileSystem,
                StringComparer.OrdinalIgnoreCase) == true;
            UseHttp = task?.Tools.Contains(
                ToolGroups.Http,
                StringComparer.OrdinalIgnoreCase) == true;
            UseWeb = task?.Tools.Contains(
                ToolGroups.Web,
                StringComparer.OrdinalIgnoreCase) == true;
            AuthorizedLocations.Clear();
            foreach (var rule in task?.FileSystemAccess ?? [])
            {
                AuthorizedLocations.Add(
                    new FileSystemAccessItem(rule.Path, rule.Access));
            }

            WorkflowText = task is null
                ? "[]"
                : JsonSerializer.Serialize(
                    task.Workflow,
                    ScheduledTaskSerializer.Options);
            IsIdReadOnly = task is not null;
            ValidationText = task is null
                ? "Enter an id, name, prompt or workflow, and schedule."
                : "Task loaded. The id is immutable after creation.";
            UpdateCronPreview();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (TaskValidationException exception)
        {
            ValidationText = exception.Message;
        }
        catch (Exception exception)
        {
            ValidationText = exception.Message;
            await ReportAsync(exception, "Task editor load").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var workflow = DeserializeWorkflow(WorkflowText);
            var task = new ScheduledTask
            {
                Id = Id.Trim(),
                Name = Name.Trim(),
                Schedule = Schedule.Trim(),
                Prompt = Prompt.Trim(),
                Tools = GetSelectedToolGroups(),
                FileSystemAccess = AuthorizedLocations
                    .Select(static item => item.ToRule())
                    .ToArray(),
                Workflow = workflow,
                Enabled = IsEnabled,
                ErrorPolicy = SelectedErrorPolicy,
            };
            ScheduledTaskValidator.Validate(task);

            if (_originalTaskId is not null &&
                !string.Equals(
                    _originalTaskId,
                    task.Id,
                    StringComparison.Ordinal))
            {
                throw new TaskValidationException(
                    "Task id is immutable after creation.");
            }

            if (_originalTaskId is null &&
                await _taskStore
                    .GetAsync(task.Id, cancellationToken)
                    .ConfigureAwait(true) is not null)
            {
                throw new TaskValidationException(
                    $"Task id '{task.Id}' already exists.");
            }

            await _taskStore
                .SaveAsync(task, cancellationToken)
                .ConfigureAwait(true);
            await SynchronizeRegistrationAsync(task, cancellationToken)
                .ConfigureAwait(true);
            _originalTaskId = task.Id;
            Id = task.Id;
            IsIdReadOnly = true;
            EditorTitle = $"Edit task · {task.Name}";
            ValidationText = "Task saved and scheduler registration synchronized.";
            _messenger.Send(new TaskSavedMessage(task.Id));
            _messenger.Send(
                new SnackbarNotificationMessage(
                    $"Task '{task.Name}' saved."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
            when (exception is TaskValidationException or
                WorkflowValidationException or
                JsonException)
        {
            ValidationText = AddAliasSuggestion(exception.Message);
        }
        catch (Exception exception)
        {
            ValidationText = exception.Message;
            await ReportAsync(exception, "Task editor save").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenPrompt))]
    private void OpenPrompt()
    {
        if (_originalTaskId is not null)
        {
            _messenger.Send(new EditPromptMessage(_originalTaskId));
        }
    }

    [RelayCommand]
    private void BackToTasks() =>
        _messenger.Send(new NavigateMessage(PageRoute.Tasks));

    [RelayCommand]
    private void AddAuthorizedLocation()
    {
        var selectedPath = _desktopInteraction.SelectFolder(
            "Select an authorized task location");
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        var normalizedPath = NormalizeDirectoryPath(selectedPath);
        if (AuthorizedLocations.Any(
                item => string.Equals(
                    item.Path,
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            ValidationText = $"Location '{normalizedPath}' is already authorized.";
            return;
        }

        var root = Path.GetPathRoot(normalizedPath);
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(normalizedPath),
                Path.TrimEndingDirectorySeparator(root ?? string.Empty),
                StringComparison.OrdinalIgnoreCase) &&
            !_desktopInteraction.Confirm(
                "Authorize an entire volume?",
                $"This grants the task access to the entire volume '{normalizedPath}' and all descendants. Continue?"))
        {
            return;
        }

        AuthorizedLocations.Add(
            new FileSystemAccessItem(
                normalizedPath,
                FileSystemAccessMode.ReadWrite));
        ValidationText =
            $"Location '{normalizedPath}' added with read and write access.";
    }

    [RelayCommand]
    private void RemoveAuthorizedLocation(FileSystemAccessItem? item)
    {
        if (item is not null)
        {
            AuthorizedLocations.Remove(item);
        }
    }

    private bool CanOpenPrompt() => IsIdReadOnly;

    private void UpdateCronPreview()
    {
        SelectedCronPreset = CronPresets.FirstOrDefault(
            preset => string.Equals(
                preset.Expression,
                Schedule.Trim(),
                StringComparison.Ordinal));
        try
        {
            var cron = Schedule.Trim();
            CronDescription = CronSchedule.DescribeCommonPattern(cron);
            var occurrences = CronSchedule.NextOccurrences(
                cron,
                _timeProvider.GetLocalNow(),
                TimeZoneInfo.Local,
                3);
            CronPreviewText = occurrences.Count == 0
                ? "No future occurrence."
                : string.Join(
                    Environment.NewLine,
                    occurrences.Select(
                        occurrence =>
                            "• " + occurrence.ToLocalTime().ToString(
                                "ddd, dd MMM yyyy HH:mm",
                                CultureInfo.InvariantCulture)));
            IsCronValid = true;
        }
        catch (Exception exception)
            when (exception is ArgumentException or Cronos.CronFormatException)
        {
            IsCronValid = false;
            CronDescription = "Invalid schedule";
            CronPreviewText = "Enter a valid five-field cron expression.";
        }
    }

    private IReadOnlyList<string> GetSelectedToolGroups()
    {
        var groups = new List<string>(6);
        if (UseGmail)
        {
            groups.Add(ToolGroups.Gmail);
        }

        if (UseCalendar)
        {
            groups.Add(ToolGroups.Calendar);
        }

        if (UseDropbox)
        {
            groups.Add(ToolGroups.Dropbox);
        }

        if (UseFileSystem)
        {
            groups.Add(ToolGroups.FileSystem);
        }

        if (UseHttp)
        {
            groups.Add(ToolGroups.Http);
        }

        if (UseWeb)
        {
            groups.Add(ToolGroups.Web);
        }

        return groups;
    }

    private static IReadOnlyList<WorkflowStepDefinition> DeserializeWorkflow(
        string workflowText)
    {
        if (string.IsNullOrWhiteSpace(workflowText))
        {
            return [];
        }

        return JsonSerializer.Deserialize<WorkflowStepDefinition[]>(
                workflowText,
                ScheduledTaskSerializer.Options)
            ?? throw new WorkflowValidationException(
                "Workflow JSON must contain an array.");
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(path.Trim()));
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.TrimEndingDirectorySeparator(fullPath);
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

    private static string AddAliasSuggestion(string message)
    {
        const string marker = "unknown alias '";
        var start = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return message;
        }

        start += marker.Length;
        var end = message.IndexOf('\'', start);
        if (end <= start)
        {
            return message;
        }

        var unknownAlias = message[start..end];
        var suggestion = ToolMap.Definitions
            .Select(
                definition => new
                {
                    definition.Alias,
                    Distance = LevenshteinDistance(
                        unknownAlias,
                        definition.Alias),
                })
            .OrderBy(static item => item.Distance)
            .FirstOrDefault();
        return suggestion is not null && suggestion.Distance <= 3
            ? $"{message} Did you mean '{suggestion.Alias}'?"
            : message;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost =
                    char.ToUpperInvariant(left[leftIndex - 1]) ==
                    char.ToUpperInvariant(right[rightIndex - 1])
                        ? 0
                        : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(
                        current[rightIndex - 1] + 1,
                        previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private async Task ReportAsync(Exception exception, string source)
    {
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(true);
    }
}
