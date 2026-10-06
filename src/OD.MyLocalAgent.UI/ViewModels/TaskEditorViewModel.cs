using System.Globalization;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.ComponentModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Edits and validates scheduled task definitions.
/// </summary>
public sealed partial class TaskEditorViewModel :
    ObservableObject,
    IDeactivatablePage,
    IAsyncNavigationGuard
{
    private static readonly HashSet<string> EditablePropertyNames =
    [
        nameof(Id),
        nameof(Name),
        nameof(Schedule),
        nameof(Prompt),
        nameof(UseGlobalSystemPrompt),
        nameof(UseSettingsModel),
        nameof(ModelOverride),
        nameof(IsEnabled),
        nameof(SelectedErrorPolicy),
        nameof(WorkflowText),
        nameof(UseGlobalTemperature),
        nameof(TemperatureOverrideText),
        nameof(UseGlobalTopP),
        nameof(UseGlobalContextWindow),
        nameof(ContextWindowOverrideText),
        nameof(TopPOverrideText),
        nameof(UseGlobalThinking),
        nameof(ReasoningEnabled),
        nameof(ReasoningEffortText),
        nameof(SqliteGuardrailsEnabled),
        nameof(KnowledgeTagsText),
    ];

    private readonly ITaskStore _taskStore;
    private readonly ITaskSchedulerService _scheduler;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IDesktopInteractionService _desktopInteraction;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _timeProvider;
    private readonly IAIWorkflowHelperDialogService? _workflowHelperDialog;
    private readonly IWorkflowToolCatalogDialogService? _toolCatalogDialog;
    private readonly IWorkflowPromptEditorDialogService? _promptEditorDialog;
    private readonly IWorkflowStepEditorDialogService? _stepEditorDialog;
    private readonly ITaskCloneDialogService? _cloneDialog;
    private readonly IToolCatalog _catalog;
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;
    private readonly IOptionsMonitor<OllamaOptions>? _ollamaOptions;
    private readonly IOptionsMonitor<OpenAIOptions>? _openAiOptions;
    private readonly IReasoningEffortValidator? _reasoningEffortValidator;
    private readonly IOptionsMonitor<SQLiteOptions>? _sqliteOptions;
    private readonly IPathSandbox? _pathSandbox;
    private readonly IKnowledgeLibraryService? _knowledgeLibrary;
    private readonly ObservableCollection<ToolGroupSelectionItem> _toolGroupSelectionItems;
    private readonly ObservableCollection<TaskCapabilitySelectionItem> _capabilitySelectionItems;
    private string? _requestedTaskId;
    private string? _originalTaskId;
    private AIEditorSnapshot? _assistedUndoSnapshot;
    private bool _isLoading;

    /// <summary>Gets catalog-driven Knowledge tag choices, including stale persisted tags.</summary>
    public ObservableCollection<KnowledgeTagSelectionItem> KnowledgeTagSelections { get; } = [];

    [ObservableProperty]
    private string _editorTitle = "New task";

    [ObservableProperty]
    private string _createdText = "Not saved yet";

    [ObservableProperty]
    private string _createdUtcToolTip = string.Empty;

    [ObservableProperty]
    private string _lastModifiedText = "Not saved yet";

    [ObservableProperty]
    private string _lastModifiedUtcToolTip = string.Empty;

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _schedule = "0 7 * * *";

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private bool _useGlobalSystemPrompt = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModelOverrideEnabled))]
    private bool _useSettingsModel = true;

    [ObservableProperty]
    private string _modelOverride = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemperatureOverrideEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _useGlobalTemperature = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThinkingOverrideEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _useGlobalThinking = true;

    [ObservableProperty]
    private bool _reasoningEnabled = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _reasoningEffortText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _temperatureOverrideText = "0.2";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopPOverrideEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _useGlobalTopP = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _topPOverrideText = "1";

    [ObservableProperty]
    private string _effectiveTemperatureText = "0.2 (Settings)";

    [ObservableProperty]
    private string _effectiveTopPText = "1 (Settings)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextWindowOverrideEnabled))]
    private bool _useGlobalContextWindow = true;

    [ObservableProperty]
    private string _contextWindowOverrideText = string.Empty;

    [ObservableProperty]
    private string _effectiveContextWindowText = string.Empty;

    /// <summary>Gets whether the task context-window override can be edited.</summary>
    public bool IsContextWindowOverrideEnabled => !UseGlobalContextWindow;

    [ObservableProperty]
    private string _generationValidationText =
        "Task generation overrides are valid.";

    [ObservableProperty]
    private bool _hasGenerationValidationError;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _sqliteGuardrailsEnabled = true;

    [ObservableProperty]
    private string _knowledgeTagsText = string.Empty;

    /// <summary>Gets whether the ordinary Knowledge tool group is enabled.</summary>
    public bool IsKnowledgeEnabled => _toolGroupSelectionItems.Any(item => item.IsSelected && string.Equals(item.Name, ToolGroups.Knowledge, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets the deny-by-default Knowledge authorization warning.</summary>
    public string KnowledgeScopeWarning => IsKnowledgeEnabled && string.IsNullOrWhiteSpace(KnowledgeTagsText)
        ? "Knowledge is enabled, but no tags are authorized."
        : string.Empty;

    /// <summary>Gets a warning when the task still references unavailable tags.</summary>
    public string KnowledgeStaleTagWarning => KnowledgeTagSelections.Any(item => item.IsSelected && !item.IsAvailable)
        ? "One or more authorized Knowledge tags are unavailable. Remove them or recreate the tags in the library."
        : string.Empty;

    [ObservableProperty]
    private WorkflowErrorPolicy _selectedErrorPolicy =
        WorkflowErrorPolicy.StopOnError;

    /// <summary>
    /// Gets the task-specific absolute authorized locations.
    /// </summary>
    public ObservableCollection<FileSystemAccessItem> AuthorizedLocations { get; } = [];

    /// <summary>
    /// Gets a value indicating whether local-path authorization is relevant.
    /// </summary>
    public bool ShowAuthorizedLocations => ToolGroupSelections.Any(
        static item => item.IsSelected && item.RequiresLocalPathAuthorization);

    /// <summary>
    /// Gets a value indicating whether the Temperature override editor is enabled.
    /// </summary>
    public bool IsTemperatureOverrideEnabled => !UseGlobalTemperature;

    /// <summary>
    /// Gets a value indicating whether the Top P override editor is enabled.
    /// </summary>
    public bool IsTopPOverrideEnabled => !UseGlobalTopP;

    /// <summary>Gets whether this task explicitly controls thinking.</summary>
    public bool IsThinkingOverrideEnabled => !UseGlobalThinking;

    /// <summary>Gets common reasoning levels while allowing provider-specific text.</summary>
    public IReadOnlyList<string> ReasoningEffortSuggestions { get; } = ["low", "medium", "high"];

    /// <summary>Gets whether the task model override editor is enabled.</summary>
    public bool IsModelOverrideEnabled => !UseSettingsModel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenAIExplainCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenPromptCommand))]
    private string _workflowText = "[]";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoAssistedChangeCommand))]
    private bool _hasAssistedUndoSnapshot;

    [ObservableProperty]
    private ToolDefinition? _selectedWorkflowAlias;

    [ObservableProperty]
    private int _workflowCaretIndex;

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
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    [NotifyCanExecuteChangedFor(nameof(ArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloneAndArchiveCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskEditorViewModel"/> class.
    /// </summary>
    public TaskEditorViewModel(
        ITaskStore taskStore,
        ITaskSchedulerService scheduler,
        IIncidentReporter incidentReporter,
        IDesktopInteractionService desktopInteraction,
        IMessenger messenger,
        TimeProvider timeProvider,
        IAIWorkflowHelperDialogService? workflowHelperDialog = null,
        IWorkflowToolCatalogDialogService? toolCatalogDialog = null,
        IWorkflowPromptEditorDialogService? promptEditorDialog = null,
        IOptionsMonitor<AIOptions>? aiOptions = null,
        IOptionsMonitor<SQLiteOptions>? sqliteOptions = null,
        IPathSandbox? pathSandbox = null,
        IKnowledgeLibraryService? knowledgeLibrary = null,
        IWorkflowStepEditorDialogService? stepEditorDialog = null,
        IToolCatalog? catalog = null,
        IOptionsMonitor<OllamaOptions>? ollamaOptions = null,
        IOptionsMonitor<OpenAIOptions>? openAiOptions = null,
        ITaskCloneDialogService? cloneDialog = null,
        IReasoningEffortValidator? reasoningEffortValidator = null)
    {
        _taskStore = taskStore;
        _scheduler = scheduler;
        _incidentReporter = incidentReporter;
        _desktopInteraction = desktopInteraction;
        _messenger = messenger;
        _timeProvider = timeProvider;
        _workflowHelperDialog = workflowHelperDialog;
        _toolCatalogDialog = toolCatalogDialog;
        _promptEditorDialog = promptEditorDialog;
        _aiOptions = aiOptions;
        _sqliteOptions = sqliteOptions;
        _pathSandbox = pathSandbox;
        _knowledgeLibrary = knowledgeLibrary;
        _stepEditorDialog = stepEditorDialog;
        _catalog = catalog ?? ToolCatalog.Default;
        _ollamaOptions = ollamaOptions;
        _openAiOptions = openAiOptions;
        _cloneDialog = cloneDialog;
        _reasoningEffortValidator = reasoningEffortValidator;
        ErrorPolicies = Enum.GetValues<WorkflowErrorPolicy>();
        WorkflowAliases = ToolMap.Definitions;
        _toolGroupSelectionItems = new ObservableCollection<ToolGroupSelectionItem>(
            ToolGroups.TaskWhitelist.Select(
                static group => new ToolGroupSelectionItem(
                    group,
                    ToolMap.Definitions.Any(
                        tool =>
                            string.Equals(
                                tool.ToolGroup,
                                group.Name,
                                StringComparison.OrdinalIgnoreCase) &&
                            tool.LocalPathArguments.Count > 0))));
        ToolGroupSelections = new ReadOnlyObservableCollection<ToolGroupSelectionItem>(
            _toolGroupSelectionItems);
        _capabilitySelectionItems = new ObservableCollection<TaskCapabilitySelectionItem>(
            TaskCapabilityCatalog.Definitions.Select(static definition =>
                new TaskCapabilitySelectionItem(definition)));
        CapabilitySelections = new ReadOnlyObservableCollection<TaskCapabilitySelectionItem>(
            _capabilitySelectionItems);
        foreach (var selection in _toolGroupSelectionItems)
        {
            selection.PropertyChanged += OnToolGroupSelectionChanged;
        }
        foreach (var selection in _capabilitySelectionItems)
        {
            selection.PropertyChanged += OnCapabilitySelectionChanged;
        }

        SelectedWorkflowAlias = WorkflowAliases.FirstOrDefault();
        PropertyChanged += OnEditablePropertyChanged;
        AuthorizedLocations.CollectionChanged += OnAuthorizedLocationsChanged;
        UpdateCronPreview();
    }

    /// <summary>
    /// Gets the supported task-level workflow error policies.
    /// </summary>
    public IReadOnlyList<WorkflowErrorPolicy> ErrorPolicies { get; }

    /// <summary>
    /// Gets the workflow steps available in the Tool Map.
    /// </summary>
    public IReadOnlyList<ToolDefinition> WorkflowAliases { get; }

    /// <summary>
    /// Gets task-whitelist choices projected from the shared ordered tool-group catalog.
    /// </summary>
    public ReadOnlyObservableCollection<ToolGroupSelectionItem> ToolGroupSelections { get; }

    /// <summary>Gets sensitive permissions applicable to the current task draft.</summary>
    public ReadOnlyObservableCollection<TaskCapabilitySelectionItem> CapabilitySelections { get; }

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

    void IDeactivatablePage.Deactivate() =>
        ClearAssistedUndo();

    /// <inheritdoc />
    public async Task<bool> CanNavigateAwayAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        switch (_desktopInteraction.PromptUnsavedChanges(
                    "Unsaved task",
                    "Save your task changes before leaving the editor?"))
        {
            case UnsavedChangesChoice.Save:
                await SaveAsync(CancellationToken.None).ConfigureAwait(true);
                return !IsDirty;
            case UnsavedChangesChoice.Discard:
                IsDirty = false;
                ClearAssistedUndo();
                return true;
            default:
                return false;
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

    partial void OnUseGlobalTemperatureChanged(bool value) =>
        UpdateGenerationState();

    partial void OnTemperatureOverrideTextChanged(string value) =>
        UpdateGenerationState();

    partial void OnUseGlobalContextWindowChanged(bool value) => UpdateGenerationState();

    partial void OnContextWindowOverrideTextChanged(string value) => UpdateGenerationState();

    partial void OnUseGlobalTopPChanged(bool value) =>
        UpdateGenerationState();

    partial void OnUseGlobalThinkingChanged(bool value) =>
        UpdateGenerationState();

    partial void OnTopPOverrideTextChanged(string value) =>
        UpdateGenerationState();

    partial void OnReasoningEnabledChanged(bool value)
    {
        if (!_isLoading)
        {
            UseGlobalThinking = false;
        }

        UpdateGenerationState();
    }

    partial void OnReasoningEffortTextChanged(string value)
    {
        if (!_isLoading && !string.IsNullOrWhiteSpace(value))
        {
            UseGlobalThinking = false;
        }

        UpdateGenerationState();
    }

    partial void OnWorkflowTextChanged(string value)
    {
        WorkflowCaretIndex = WorkflowCaretInsertionService.ClampCaret(
            value,
            WorkflowCaretIndex);
        OpenToolCatalogCommand.NotifyCanExecuteChanged();
        OpenWorkflowPromptEditorCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CapabilityWarning));
        RefreshCurrentStep();
    }

    partial void OnWorkflowCaretIndexChanged(int value) => RefreshCurrentStep();

    /// <summary>Gets the compact caret-derived current step projection.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InsertWorkflowStepBeforeCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertWorkflowStepAfterCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditWorkflowStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteWorkflowStepCommand))]
    private string _currentWorkflowStepText = "No current step — the workflow is empty.";

    [RelayCommand(CanExecute = nameof(CanInsertWorkflowStep))]
    private void InsertWorkflowStepBefore() => OpenStepEditor(WorkflowStepEditorMode.InsertBefore);

    [RelayCommand(CanExecute = nameof(CanInsertWorkflowStep))]
    private void InsertWorkflowStepAfter() => OpenStepEditor(WorkflowStepEditorMode.InsertAfter);

    [RelayCommand(CanExecute = nameof(CanEditWorkflowStep))]
    private void EditWorkflowStep() => OpenStepEditor(WorkflowStepEditorMode.Edit);

    [RelayCommand(CanExecute = nameof(CanDeleteWorkflowStep))]
    private void DeleteWorkflowStep()
    {
        var location = WorkflowStepEditingService.Locate(WorkflowText, WorkflowCaretIndex).Location;
        if (location is null) return;
        try
        {
            var workflow = WorkflowDefinitionSerializer.Deserialize(WorkflowText);
            var dependents = WorkflowStepDependencyAnalyzer.FindLaterReferences(workflow, location.Index);
            var dependencyText = dependents.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}{Environment.NewLine}The remaining workflow will contain broken references in {string.Join(", ", dependents.Select(static dependency => $"#{dependency.StepIndex + 1} “{dependency.EffectiveId}” ({string.Join(", ", dependency.ArgumentNames)})"))}.";
            if (!_desktopInteraction.ConfirmDestructive(
                    dependents.Count == 0 ? "Delete workflow step" : "Delete referenced workflow step",
                    $"Delete step #{location.Index + 1} “{location.EffectiveId}” ({location.Alias})?{Environment.NewLine}This removes the step from the workflow draft. Task permissions are not removed.{dependencyText}",
                    dependents.Count == 0 ? "Delete step" : "Delete anyway"))
            {
                return;
            }
            var mutation = WorkflowStepEditingService.Delete(WorkflowText, location.Index, location.Alias);
            ApplyStepEditorResult(new WorkflowStepEditorResult(mutation.Text, mutation.CaretIndex, [], [], [], $"Workflow step deleted from #{location.Index + 1} — {location.Alias} ({location.EffectiveId}). Task permissions were unchanged. Save the task to persist this change.")
            {
                AffectedIndex = mutation.AffectedIndex,
                Operation = "Delete",
                Alias = location.Alias,
                EffectiveId = location.EffectiveId,
                CanonicalFormattingApplied = mutation.CanonicalFormattingApplied,
                Warnings = dependents.Count == 0 ? [] : ["The remaining workflow contains dependent placeholder references that must be repaired."],
            });
        }
        catch (WorkflowValidationException exception) { ValidationText = exception.Message; }
    }

    private bool CanInsertWorkflowStep() => WorkflowStepEditingService.Locate(WorkflowText, WorkflowCaretIndex).Error is null;
    private bool CanEditWorkflowStep() { var location = WorkflowStepEditingService.Locate(WorkflowText, WorkflowCaretIndex).Location; return location is not null && _catalog.Find(location.Alias) is not null; }
    private bool CanDeleteWorkflowStep() => WorkflowStepEditingService.Locate(WorkflowText, WorkflowCaretIndex).Location is not null;
    private void RefreshCurrentStep()
    {
        var result = WorkflowStepEditingService.Locate(WorkflowText, WorkflowCaretIndex);
        CurrentWorkflowStepText = result.Error is not null ? result.Error : result.Location is null ? "No current step — the workflow is empty." : $"Current step: #{result.Location.Index + 1} — {result.Location.Alias} ({result.Location.EffectiveId})";
        InsertWorkflowStepBeforeCommand.NotifyCanExecuteChanged(); InsertWorkflowStepAfterCommand.NotifyCanExecuteChanged(); EditWorkflowStepCommand.NotifyCanExecuteChanged(); DeleteWorkflowStepCommand.NotifyCanExecuteChanged();
    }
    private void OpenStepEditor(WorkflowStepEditorMode mode)
    {
        if (_stepEditorDialog is null) { ValidationText = "Workflow step editor is unavailable."; return; }
        try
        {
            var location = WorkflowStepEditingService.Locate(WorkflowText, WorkflowCaretIndex);
            if (location.Error is not null || mode == WorkflowStepEditorMode.Edit && location.Location is null)
            {
                ValidationText = location.Error ?? "No workflow step is selected.";
                return;
            }
            var workflow = DeserializeWorkflow(WorkflowText);
            var effective = GenerationPolicyResolver.Resolve(CurrentGenerationDefaults, TryCreateGenerationOptions(out var overrides) ? overrides : null);
            var result = _stepEditorDialog.Show(new WorkflowStepEditorRequest(WorkflowText, WorkflowCaretIndex, mode, GetSelectedToolGroups(), AuthorizedLocations.Select(static item => item.ToRule()).ToArray(), GetSelectedCapabilities(), CreateKnowledgeAccess()?.AllowedTags ?? [], SelectedErrorPolicy, effective.Temperature, effective.TopP)
            {
                ConfiguredModel = GetConfiguredProviderModel(),
                ContextWindowTokens = effective.ContextWindowTokens,
                ContextWindowSource = effective.ContextWindowSource,
                TaskModel = UseSettingsModel ? null : ModelOverride.Trim(),
                DraftTask = CreateWorkflowPreviewTask(workflow),
            });
            if (result is not null) ApplyStepEditorResult(result);
        }
        catch (Exception exception) when (exception is WorkflowValidationException or TaskValidationException or JsonException or FormatException)
        {
            ValidationText = exception.Message;
        }
        catch (Exception exception)
        {
            ValidationText = "The workflow step editor encountered an unexpected error. No draft changes were applied.";
            _ = ReportAsync(exception, "Workflow step editor");
        }
    }

    private string? GetConfiguredProviderModel()
    {
        var provider = _aiOptions?.CurrentValue.Provider;
        return string.Equals(provider, AIOptions.OpenAIProvider, StringComparison.OrdinalIgnoreCase)
            ? _openAiOptions?.CurrentValue.Model
            : _ollamaOptions?.CurrentValue.Model;
    }
    private void ApplyStepEditorResult(WorkflowStepEditorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _ = DeserializeWorkflow(result.WorkflowText);
        if (WorkflowStepEditingService.Locate(result.WorkflowText, result.CaretIndex).Error is { } error)
        {
            throw new WorkflowValidationException(error);
        }
        if (result.AcceptedToolGroups.Any(group => !ToolGroups.All.Contains(group))) throw new TaskValidationException("The step editor returned an unknown task tool group.");
        if (result.AcceptedCapabilities.Any(capability => capability == TaskCapability.None || !Enum.IsDefined(capability))) throw new TaskValidationException("The step editor returned an unknown task capability.");
        foreach (var rule in result.AcceptedFileSystemAccess)
        {
            if (string.IsNullOrWhiteSpace(rule.Path) || !Path.IsPathRooted(Environment.ExpandEnvironmentVariables(rule.Path.Trim())) || !Enum.IsDefined(rule.Access)) throw new TaskValidationException("The step editor returned an invalid file-system authorization proposal.");
        }
        CaptureAssistedUndoSnapshot();
        _isLoading = true;
        try
        {
            WorkflowText = result.WorkflowText;
            WorkflowCaretIndex = result.CaretIndex;
            foreach (var group in result.AcceptedToolGroups)
            {
                SetToolGroupSelected(group, true);
            }
            foreach (var capability in result.AcceptedCapabilities)
            {
                var selection = _capabilitySelectionItems.FirstOrDefault(item => item.Capability == capability);
                if (selection is not null)
                {
                    selection.IsSelected = true;
                }
            }
            foreach (var access in result.AcceptedFileSystemAccess)
            {
                var existing = AuthorizedLocations.FirstOrDefault(item => string.Equals(ScheduledTaskValidator.NormalizeAccessPath(item.Path), ScheduledTaskValidator.NormalizeAccessPath(access.Path), StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    AuthorizedLocations.Add(new FileSystemAccessItem(access.Path, access.Access));
                }
                else if (access.Access == FileSystemAccessMode.ReadWrite)
                {
                    existing.CanWrite = true;
                }
            }
        }
        finally
        {
            _isLoading = false;
        }
        HasAssistedUndoSnapshot = true;
        IsDirty = true;
        ValidationText = result.Summary;
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        _isLoading = true;
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
            NotifyTaskManagementCanExecuteChanged();
            ClearAssistedUndo();
            EditorTitle = task is null ? "New task" : $"Edit task · {task.Name}";
            UpdateTimestampDisplay(task);
            Id = task?.Id ?? string.Empty;
            Name = task?.Name ?? string.Empty;
            Schedule = task?.Schedule ?? "0 7 * * *";
            Prompt = task?.Prompt ?? string.Empty;
            UseGlobalSystemPrompt = task?.UseGlobalSystemPrompt ?? true;
            UseSettingsModel = string.IsNullOrWhiteSpace(task?.Model);
            ModelOverride = task?.Model ?? string.Empty;
            var defaults = CurrentGenerationDefaults;
            UseGlobalTemperature =
                task?.GenerationOptions?.Temperature is null;
            TemperatureOverrideText =
                (task?.GenerationOptions?.Temperature ??
                 defaults.Temperature).ToString(
                    "G",
                    CultureInfo.CurrentCulture);
            UseGlobalTopP = task?.GenerationOptions?.TopP is null;
            UseGlobalContextWindow = task?.GenerationOptions?.ContextWindowTokens is null;
            ContextWindowOverrideText = (task?.GenerationOptions?.ContextWindowTokens ?? defaults.ContextWindowTokens)
                .ToString(CultureInfo.InvariantCulture);
            UseGlobalThinking = task?.GenerationOptions?.ReasoningEnabled is null &&
                task?.GenerationOptions?.ReasoningEffort is null;
            ReasoningEnabled = task?.GenerationOptions?.ReasoningEnabled ?? true;
            ReasoningEffortText = task?.GenerationOptions?.ReasoningEffort ?? string.Empty;
            TopPOverrideText =
                (task?.GenerationOptions?.TopP ??
                 defaults.TopP).ToString(
                    "G",
                    CultureInfo.CurrentCulture);
            UpdateGenerationState();
            IsEnabled = task?.Enabled ?? true;
            SqliteGuardrailsEnabled = task?.SqliteGuardrailsEnabled ??
                (_sqliteOptions?.CurrentValue.GuardrailsEnabledByDefault ?? true);
            SelectedErrorPolicy =
                task?.ErrorPolicy ?? WorkflowErrorPolicy.StopOnError;
            SetSelectedToolGroups(task?.Tools ?? []);
            SetSelectedCapabilities(task?.Capabilities ?? []);
            var persistedKnowledgeTags = task?.KnowledgeAccess?.AllowedTags ?? [];
            var knowledgeTagCatalog = _knowledgeLibrary is null
                ? Array.Empty<string>()
                : await _knowledgeLibrary.GetTagCatalogAsync(cancellationToken).ConfigureAwait(true);
            BuildKnowledgeTagSelections(knowledgeTagCatalog, persistedKnowledgeTags);
            KnowledgeTagsText = string.Join(", ", persistedKnowledgeTags);
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
            IsDirty = false;
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
            _isLoading = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            EnsureNoApplicationOwnedAuthorization();
            var generationOptions = CreateGenerationOptions();
            var workflow = DeserializeWorkflow(WorkflowText);
            var task = new ScheduledTask
            {
                Id = Id.Trim(),
                Name = Name.Trim(),
                Schedule = Schedule.Trim(),
                Prompt = Prompt.Trim(),
                UseGlobalSystemPrompt = UseGlobalSystemPrompt,
                GenerationOptions = generationOptions,
                Model = UseSettingsModel ? null : ModelOverride.Trim(),
                Tools = GetSelectedToolGroups(),
                KnowledgeAccess = CreateKnowledgeAccess(),
                Capabilities = GetSelectedCapabilities(),
                FileSystemAccess = AuthorizedLocations
                    .Select(static item => item.ToRule())
                    .ToArray(),
                Workflow = workflow,
                Enabled = IsEnabled,
                SqliteGuardrailsEnabled = SqliteGuardrailsEnabled,
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

            var reasoningValidationWarning = ValidateReasoningEfforts(task);
            var persistedTask = await _taskStore
                .SaveAsync(task, cancellationToken)
                .ConfigureAwait(true);
            await SynchronizeRegistrationAsync(persistedTask, cancellationToken)
                .ConfigureAwait(true);
            _originalTaskId = persistedTask.Id;
            NotifyTaskManagementCanExecuteChanged();
            ClearAssistedUndo();
            Id = persistedTask.Id;
            IsIdReadOnly = true;
            EditorTitle = $"Edit task · {persistedTask.Name}";
            UpdateTimestampDisplay(persistedTask);
            ValidationText = string.IsNullOrWhiteSpace(reasoningValidationWarning)
                ? "Task saved and scheduler registration synchronized."
                : $"Task saved and scheduler registration synchronized. {reasoningValidationWarning}";
            IsDirty = false;
            _messenger.Send(new TaskSavedMessage(persistedTask.Id));
            _messenger.Send(
                new SnackbarNotificationMessage(
                    $"Task '{persistedTask.Name}' saved."));
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
            _desktopInteraction.ShowError(
                "Task was not saved",
                "The task definition is incomplete or invalid. No changes were saved.\n\n" +
                ValidationText);
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

    private void UpdateTimestampDisplay(ScheduledTask? task)
    {
        CreatedText = FormatTimestamp(task?.CreatedUtc);
        CreatedUtcToolTip = FormatUtcTimestamp(task?.CreatedUtc);
        LastModifiedText = FormatTimestamp(task?.UpdatedUtc);
        LastModifiedUtcToolTip = FormatUtcTimestamp(task?.UpdatedUtc);
    }

    private string? ValidateReasoningEfforts(ScheduledTask task)
    {
        if (_reasoningEffortValidator is null)
        {
            return null;
        }

        var provider = _aiOptions?.CurrentValue.Provider ?? AIOptions.OllamaProvider;
        var configuredModel = string.Equals(provider, AIOptions.OllamaProvider, StringComparison.OrdinalIgnoreCase)
            ? _ollamaOptions?.CurrentValue.Model
            : _openAiOptions?.CurrentValue.Model;
        var taskModel = task.Model ?? configuredModel;
        if (string.IsNullOrWhiteSpace(taskModel))
        {
            return "Reasoning levels could not be validated because no effective model is configured.";
        }

        var requests = new Dictionary<string, (string Model, string Effort)>(StringComparer.Ordinal);
        AddRequest(requests, taskModel, task.GenerationOptions?.ReasoningEffort);
        foreach (var step in task.Workflow)
        {
            string? effort;
            if (string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) &&
                step.Args.ValueKind == JsonValueKind.Object &&
                step.Args.TryGetProperty("thinking", out var thinking))
            {
                effort = thinking.ValueKind == JsonValueKind.String
                    ? thinking.GetString()
                    : null;
            }
            else
            {
                effort = step.GenerationOptions?.ReasoningEffort;
            }

            AddRequest(requests, step.Model ?? taskModel, effort);
        }

        var warnings = new List<string>();
        foreach (var request in requests.Values)
        {
            var result = _reasoningEffortValidator
                .ValidateCached(request.Model, request.Effort);
            if (result.Status == ReasoningEffortValidationStatus.Rejected)
            {
                ClearRejectedReasoningEffort(task, request.Model, request.Effort, taskModel);
                throw new TaskValidationException(
                    $"Model '{request.Model}' rejected reasoning level '{request.Effort}'. The rejected value was removed; enter another level or leave it blank for automatic reasoning.");
            }

            if (result.Status == ReasoningEffortValidationStatus.Unavailable)
            {
                warnings.Add(result.Message);
            }
        }

        return warnings.Count == 0
            ? null
            : string.Join(" ", warnings.Distinct(StringComparer.Ordinal));
    }

    private void ClearRejectedReasoningEffort(
        ScheduledTask task,
        string rejectedModel,
        string rejectedEffort,
        string taskModel)
    {
        if (string.Equals(taskModel, rejectedModel, StringComparison.Ordinal) &&
            string.Equals(task.GenerationOptions?.ReasoningEffort, rejectedEffort, StringComparison.Ordinal))
        {
            ReasoningEffortText = string.Empty;
        }

        var changed = false;
        var workflow = task.Workflow.Select(step =>
        {
            if (!string.Equals(step.Model ?? taskModel, rejectedModel, StringComparison.Ordinal))
            {
                return step;
            }

            if (string.Equals(step.Step, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) &&
                step.Args.ValueKind == JsonValueKind.Object &&
                step.Args.TryGetProperty("thinking", out var thinking) &&
                thinking.ValueKind == JsonValueKind.String &&
                string.Equals(thinking.GetString(), rejectedEffort, StringComparison.Ordinal))
            {
                var arguments = step.Args.EnumerateObject()
                    .Where(static property => !string.Equals(property.Name, "thinking", StringComparison.Ordinal))
                    .ToDictionary(
                        static property => property.Name,
                        static property => property.Value.Clone(),
                        StringComparer.Ordinal);
                changed = true;
                return step with { Args = JsonSerializer.SerializeToElement(arguments) };
            }

            if (!string.Equals(
                    step.GenerationOptions?.ReasoningEffort,
                    rejectedEffort,
                    StringComparison.Ordinal))
            {
                return step;
            }

            var generationOptions = step.GenerationOptions! with { ReasoningEffort = null };
            changed = true;
            return step with
            {
                GenerationOptions = generationOptions.IsEmpty ? null : generationOptions,
            };
        }).ToArray();

        if (changed)
        {
            WorkflowText = WorkflowDefinitionSerializer.Serialize(workflow);
        }
    }

    private static void AddRequest(
        IDictionary<string, (string Model, string Effort)> requests,
        string model,
        string? effort)
    {
        if (string.IsNullOrWhiteSpace(effort))
        {
            return;
        }

        var key = string.Concat(model, "\0", effort);
        requests.TryAdd(key, (model, effort));
    }

    private string FormatTimestamp(DateTimeOffset? timestamp)
    {
        if (timestamp is null)
        {
            return "Not saved yet";
        }

        return TimeZoneInfo.ConvertTime(timestamp.Value, _timeProvider.LocalTimeZone)
            .ToString("g", CultureInfo.CurrentCulture);
    }

    private static string FormatUtcTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ??
        string.Empty;

    [RelayCommand(CanExecute = nameof(CanManagePersistedTask))]
    private async Task CloneAsync(CancellationToken cancellationToken)
    {
        var source = await GetSourceTaskAsync(cancellationToken).ConfigureAwait(true);
        if (source is null || _cloneDialog is null)
        {
            if (_cloneDialog is null)
            {
                ValidationText = "Task cloning is unavailable.";
            }

            return;
        }

        var cloneId = _cloneDialog.Show(source.Id);
        if (string.IsNullOrWhiteSpace(cloneId))
        {
            return;
        }

        await CloneAndOpenAsync(source, cloneId, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanManagePersistedTask))]
    private async Task ArchiveAsync(CancellationToken cancellationToken)
    {
        var source = await GetSourceTaskAsync(cancellationToken).ConfigureAwait(true);
        if (source is null ||
            !_desktopInteraction.ConfirmDestructive(
                "Archive task",
                $"Archive '{source.Name}'? Its Windows scheduler registration will be removed and its saved JSON will be retained in the archives folder." +
                (IsDirty ? " Unsaved editor changes will not be included." : string.Empty),
                "Archive task"))
        {
            return;
        }

        if (await ArchiveSourceAsync(source, cancellationToken).ConfigureAwait(true))
        {
            IsDirty = false;
            _messenger.Send(new NavigateMessage(PageRoute.Tasks));
        }
    }

    [RelayCommand(CanExecute = nameof(CanManagePersistedTask))]
    private async Task CloneAndArchiveAsync(CancellationToken cancellationToken)
    {
        var source = await GetSourceTaskAsync(cancellationToken).ConfigureAwait(true);
        if (source is null || _cloneDialog is null)
        {
            if (_cloneDialog is null)
            {
                ValidationText = "Task cloning is unavailable.";
            }

            return;
        }

        var cloneId = _cloneDialog.Show(source.Id);
        if (string.IsNullOrWhiteSpace(cloneId) ||
            !_desktopInteraction.ConfirmDestructive(
                "Clone and archive task",
                $"Create clone '{cloneId.Trim()}' and archive '{source.Name}'? Its Windows scheduler registration will be removed." +
                (IsDirty ? " Unsaved editor changes will not be included in the clone or archive." : string.Empty),
                "Clone and archive"))
        {
            return;
        }

        var clone = await CloneAndOpenAsync(source, cloneId, cancellationToken).ConfigureAwait(true);
        if (clone is not null)
        {
            _ = await ArchiveSourceAsync(source, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task<ScheduledTask?> GetSourceTaskAsync(CancellationToken cancellationToken)
    {
        if (_originalTaskId is null)
        {
            return null;
        }

        IsBusy = true;
        try
        {
            var source = await _taskStore.GetAsync(_originalTaskId, cancellationToken).ConfigureAwait(true);
            if (source is null)
            {
                ValidationText = $"Task '{_originalTaskId}' no longer exists.";
            }

            return source;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            ValidationText = exception.Message;
            await ReportAsync(exception, "Task editor source load").ConfigureAwait(true);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ScheduledTask?> CloneAndOpenAsync(
        ScheduledTask source,
        string cloneId,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var clone = source with
            {
                Id = cloneId.Trim(),
                CreatedUtc = null,
                UpdatedUtc = null,
            };
            ScheduledTaskValidator.Validate(clone);
            if (await _taskStore.GetAsync(clone.Id, cancellationToken).ConfigureAwait(true) is not null)
            {
                throw new TaskValidationException($"Task id '{clone.Id}' already exists.");
            }

            var persistedClone = await _taskStore.SaveAsync(clone, cancellationToken).ConfigureAwait(true);
            await SynchronizeRegistrationAsync(persistedClone, cancellationToken).ConfigureAwait(true);
            _messenger.Send(new TaskSavedMessage(persistedClone.Id));
            _messenger.Send(new SnackbarNotificationMessage($"Task '{source.Name}' cloned as '{persistedClone.Id}'."));
            Open(persistedClone.Id);
            return persistedClone;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is TaskValidationException or WorkflowValidationException or JsonException)
        {
            ValidationText = exception.Message;
            _desktopInteraction.ShowError("Task was not cloned", ValidationText);
            return null;
        }
        catch (Exception exception)
        {
            ValidationText = exception.Message;
            await ReportAsync(exception, "Task cloning").ConfigureAwait(true);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ArchiveSourceAsync(ScheduledTask source, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await _scheduler.UnregisterAsync(source.Id, cancellationToken).ConfigureAwait(true);
            var archivePath = await _taskStore.ArchiveAsync(source.Id, cancellationToken).ConfigureAwait(true);
            if (archivePath is null)
            {
                ValidationText = $"Task '{source.Id}' no longer exists.";
                return false;
            }

            _messenger.Send(new SnackbarNotificationMessage($"Task '{source.Name}' archived."));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ValidationText = exception.Message;
            await ReportAsync(exception, "Task archiving").ConfigureAwait(true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanManagePersistedTask() => _originalTaskId is not null && !IsBusy;

    private void NotifyTaskManagementCanExecuteChanged()
    {
        CloneCommand.NotifyCanExecuteChanged();
        ArchiveCommand.NotifyCanExecuteChanged();
        CloneAndArchiveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenPrompt))]
    private void OpenPrompt()
    {
        if (_originalTaskId is not null &&
            TryCreateGenerationOptions(out var generationOptions))
        {
            _messenger.Send(
                new EditPromptMessage(
                    _originalTaskId,
                    generationOptions,
                    UseGenerationOptionsSnapshot: true));
        }
    }

    [RelayCommand]
    private void ViewSystemPrompt()
    {
        if (!UseGlobalSystemPrompt)
        {
            _desktopInteraction.ShowInformation(
                "Applied system prompt",
                "Configured system prompts are disabled for this task. " +
                "Built-in safety instructions remain active.");
            return;
        }

        var options = _aiOptions?.CurrentValue;
        var agentInstructions = options?.EffectiveAgentSystemPrompt;
        var promptStepInstructions = options?.EffectivePromptStepSystemPrompt;
        var hasAgentInstructions = !string.IsNullOrWhiteSpace(agentInstructions);
        var hasPromptStepInstructions = !string.IsNullOrWhiteSpace(promptStepInstructions);
        _desktopInteraction.ShowInformation(
            "Applied system prompts",
            !hasAgentInstructions && !hasPromptStepInstructions
                ? "No task system prompts are currently configured. " +
                  "Built-in safety instructions remain active."
                : $"Agent tasks:{Environment.NewLine}" +
                  (hasAgentInstructions ? agentInstructions!.Trim() : "(none)") +
                  $"{Environment.NewLine}{Environment.NewLine}AI prompt steps:{Environment.NewLine}" +
                  (hasPromptStepInstructions ? promptStepInstructions!.Trim() : "(none)"));
    }

    [RelayCommand]
    private async Task BackToTasksAsync()
    {
        if (!await CanNavigateAwayAsync().ConfigureAwait(true))
        {
            return;
        }

        _messenger.Send(new NavigateMessage(PageRoute.Tasks));
    }

    [RelayCommand]
    private void AddWorkflowStep()
    {
        if (SelectedWorkflowAlias is null)
        {
            ValidationText = "Choose a workflow step to add.";
            return;
        }

        try
        {
            var workflow = DeserializeWorkflow(WorkflowText).ToList();
            var step = WorkflowSnippetBuilder.Create(
                SelectedWorkflowAlias,
                new WorkflowSnippetOptions(),
                workflow,
                SelectedErrorPolicy,
                temperature: 0,
                topP: 0);
            workflow.Add(step);
            WorkflowText = WorkflowDefinitionSerializer.Serialize(workflow);
            ValidationText =
                $"Step '{SelectedWorkflowAlias.Alias}' added. Complete its arguments.";
        }
        catch (Exception exception)
            when (exception is JsonException or WorkflowValidationException)
        {
            ValidationText = exception.Message;
        }
    }

    [RelayCommand]
    private void FormatWorkflow()
    {
        try
        {
            WorkflowText = JsonSerializer.Serialize(
                DeserializeWorkflow(WorkflowText),
                ScheduledTaskSerializer.Options);
            ValidationText = "Workflow JSON formatted.";
        }
        catch (Exception exception)
            when (exception is JsonException or WorkflowValidationException)
        {
            ValidationText = exception.Message;
        }
    }

    [RelayCommand]
    private void ValidateWorkflow()
    {
        try
        {
            var workflow = DeserializeWorkflow(WorkflowText);
            ScheduledTaskValidator.Validate(
                new ScheduledTask
                {
                    Id = string.IsNullOrWhiteSpace(Id) ? "workflow-preview" : Id.Trim(),
                    Name = string.IsNullOrWhiteSpace(Name) ? "Workflow preview" : Name.Trim(),
                    Schedule = string.IsNullOrWhiteSpace(Schedule)
                        ? "0 7 * * *"
                        : Schedule.Trim(),
                    Prompt = string.IsNullOrWhiteSpace(Prompt)
                        ? "Workflow preview"
                        : Prompt.Trim(),
                    GenerationOptions = CreateGenerationOptions(),
                    Model = UseSettingsModel ? null : ModelOverride.Trim(),
                    Tools = GetSelectedToolGroups(),
                    KnowledgeAccess = CreateKnowledgeAccess(),
                    Workflow = workflow,
                    Enabled = false,
                    ErrorPolicy = SelectedErrorPolicy,
                });
            ValidationText =
                $"Workflow is valid ({workflow.Count} step(s)).";
            _messenger.Send(new SnackbarNotificationMessage("Validation OK."));
        }
        catch (Exception exception)
            when (exception is TaskValidationException or
                WorkflowValidationException or
                JsonException)
        {
            ValidationText = AddAliasSuggestion(exception.Message);
            _messenger.Send(new SnackbarNotificationMessage("Invalid workflow."));
        }
    }

    [RelayCommand]
    private void LoadTranslationExample()
    {
        WorkflowText =
            """
            [
              {
                "step": "filesystem.read",
                "id": "source",
                "args": {
                  "path": "inbox/source.txt"
                }
              },
              {
                "step": "ai.prompt",
                "id": "translation",
                "args": {
                  "prompt": "Translate the source file into English. Return only the translated text.",
                  "includeContext": true
                }
              },
              {
                "step": "filesystem.write",
                "id": "save",
                "args": {
                  "path": "translations/source.en.txt",
                  "content": "{{translation.output}}"
                }
              }
            ]
            """;
        SetToolGroupSelected(ToolGroups.FileSystem, isSelected: true);
        ValidationText =
            "Translation example loaded. Adapt the source and destination paths.";
    }

    [RelayCommand(CanExecute = nameof(CanOpenToolCatalog))]
    private void OpenToolCatalog()
    {
        if (_toolCatalogDialog is null)
        {
            ValidationText = "Tool catalog is unavailable.";
            return;
        }

        var effective = GenerationPolicyResolver.Resolve(
            CurrentGenerationDefaults,
            TryCreateGenerationOptions(out var overrides) ? overrides : null);
        var result = _toolCatalogDialog.Show(
            new WorkflowToolCatalogRequest(
                WorkflowText,
                WorkflowCaretIndex,
                GetSelectedToolGroups().ToArray(),
                AuthorizedLocations.Select(static item => item.ToRule()).ToArray(),
                GetSelectedCapabilities(),
                SelectedErrorPolicy,
                effective.Temperature,
                effective.TopP));
        if (result is not null)
        {
            ApplyToolCatalogResult(result);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenWorkflowPromptEditor))]
    private void OpenWorkflowPromptEditor()
    {
        if (_promptEditorDialog is null)
        {
            ValidationText = "Workflow prompt editor is unavailable.";
            return;
        }

        WorkflowPromptEditorResult? result;
        try
        {
            result = _promptEditorDialog.Show(new WorkflowPromptEditorRequest(
                WorkflowText,
                GetSelectedToolGroups(),
                CreateKnowledgeAccess()?.AllowedTags));
        }
        catch (WorkflowValidationException exception)
        {
            ValidationText =
                $"The workflow must be valid JSON before editing prompts. {exception.Message}";
            return;
        }

        if (result is not null)
        {
            ApplyPromptEditorResult(result);
        }
    }

    [RelayCommand]
    private void OpenAIHelper()
    {
        if (_workflowHelperDialog is null)
        {
            ValidationText = "AI Workflow Helper is unavailable.";
            return;
        }

        try
        {
            var workflow = DeserializeWorkflow(WorkflowText);
            var result = _workflowHelperDialog.Show(
                CreateHelperRequest(AIWorkflowHelperMode.Author, workflow));
            if (result is not null)
            {
                ApplyAIResult(result);
            }
        }
        catch (Exception exception) when (
            exception is TaskValidationException or
                WorkflowValidationException or
                JsonException)
        {
            ValidationText = AddAliasSuggestion(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenAIExplain))]
    private void OpenAIExplain()
    {
        if (_workflowHelperDialog is null)
        {
            ValidationText = "AI Workflow Helper is unavailable.";
            return;
        }

        try
        {
            var workflow = DeserializeWorkflow(WorkflowText);
            if (workflow.Count == 0)
            {
                ValidationText = "AI Explain requires at least one workflow step.";
                return;
            }

            ValidateWorkflowPreview(workflow);
            _ = _workflowHelperDialog.Show(
                CreateHelperRequest(AIWorkflowHelperMode.Explain, workflow));
        }
        catch (Exception exception) when (
            exception is TaskValidationException or
                WorkflowValidationException or
                JsonException)
        {
            ValidationText = AddAliasSuggestion(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndoAssistedChange))]
    private void UndoAssistedChange()
    {
        if (_assistedUndoSnapshot is null)
        {
            return;
        }

        WorkflowText = _assistedUndoSnapshot.WorkflowText;
        SetSelectedToolGroups(_assistedUndoSnapshot.ToolGroups);
        SetSelectedCapabilities(_assistedUndoSnapshot.Capabilities);
        AuthorizedLocations.Clear();
        foreach (var rule in _assistedUndoSnapshot.FileSystemAccess)
        {
            AuthorizedLocations.Add(
                new FileSystemAccessItem(rule.Path, rule.Access));
        }

        ClearAssistedUndo();
        ValidationText = "The last assisted workflow change was undone.";
    }

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
        if (_pathSandbox?.IsApplicationOwnedPath(normalizedPath) == true)
        {
            ValidationText =
                $"Location '{normalizedPath}' is reserved for application data and cannot be authorized.";
            return;
        }

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

    private void EnsureNoApplicationOwnedAuthorization()
    {
        var reserved = AuthorizedLocations.FirstOrDefault(
            item => _pathSandbox?.IsApplicationOwnedPath(item.Path) == true);
        if (reserved is not null)
        {
            throw new TaskValidationException(
                $"File-system access path '{reserved.Path}' is reserved for application data.");
        }
    }

    private bool CanOpenPrompt() =>
        IsIdReadOnly &&
        (string.IsNullOrWhiteSpace(WorkflowText) ||
         string.Equals(WorkflowText.Trim(), "[]", StringComparison.Ordinal));

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
        => [.. ToolGroupSelections
            .Where(static item => item.IsSelected)
            .Select(static item => item.Name)];

    private IReadOnlyList<TaskCapability> GetSelectedCapabilities() =>
        [.. CapabilitySelections
            .Where(static item => item.IsSelected)
            .Select(static item => item.Capability)];

    private void SetSelectedCapabilities(IEnumerable<TaskCapability> capabilities)
    {
        var selected = capabilities.ToHashSet();
        foreach (var item in CapabilitySelections)
        {
            item.IsSelected = selected.Contains(item.Capability);
        }

        UpdateCapabilityVisibility();
    }

    private void UpdateCapabilityVisibility()
    {
        var groups = GetSelectedToolGroups().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relevant = ToolMap.Definitions
            .Where(definition => groups.Contains(definition.ToolGroup))
            .Where(static definition => definition.RequiredCapability is not null)
            .Select(static definition => definition.RequiredCapability!.Value)
            .ToHashSet();
        foreach (var item in CapabilitySelections)
        {
            item.IsVisible = item.IsSelected || relevant.Contains(item.Capability);
        }
    }

    /// <summary>Gets a warning for workflow operations lacking task permission.</summary>
    public string CapabilityWarning
    {
        get
        {
            try
            {
                var analysis = WorkflowRequirementAnalyzer.Analyze(
                    DeserializeWorkflow(WorkflowText),
                    GetSelectedToolGroups(),
                    AuthorizedLocations.Select(static item => item.ToRule()).ToArray(),
                    GetSelectedCapabilities());
                var missing = analysis.CapabilityRequirements
                    .Where(static requirement => !requirement.IsGranted)
                    .Select(static requirement => requirement.Capability)
                    .ToArray();
                return missing.Length == 0
                    ? string.Empty
                    : $"This workflow uses operations that are not authorized: {string.Join(", ", missing)}. Grant them below or the task will fail before the side effect.";
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private bool CanOpenAIExplain() =>
        !string.IsNullOrWhiteSpace(WorkflowText) &&
        !string.Equals(WorkflowText.Trim(), "[]", StringComparison.Ordinal);

    private bool CanUndoAssistedChange() => HasAssistedUndoSnapshot;

    private bool CanOpenToolCatalog() => _toolCatalogDialog is not null;

    private bool CanOpenWorkflowPromptEditor() => _promptEditorDialog is not null;

    private AIWorkflowHelperRequest CreateHelperRequest(
        AIWorkflowHelperMode mode,
        IReadOnlyList<WorkflowStepDefinition> workflow)
    {
        return new AIWorkflowHelperRequest(
            mode,
            workflow,
            Id,
            Name,
            Schedule,
            Prompt,
            SelectedErrorPolicy,
            GetSelectedToolGroups(),
            AuthorizedLocations.Select(static item => item.ToRule()).ToArray(),
            GetSelectedCapabilities())
        {
            TaskGenerationOptions = CreateGenerationOptions(),
            GenerationDefaults = CurrentGenerationDefaults,
        };
    }

    private void ValidateWorkflowPreview(IReadOnlyList<WorkflowStepDefinition> workflow) =>
        ScheduledTaskValidator.Validate(CreateWorkflowPreviewTask(workflow));

    private ScheduledTask CreateWorkflowPreviewTask(IReadOnlyList<WorkflowStepDefinition> workflow) =>
            new()
            {
                Id = string.IsNullOrWhiteSpace(Id) ? "workflow-preview" : Id.Trim(),
                Name = string.IsNullOrWhiteSpace(Name) ? "Workflow preview" : Name.Trim(),
                Schedule = string.IsNullOrWhiteSpace(Schedule) ? "0 7 * * *" : Schedule.Trim(),
                Prompt = string.IsNullOrWhiteSpace(Prompt) ? "Workflow preview" : Prompt,
                GenerationOptions = CreateGenerationOptions(),
                Model = UseSettingsModel ? null : ModelOverride.Trim(),
                Tools = GetSelectedToolGroups(),
                KnowledgeAccess = CreateKnowledgeAccess(),
                Capabilities = GetSelectedCapabilities(),
                FileSystemAccess = AuthorizedLocations.Select(static item => item.ToRule()).ToArray(),
                Workflow = workflow,
                Enabled = false,
                SqliteGuardrailsEnabled = SqliteGuardrailsEnabled,
                ErrorPolicy = SelectedErrorPolicy,
            };

    private KnowledgeAccessDefinition? CreateKnowledgeAccess()
    {
        var tags = string.IsNullOrWhiteSpace(KnowledgeTagsText) ? [] : KnowledgeTagsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return tags.Length == 0 ? null : new KnowledgeAccessDefinition { AllowedTags = KnowledgeTagPolicy.NormalizeScope(tags) };
    }

    partial void OnKnowledgeTagsTextChanged(string value)
    {
        OnPropertyChanged(nameof(KnowledgeScopeWarning));
    }

    private void BuildKnowledgeTagSelections(
        IReadOnlyList<string> catalog,
        IReadOnlyList<string> persistedTags)
    {
        foreach (var item in KnowledgeTagSelections)
        {
            item.PropertyChanged -= OnKnowledgeTagSelectionChanged;
        }

        KnowledgeTagSelections.Clear();
        var available = catalog.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = persistedTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in catalog.Concat(persistedTags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase))
        {
            var item = new KnowledgeTagSelectionItem(tag, available.Contains(tag), selected.Contains(tag));
            item.PropertyChanged += OnKnowledgeTagSelectionChanged;
            KnowledgeTagSelections.Add(item);
        }

        OnPropertyChanged(nameof(KnowledgeStaleTagWarning));
    }

    private void OnKnowledgeTagSelectionChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(KnowledgeTagSelectionItem.IsSelected) || _isLoading)
        {
            return;
        }

        KnowledgeTagsText = string.Join(", ", KnowledgeTagSelections.Where(static item => item.IsSelected).Select(static item => item.Name));
        OnPropertyChanged(nameof(KnowledgeStaleTagWarning));
    }

    private void ApplyAIResult(AIWorkflowHelperResult result)
    {
        CaptureAssistedUndoSnapshot();
        WorkflowText = result.WorkflowJson;
        SetSelectedToolGroups(result.ToolGroups);
        SetSelectedCapabilities(result.Capabilities);
        AuthorizedLocations.Clear();
        foreach (var rule in result.FileSystemAccess)
        {
            AuthorizedLocations.Add(
                new FileSystemAccessItem(rule.Path, rule.Access));
        }

        HasAssistedUndoSnapshot = true;
        ValidationText =
            "AI workflow changes applied to the editor. Save the task to persist them.";
    }

    private void ApplyToolCatalogResult(WorkflowToolCatalogResult result)
    {
        CaptureAssistedUndoSnapshot();
        WorkflowText = result.WorkflowText;
        WorkflowCaretIndex = WorkflowCaretInsertionService.ClampCaret(
            result.WorkflowText,
            result.CaretIndex);
        if (result.AcceptedToolGroups.Count > 0)
        {
            SetSelectedToolGroups(
                GetSelectedToolGroups().Concat(result.AcceptedToolGroups));
        }

        HasAssistedUndoSnapshot = true;
        var message = result.Insertions.Count == 1
            ? "1 step inserted from the Tool catalog."
            : $"{result.Insertions.Count} steps inserted from the Tool catalog.";
        ValidationText = result.Warnings.Count == 0
            ? message
            : $"{message} {string.Join(" ", result.Warnings)}";
    }

    private void ApplyPromptEditorResult(WorkflowPromptEditorResult result)
    {
        CaptureAssistedUndoSnapshot();
        WorkflowText = result.WorkflowText;
        WorkflowCaretIndex = WorkflowCaretInsertionService.ClampCaret(
            result.WorkflowText,
            WorkflowCaretIndex);
        HasAssistedUndoSnapshot = true;
        var message = result.ModifiedSteps.Count == 1
            ? "1 prompt step updated from the Workflow prompt editor."
            : $"{result.ModifiedSteps.Count} prompt steps updated from the Workflow prompt editor.";
        ValidationText = result.Warnings.Count == 0
            ? message
            : $"{message} {string.Join(" ", result.Warnings)}";
    }

    private void CaptureAssistedUndoSnapshot() =>
        _assistedUndoSnapshot = new AIEditorSnapshot(
            WorkflowText,
            GetSelectedToolGroups().ToArray(),
            GetSelectedCapabilities().ToArray(),
            AuthorizedLocations.Select(static item => item.ToRule()).ToArray());

    private void SetSelectedToolGroups(IEnumerable<string> toolGroups)
    {
        var groups = new HashSet<string>(
            toolGroups,
            StringComparer.OrdinalIgnoreCase);
        foreach (var selection in ToolGroupSelections)
        {
            selection.IsSelected = groups.Contains(selection.Name);
        }
    }

    /// <summary>
    /// Changes one task tool-group selection through the shared catalog projection.
    /// </summary>
    /// <param name="toolGroup">The stable task-whitelist group name.</param>
    /// <param name="isSelected">Whether the group is granted.</param>
    /// <exception cref="ArgumentException">The group is not a task-whitelist group.</exception>
    public void SetToolGroupSelected(string toolGroup, bool isSelected)
    {
        FindToolGroupSelection(toolGroup).IsSelected = isSelected;
    }

    /// <summary>
    /// Determines whether the current task draft grants a tool group.
    /// </summary>
    /// <param name="toolGroup">The stable task-whitelist group name.</param>
    /// <returns><see langword="true"/> when selected; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">The group is not a task-whitelist group.</exception>
    public bool IsToolGroupSelected(string toolGroup)
    {
        return FindToolGroupSelection(toolGroup).IsSelected;
    }

    private ToolGroupSelectionItem FindToolGroupSelection(string toolGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolGroup);
        return ToolGroupSelections.FirstOrDefault(
            item => string.Equals(
                item.Name,
                toolGroup,
                StringComparison.OrdinalIgnoreCase)) ??
            throw new ArgumentException(
                $"Unknown task tool group '{toolGroup}'.",
                nameof(toolGroup));
    }

    private void ClearAssistedUndo()
    {
        _assistedUndoSnapshot = null;
        HasAssistedUndoSnapshot = false;
    }

    private static IReadOnlyList<WorkflowStepDefinition> DeserializeWorkflow(
        string workflowText)
    {
        if (string.IsNullOrWhiteSpace(workflowText))
        {
            return [];
        }

        return WorkflowDefinitionSerializer.Deserialize(workflowText);
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

    private AIGenerationDefaults CurrentGenerationDefaults =>
        _aiOptions?.CurrentValue.GenerationDefaults ??
        new AIGenerationDefaults();

    private bool CanSave() =>
        !IsBusy && TryCreateGenerationOptions(out _);

    private GenerationOptions? CreateGenerationOptions()
    {
        if (!TryCreateGenerationOptions(out var options))
        {
            throw new TaskValidationException(GenerationValidationText);
        }

        return options;
    }

    private bool TryCreateGenerationOptions(
        out GenerationOptions? options)
    {
        options = null;
        double? temperature = null;
        double? topP = null;
        int? contextWindowTokens = null;
        if (!UseGlobalContextWindow && !string.IsNullOrWhiteSpace(ContextWindowOverrideText))
        {
            if (!int.TryParse(ContextWindowOverrideText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedContext))
            {
                GenerationValidationText = "Task context window must be an integer number of tokens.";
                return false;
            }
            contextWindowTokens = parsedContext;
        }
        if (!UseGlobalTemperature)
        {
            if (!SettingsViewModel.TryParseCultureSafeDouble(
                    TemperatureOverrideText,
                    CultureInfo.CurrentCulture,
                    out var parsedTemperature))
            {
                GenerationValidationText =
                    "Task Temperature must be a number from 0 through 2.";
                return false;
            }

            temperature = parsedTemperature;
        }

        if (!UseGlobalTopP)
        {
            if (!SettingsViewModel.TryParseCultureSafeDouble(
                    TopPOverrideText,
                    CultureInfo.CurrentCulture,
                    out var parsedTopP))
            {
                GenerationValidationText =
                    "Task Top P must be a number greater than 0 and at most 1.";
                return false;
            }

            topP = parsedTopP;
        }

        var reasoningEffort = string.IsNullOrWhiteSpace(ReasoningEffortText)
            ? null
            : ReasoningEffortText.Trim();
        if (temperature is null && topP is null && contextWindowTokens is null && UseGlobalThinking)
        {
            return true;
        }

        var candidate = new GenerationOptions
        {
            Temperature = temperature,
            TopP = topP,
            ContextWindowTokens = contextWindowTokens,
            ReasoningEnabled = UseGlobalThinking ? null : ReasoningEnabled,
            ReasoningEffort = !UseGlobalThinking && ReasoningEnabled ? reasoningEffort : null,
        };
        var error = GenerationPolicyValidation.ValidateOverrides(candidate);
        if (error is not null)
        {
            GenerationValidationText = error;
            return false;
        }

        options = candidate;
        return true;
    }

    private void UpdateGenerationState()
    {
        if (TryCreateGenerationOptions(out var options))
        {
            HasGenerationValidationError = false;
            GenerationValidationText =
                "Valid. Each property applies to task-owned AI calls unless an AI workflow step overrides it.";
            var effective = GenerationPolicyResolver.Resolve(
                CurrentGenerationDefaults,
                options);
            EffectiveTemperatureText =
                FormatEffective(effective.Temperature, effective.TemperatureSource);
            EffectiveTopPText =
                FormatEffective(effective.TopP, effective.TopPSource);
            EffectiveContextWindowText = $"{effective.ContextWindowTokens:N0} tokens ({effective.ContextWindowSource})";
        }
        else
        {
            HasGenerationValidationError = true;
        }

        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnEditablePropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!_isLoading &&
            e.PropertyName is not null &&
            EditablePropertyNames.Contains(e.PropertyName))
        {
            IsDirty = true;
        }
    }

    private void OnAuthorizedLocationsChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (!_isLoading)
        {
            IsDirty = true;
        }
    }

    private void OnToolGroupSelectionChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!string.Equals(
                e.PropertyName,
                nameof(ToolGroupSelectionItem.IsSelected),
                StringComparison.Ordinal))
        {
            return;
        }

        OnPropertyChanged(nameof(ShowAuthorizedLocations));
        OnPropertyChanged(nameof(IsKnowledgeEnabled));
        OnPropertyChanged(nameof(KnowledgeScopeWarning));
        UpdateCapabilityVisibility();
        OnPropertyChanged(nameof(CapabilityWarning));
        if (!_isLoading)
        {
            IsDirty = true;
        }
    }

    private void OnCapabilitySelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskCapabilitySelectionItem.IsSelected) && !_isLoading)
        {
            IsDirty = true;
            UpdateCapabilityVisibility();
            OnPropertyChanged(nameof(CapabilityWarning));
        }
    }

    private static string FormatEffective(
        double value,
        GenerationOptionSource source) =>
        $"{value.ToString("G", CultureInfo.CurrentCulture)} ({source})";

    private sealed record AIEditorSnapshot(
        string WorkflowText,
        IReadOnlyList<string> ToolGroups,
        IReadOnlyList<TaskCapability> Capabilities,
        IReadOnlyList<FileSystemAccessRule> FileSystemAccess);
}
