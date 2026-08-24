using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Builds one typed workflow step form solely from Tool Map metadata.</summary>
public sealed partial class WorkflowStepEditorViewModel : ObservableObject
{
    private WorkflowStepEditorRequest? _request;
    private WorkflowStepLocation? _location;
    private IReadOnlyList<WorkflowStepDefinition> _workflow = [];
    private bool _initializing;
    private bool _synchronizingPromptTools;
    private readonly IToolCatalog _catalog;
    private readonly IDesktopInteractionService? _desktopInteraction;
    private ToolDefinition? _lastSelectedTool;
    private bool _revertingTool;
    private readonly Dictionary<string, FileSystemAccessRule> _pathProposals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new step editor ViewModel.</summary>
    public WorkflowStepEditorViewModel(IToolCatalog? catalog = null, IDesktopInteractionService? desktopInteraction = null)
    {
        _catalog = catalog ?? ToolCatalog.Default;
        _desktopInteraction = desktopInteraction;
        PropertyChanged += OnEditorPropertyChanged;
    }

    [ObservableProperty] private ToolDefinition? _selectedTool;
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private bool _overrideErrorPolicy;
    [ObservableProperty] private WorkflowErrorPolicy _onError;
    [ObservableProperty] private string _retryCount = string.Empty;
    [ObservableProperty] private string _retryBackoffSeconds = string.Empty;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private bool _inheritModel = true;
    [ObservableProperty] private string _toolAliases = string.Empty;
    [ObservableProperty] private string _knowledgeTags = string.Empty;
    [ObservableProperty] private bool _overrideGeneration;
    [ObservableProperty] private bool _overrideTemperature;
    [ObservableProperty] private bool _overrideTopP;
    [ObservableProperty] private bool _overrideReasoning;
    [ObservableProperty] private bool _narrowKnowledge;
    [ObservableProperty] private string _temperature = string.Empty;
    [ObservableProperty] private string _topP = string.Empty;
    [ObservableProperty] private bool? _reasoningEnabled;
    [ObservableProperty] private string _toolSearchText = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InsertPlaceholderCommand))]
    private WorkflowStepArgumentEditorItem? _selectedArgument;
    [ObservableProperty] private WorkflowPlaceholderDefinition? _selectedPlaceholder;

    /// <summary>Gets the currently generated argument rows.</summary>
    public ObservableCollection<WorkflowStepArgumentEditorItem> Arguments { get; } = [];
    /// <summary>Gets missing task authorizations proposed for explicit opt-in only.</summary>
    public ObservableCollection<WorkflowProposalItem> AuthorizationProposals { get; } = [];
    /// <summary>Gets deterministic execution and scope warnings for the provisional workflow.</summary>
    public ObservableCollection<string> ExecutionWarnings { get; } = [];
    /// <summary>Gets agent-callable prompt tools in immutable catalog order.</summary>
    public ObservableCollection<WorkflowPromptToolSelectionItem> PromptTools { get; } = [];
    /// <summary>Gets the task-bounded Knowledge tag choices.</summary>
    public ObservableCollection<WorkflowKnowledgeTagSelectionItem> KnowledgeTagSelections { get; } = [];
    /// <summary>Gets the Tool Map choices in catalog order.</summary>
    public IReadOnlyList<ToolDefinition> Tools => _catalog.Definitions;
    /// <summary>Gets catalog tools matching the current insert-mode search text.</summary>
    public ObservableCollection<ToolDefinition> FilteredTools { get; } = [];
    /// <summary>Gets shared placeholder templates available to eligible text arguments.</summary>
    public IReadOnlyList<WorkflowPlaceholderDefinition> Placeholders => WorkflowPlaceholderCatalog.Entries;
    /// <summary>Gets whether placeholder insertion applies to the selected argument.</summary>
    public bool CanInsertPlaceholder => SelectedArgument?.Definition.SupportsPlaceholders == true;
    /// <summary>Gets the available error policies.</summary>
    public IReadOnlyList<WorkflowErrorPolicy> ErrorPolicies => Enum.GetValues<WorkflowErrorPolicy>();
    /// <summary>Gets whether this is an edit with an immutable alias.</summary>
    public bool IsEdit => _request?.Mode == WorkflowStepEditorMode.Edit;
    /// <summary>Gets whether the Tool Map selector is available.</summary>
    public bool IsInsert => !IsEdit;
    /// <summary>Gets whether the selected step is the exact prompt alias.</summary>
    public bool IsPromptSelected => string.Equals(SelectedTool?.Alias, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase);
    /// <summary>Gets whether the selected step supports generation overrides.</summary>
    public bool IsAiSelected => SelectedTool?.IsAiStep == true;
    /// <summary>Gets whether retry-specific fields apply.</summary>
    public bool IsRetrySelected => (OverrideErrorPolicy ? OnError : _request?.ErrorPolicy) == WorkflowErrorPolicy.Retry;
    /// <summary>Gets whether task-bounded Knowledge narrowing is applicable.</summary>
    public bool ShowKnowledgeTags => IsPromptSelected && PromptTools.Any(static item => item.IsSelected && string.Equals(item.Tool.Alias, "knowledge.search", StringComparison.OrdinalIgnoreCase));
    /// <summary>Gets deterministic model choices assembled from the request only.</summary>
    public IReadOnlyList<string> ModelChoices => new[] { _request?.ConfiguredModel, _request?.TaskModel, Model }.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    /// <summary>Gets the modal title.</summary>
    public string Title => IsEdit ? "Edit workflow step" : "Insert workflow step";
    /// <summary>Gets the primary button label.</summary>
    public string ApplyLabel => IsEdit ? "Apply" : "Insert";
    /// <summary>Gets the result after Apply, or null after Cancel/no-op.</summary>
    public WorkflowStepEditorResult? Result { get; private set; }
    /// <summary>Gets whether the current close request follows a successful/no-op Apply.</summary>
    public bool IsApplyCloseRequested { get; private set; }
    /// <summary>Gets whether closing should confirm discard.</summary>
    public bool HasEffectiveChanges => HasChanges || AuthorizationProposals.Any(static item => item.IsAccepted);
    /// <summary>Raised when the host should close the modal.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Initializes from an immutable task editor snapshot.</summary>
    public void Initialize(WorkflowStepEditorRequest request)
    {
        _initializing = true;
        try
        {
        _request = request;
        var located = WorkflowStepEditingService.Locate(request.WorkflowText, request.CaretIndex);
        if (located.Error is not null)
        {
            throw new WorkflowValidationException($"The workflow must be valid JSON before editing steps. {located.Error}");
        }
        _location = located.Location;
        _workflow = WorkflowDefinitionSerializer.Deserialize(request.WorkflowText);
        BuildPromptToolChoices();
        BuildKnowledgeTagChoices();
        RefreshToolFilter();
        if (IsEdit && _location is null)
        {
            throw new WorkflowValidationException("No workflow step is selected.");
        }
        SelectedTool = IsEdit ? _catalog.Find(_location!.Alias) : Tools.FirstOrDefault();
        if (IsEdit && SelectedTool is null)
        {
            StatusText = "This step uses an unknown alias and can only be deleted or edited in raw JSON.";
            return;
        }
        Rebuild(SelectedTool, IsEdit ? _workflow[_location!.Index] : null);
        }
        finally { HasChanges = false; _initializing = false; RefreshAuthorizationProposals(); HasChanges = false; }
    }

    partial void OnSelectedToolChanged(ToolDefinition? value)
    {
        if (_revertingTool)
        {
            return;
        }
        if (!_initializing && !_revertingTool && !IsEdit && value is not null && _lastSelectedTool is not null && !ReferenceEquals(value, _lastSelectedTool) && HasEffectiveChanges && _desktopInteraction is not null && !_desktopInteraction.Confirm("Discard workflow step fields", "Changing the workflow tool discards all values entered for the current tool. Continue?"))
        {
            _revertingTool = true;
            SelectedTool = _lastSelectedTool;
            _revertingTool = false;
            OnPropertyChanged(nameof(IsPromptSelected));
            OnPropertyChanged(nameof(IsAiSelected));
            OnPropertyChanged(nameof(ShowKnowledgeTags));
            return;
        }
        _lastSelectedTool = value;
        if (_request is not null && !IsEdit && value is not null)
        {
            Rebuild(value, null);
            HasChanges = true;
        }
        OnPropertyChanged(nameof(IsPromptSelected));
        OnPropertyChanged(nameof(IsAiSelected));
        OnPropertyChanged(nameof(ShowKnowledgeTags));
    }

    partial void OnToolSearchTextChanged(string value) => RefreshToolFilter();
    partial void OnSelectedArgumentChanged(WorkflowStepArgumentEditorItem? value) => OnPropertyChanged(nameof(CanInsertPlaceholder));

    partial void OnOnErrorChanged(WorkflowErrorPolicy value) => OnPropertyChanged(nameof(IsRetrySelected));
    partial void OnOverrideErrorPolicyChanged(bool value) => OnPropertyChanged(nameof(IsRetrySelected));
    partial void OnToolAliasesChanged(string value)
    {
        if (!_synchronizingPromptTools)
        {
            _synchronizingPromptTools = true;
            var aliases = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in PromptTools) item.IsSelected = aliases.Contains(item.Tool.Alias);
            _synchronizingPromptTools = false;
        }
        OnPropertyChanged(nameof(ShowKnowledgeTags));
        RefreshAuthorizationProposals();
    }
    partial void OnKnowledgeTagsChanged(string value)
    {
        var tags = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in KnowledgeTagSelections) item.IsSelected = tags.Contains(item.Tag);
    }

    partial void OnOverrideTemperatureChanged(bool value) => OverrideGeneration = OverrideTemperature || OverrideTopP || OverrideReasoning;
    partial void OnOverrideTopPChanged(bool value) => OverrideGeneration = OverrideTemperature || OverrideTopP || OverrideReasoning;
    partial void OnOverrideReasoningChanged(bool value) => OverrideGeneration = OverrideTemperature || OverrideTopP || OverrideReasoning;

    [RelayCommand]
    private void Apply()
    {
        if (_request is null || SelectedTool is null)
        {
            StatusText = "Select a workflow tool.";
            return;
        }
        try
        {
            var replacement = CreateStep();
            if (IsEdit && !AuthorizationProposals.Any(static item => item.IsAccepted) &&
                string.Equals(WorkflowDefinitionSerializer.Serialize([replacement]), WorkflowDefinitionSerializer.Serialize([_workflow[_location!.Index]]), StringComparison.Ordinal))
            {
                Result = null;
                IsApplyCloseRequested = true;
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            WorkflowStepMutationResult mutation = IsEdit
                ? WorkflowStepEditingService.Replace(_request.WorkflowText, _location!.Index, _location.Alias, replacement)
                : WorkflowStepEditingService.Insert(_request.WorkflowText, _location?.Index, _request.Mode == WorkflowStepEditorMode.InsertBefore ? WorkflowStepInsertionPosition.Before : WorkflowStepInsertionPosition.After, replacement);
            var warnings = ExecutionWarnings.Concat(AuthorizationProposals.Where(static item => !item.IsAccepted).Select(static item => $"Missing {item.Kind.ToLowerInvariant()} authorization: {item.Key}.")).Distinct(StringComparer.Ordinal).ToArray();
            ValidateProvisionalTask(mutation.Text);
            var acceptedNames = AuthorizationProposals.Where(static item => item.IsAccepted).Select(static item => $"{item.Kind}: {item.Key}").ToArray();
            var operationSummary = $"Workflow step {(IsEdit ? "updated" : "inserted")} at #{mutation.AffectedIndex + 1} — {mutation.Alias} ({mutation.EffectiveId})." +
                (acceptedNames.Length == 0 ? string.Empty : $" Accepted: {string.Join(", ", acceptedNames)}.") +
                (warnings.Length == 0 ? string.Empty : $" {warnings.Length} execution warning(s).") +
                " Save the task to persist this change.";
            Result = new WorkflowStepEditorResult(
                mutation.Text,
                mutation.CaretIndex,
                AuthorizationProposals.Where(static item => item.IsAccepted && item.Kind == "Tool group").Select(static item => item.Key).ToArray(),
                AuthorizationProposals.Where(static item => item.IsAccepted && item.Kind == "Capability").Select(item => Enum.Parse<TaskCapability>(item.Key)).ToArray(),
                AuthorizationProposals.Where(static item => item.IsAccepted && item.Kind == "Path").Select(item => _pathProposals[item.Key]).ToArray(),
                operationSummary)
            {
                AffectedIndex = mutation.AffectedIndex,
                Operation = IsEdit ? "Edit" : _request.Mode.ToString(),
                Alias = mutation.Alias,
                EffectiveId = mutation.EffectiveId,
                Warnings = warnings,
                CanonicalFormattingApplied = mutation.CanonicalFormattingApplied,
            };
            IsApplyCloseRequested = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (WorkflowValidationException exception) { StatusText = exception.Message; }
        catch (FormatException exception) { StatusText = exception.Message; }
    }

    [RelayCommand]
    private void Cancel() { Result = null; IsApplyCloseRequested = false; CloseRequested?.Invoke(this, EventArgs.Empty); }

    [RelayCommand(CanExecute = nameof(CanInsertPlaceholder))]
    private void InsertPlaceholder()
    {
        if (SelectedArgument is null || SelectedPlaceholder is null) return;
        var start = Math.Clamp(SelectedArgument.SelectionStart, 0, SelectedArgument.Value.Length);
        var length = Math.Clamp(SelectedArgument.SelectionLength, 0, SelectedArgument.Value.Length - start);
        SelectedArgument.Value = string.Concat(SelectedArgument.Value.AsSpan(0, start), SelectedPlaceholder.InsertText, SelectedArgument.Value.AsSpan(start + length));
        SelectedArgument.SelectionStart = start + SelectedPlaceholder.InsertText.Length;
        SelectedArgument.SelectionLength = 0;
    }

    private void Rebuild(ToolDefinition? tool, WorkflowStepDefinition? step)
    {
        if (tool is null) return;
        Arguments.Clear();
        AuthorizationProposals.Clear();
        ExecutionWarnings.Clear();
        Id = step?.EffectiveId ?? WorkflowSnippetBuilder.CreateUniqueId(tool.Alias, _workflow);
        OverrideErrorPolicy = step?.OnError is not null;
        OnError = step?.OnError ?? _request!.ErrorPolicy;
        RetryCount = step?.RetryCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        RetryBackoffSeconds = step?.RetryBackoffSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        Model = step?.Model ?? string.Empty;
        InheritModel = step?.Model is null;
        ToolAliases = string.Join(", ", step?.ToolAliases ?? []);
        KnowledgeTags = string.Join(", ", step?.KnowledgeAccess?.AllowedTags ?? []);
        NarrowKnowledge = step?.KnowledgeAccess is not null;
        OverrideTemperature = step?.GenerationOptions?.Temperature is not null;
        OverrideTopP = step?.GenerationOptions?.TopP is not null;
        OverrideReasoning = step?.GenerationOptions?.ReasoningEnabled is not null;
        OverrideGeneration = OverrideTemperature || OverrideTopP || OverrideReasoning;
        Temperature = step?.GenerationOptions?.Temperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        TopP = step?.GenerationOptions?.TopP?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ReasoningEnabled = step?.GenerationOptions?.ReasoningEnabled;
        foreach (var definition in tool.ArgumentDefinitions)
        {
            var value = default(JsonElement);
            var present = step is not null && TryGetValue(step.Args, definition.Name, out value);
            var item = new WorkflowStepArgumentEditorItem(definition, present || definition.IsRequired || (string.Equals(tool.Alias, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) && string.Equals(definition.Name, "includeContext", StringComparison.OrdinalIgnoreCase)), present ? value.GetRawText() : InitialValue(tool, definition), _request!.KnowledgeTags);
            item.PropertyChanged += (_, _) => { if (!_initializing) { HasChanges = true; RefreshAuthorizationProposals(); } };
            Arguments.Add(item);
        }
        if (!tool.IsAiStep && !_request!.ToolGroups.Contains(tool.ToolGroup, StringComparer.OrdinalIgnoreCase))
        {
            AuthorizationProposals.Add(new WorkflowProposalItem("Tool group", tool.ToolGroup, $"Allow the '{tool.ToolGroup}' tool group for this task."));
        }
        if (tool.RequiredCapability is { } capability && !_request!.Capabilities.Contains(capability))
        {
            AuthorizationProposals.Add(new WorkflowProposalItem("Capability", capability.ToString(), $"Grant sensitive capability '{capability}' for this task."));
        }
        RefreshAuthorizationProposals();
        OnPropertyChanged(nameof(IsEdit));
        OnPropertyChanged(nameof(IsInsert));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ApplyLabel));
    }

    private WorkflowStepDefinition CreateStep()
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        string? firstError = null;
        foreach (var argument in Arguments)
        {
            argument.ValidationError = string.Empty;
            if (!argument.IsIncluded) continue;
            try
            {
                properties.Add(argument.Definition.Name, argument.Parse());
            }
            catch (Exception exception) when (exception is FormatException or JsonException)
            {
                argument.ValidationError = exception.Message;
                firstError ??= exception.Message;
            }
        }
        if (firstError is not null) throw new FormatException(firstError);
        var id = Id.Trim();
        if (string.IsNullOrWhiteSpace(id) || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9._-]+$")) throw new WorkflowValidationException("Workflow step id contains unsupported characters.");
        var retry = IsRetrySelected ? ParseNonNegative(RetryCount, "Retry count") : null;
        var backoff = IsRetrySelected ? ParseNonNegative(RetryBackoffSeconds, "Retry backoff") : null;
        var original = IsEdit ? _workflow[_location!.Index] : null;
        var isPrompt = string.Equals(SelectedTool!.Alias, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase);
        var aliases = isPrompt ? ParseAliases() : original?.ToolAliases ?? [];
        var knowledge = isPrompt ? ParseKnowledge(aliases) : original?.KnowledgeAccess;
        var persistedId = IsEdit && original is not null && original.Id is null && string.Equals(id, original.EffectiveId, StringComparison.Ordinal) ? null : id;
        var persistedArguments = properties.Count == 0 && original?.Args.ValueKind == JsonValueKind.Undefined
            ? default
            : JsonSerializer.SerializeToElement(properties);
        return new WorkflowStepDefinition
        {
            Step = SelectedTool!.Alias,
            Id = persistedId,
            Args = persistedArguments,
            OnError = OverrideErrorPolicy ? OnError : null,
            RetryCount = retry,
            RetryBackoffSeconds = backoff,
            GenerationOptions = SelectedTool.IsAiStep ? CreateGenerationOptions(original?.GenerationOptions) : null,
            Model = isPrompt ? ParseModel() : original?.Model,
            ToolAliases = aliases,
            KnowledgeAccess = knowledge,
            IsStringForm = original?.IsStringForm == true,
        };
    }
    private static int? ParseNonNegative(string text, string label) => string.IsNullOrWhiteSpace(text) ? null : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : throw new FormatException($"{label} must be a non-negative integer.");
    private IReadOnlyList<string> ParseAliases()
    {
        var aliases = PromptTools.Where(static item => item.IsSelected).Select(static item => item.Tool.Alias).ToArray();
        foreach (var alias in aliases)
        {
            var tool = _catalog.Find(alias);
            if (tool is null || !tool.IsAgentTool) throw new WorkflowValidationException($"Prompt-step alias '{alias}' is not an agent-callable tool.");
        }
        return aliases;
    }
    private KnowledgeAccessDefinition? ParseKnowledge(IReadOnlyList<string> aliases)
    {
        if (!NarrowKnowledge) return null;
        var tags = KnowledgeTagSelections.Where(static item => item.IsSelected).Select(static item => item.Tag).ToArray();
        if (tags.Length == 0) throw new WorkflowValidationException("Select at least one authorized Knowledge tag when narrowing scope.");
        if (!aliases.Contains("knowledge.search", StringComparer.OrdinalIgnoreCase)) throw new WorkflowValidationException("Prompt Knowledge tags require alias 'knowledge.search'.");
        if (tags.Any(tag => !_request!.KnowledgeTags.Contains(tag, StringComparer.OrdinalIgnoreCase))) throw new WorkflowValidationException("Prompt Knowledge tags can only narrow the task's authorized tags.");
        return new KnowledgeAccessDefinition { AllowedTags = tags };
    }
    private GenerationOptions? CreateGenerationOptions(GenerationOptions? original)
    {
        double? temperature = OverrideTemperature ? ParseFinite(Temperature, "Temperature") : null;
        double? topP = OverrideTopP ? ParseFinite(TopP, "Top P") : null;
        var options = new GenerationOptions { Temperature = temperature, TopP = topP, ReasoningEnabled = OverrideReasoning ? ReasoningEnabled : null };
        if (GenerationPolicyValidation.ValidateOverrides(options) is { } error)
        {
            throw new FormatException(error);
        }
        return options.IsEmpty ? null : options;
    }
    private static double? ParseFinite(string text, string label) => string.IsNullOrWhiteSpace(text) ? null : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : throw new FormatException($"{label} must be a finite invariant number.");
    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private string? ParseModel()
    {
        if (InheritModel) return null;
        var model = NullIfWhiteSpace(Model);
        if (model is null) throw new WorkflowValidationException("An explicit prompt model cannot be blank.");
        return model;
    }
    private static bool TryGetValue(JsonElement args, string name, out JsonElement value) { if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out value)) return true; value = default; return false; }
    private static string InitialValue(ToolDefinition tool, ToolArgumentDefinition argument)
    {
        if (string.Equals(tool.Alias, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) && string.Equals(argument.Name, "prompt", StringComparison.OrdinalIgnoreCase)) return WorkflowSnippetBuilder.DefaultPromptText;
        if (string.Equals(tool.Alias, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) && string.Equals(argument.Name, "includeContext", StringComparison.OrdinalIgnoreCase)) return "true";
        return argument.DefaultValue?.GetRawText() ?? (argument.Kind == WorkflowArgumentKind.Boolean ? "false" : argument.Kind == WorkflowArgumentKind.Object ? "{}" : string.Empty);
    }

    private void RefreshAuthorizationProposals()
    {
        if (_initializing || _request is null || SelectedTool is null) return;
        var accepted = AuthorizationProposals.Where(static item => item.IsAccepted).Select(static item => (item.Kind, item.Key)).ToHashSet();
        AuthorizationProposals.Clear();
        ExecutionWarnings.Clear();
        _pathProposals.Clear();
        try
        {
            var step = CreateStep();
            var candidate = _workflow.ToList();
            if (IsEdit)
            {
                candidate[_location!.Index] = step;
            }
            else
            {
                var index = _location is null ? 0 : _location.Index + (_request.Mode == WorkflowStepEditorMode.InsertAfter ? 1 : 0);
                candidate.Insert(index, step);
            }
            var analysis = WorkflowRequirementAnalyzer.Analyze(candidate, _request.ToolGroups, _request.FileSystemAccess, _request.Capabilities);
            foreach (var warning in analysis.Warnings) ExecutionWarnings.Add(warning);
            foreach (var group in analysis.MissingToolGroups)
            {
                AuthorizationProposals.Add(new WorkflowProposalItem("Tool group", group, $"Allow the '{group}' tool group for this task.") { IsAccepted = accepted.Contains(("Tool group", group)) });
            }
            foreach (var capability in analysis.CapabilityRequirements.Where(static item => !item.IsGranted))
            {
                var key = capability.Capability.ToString();
                AuthorizationProposals.Add(new WorkflowProposalItem("Capability", key, $"Grant sensitive capability '{key}' for this task.") { IsAccepted = accepted.Contains(("Capability", key)) });
            }
            foreach (var path in analysis.MissingPathRequirements)
            {
                var key = path.DirectoryPath;
                _pathProposals[key] = new FileSystemAccessRule { Path = path.DirectoryPath, Access = path.RequiredAccess };
                var scopeWarnings = string.Join(" ", new[]
                {
                    path.IsBroaderScope ? "The inferred directory may be broader than the selected target." : null,
                    path.IsVolumeRoot ? "This grants access to an entire volume root." : null,
                    path.IsUnc ? "This is a UNC/network path." : null,
                }.Where(static value => value is not null));
                AuthorizationProposals.Add(new WorkflowProposalItem("Path", key, $"Allow {path.RequiredAccess} access to '{path.DirectoryPath}' for {string.Join(", ", path.SourceAliases)}. {scopeWarnings}".Trim()) { IsAccepted = accepted.Contains(("Path", key)) });
            }
        }
        catch (Exception exception) when (exception is WorkflowValidationException or FormatException) { }
    }

    private void RefreshToolFilter()
    {
        var search = ToolSearchText.Trim();
        FilteredTools.Clear();
        foreach (var tool in Tools.Where(tool => string.IsNullOrEmpty(search) || tool.Alias.Contains(search, StringComparison.OrdinalIgnoreCase) || tool.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredTools.Add(tool);
        }
    }

    private void BuildPromptToolChoices()
    {
        foreach (var item in PromptTools) item.PropertyChanged -= OnPromptToolChanged;
        PromptTools.Clear();
        foreach (var tool in _catalog.Definitions.Where(static tool => tool.IsAgentTool))
        {
            var item = new WorkflowPromptToolSelectionItem(tool);
            item.PropertyChanged += OnPromptToolChanged;
            PromptTools.Add(item);
        }
    }

    private void OnPromptToolChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (_initializing || _synchronizingPromptTools || eventArgs.PropertyName != nameof(WorkflowPromptToolSelectionItem.IsSelected)) return;
        if (sender is WorkflowPromptToolSelectionItem knowledge && string.Equals(knowledge.Tool.Alias, "knowledge.search", StringComparison.OrdinalIgnoreCase) && !knowledge.IsSelected && NarrowKnowledge)
        {
            if (_desktopInteraction is not null && !_desktopInteraction.Confirm("Clear Knowledge narrowing", "Removing knowledge.search also clears this step's Knowledge tag narrowing. Continue?"))
            {
                _synchronizingPromptTools = true;
                knowledge.IsSelected = true;
                _synchronizingPromptTools = false;
                return;
            }
            NarrowKnowledge = false;
            foreach (var tag in KnowledgeTagSelections) tag.IsSelected = false;
            KnowledgeTags = string.Empty;
        }
        _synchronizingPromptTools = true;
        ToolAliases = string.Join(", ", PromptTools.Where(static item => item.IsSelected).Select(static item => item.Tool.Alias));
        _synchronizingPromptTools = false;
        HasChanges = true;
        OnPropertyChanged(nameof(ShowKnowledgeTags));
        RefreshAuthorizationProposals();
    }

    private void BuildKnowledgeTagChoices()
    {
        foreach (var item in KnowledgeTagSelections) item.PropertyChanged -= OnKnowledgeTagChanged;
        KnowledgeTagSelections.Clear();
        var available = _request!.KnowledgeTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = IsEdit && _location is not null ? _workflow[_location.Index].KnowledgeAccess?.AllowedTags ?? [] : [];
        foreach (var tag in _request.KnowledgeTags.Concat(existing).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase))
        {
            var item = new WorkflowKnowledgeTagSelectionItem(tag, available.Contains(tag));
            item.PropertyChanged += OnKnowledgeTagChanged;
            KnowledgeTagSelections.Add(item);
        }
    }

    private void OnKnowledgeTagChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (_initializing || eventArgs.PropertyName != nameof(WorkflowKnowledgeTagSelectionItem.IsSelected)) return;
        KnowledgeTags = string.Join(", ", KnowledgeTagSelections.Where(static item => item.IsSelected).Select(static item => item.Tag));
        HasChanges = true;
    }

    private void ValidateProvisionalTask(string workflowText)
    {
        if (_request?.DraftTask is null) return;
        var acceptedGroups = _request.ToolGroups.Concat(AuthorizationProposals.Where(static item => item.IsAccepted && item.Kind == "Tool group").Select(static item => item.Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var acceptedCapabilities = _request.Capabilities.Concat(AuthorizationProposals.Where(static item => item.IsAccepted && item.Kind == "Capability").Select(static item => Enum.Parse<TaskCapability>(item.Key))).Distinct().ToArray();
        var acceptedPaths = _request.FileSystemAccess.Concat(AuthorizationProposals.Where(static item => item.IsAccepted && item.Kind == "Path").Select(item => _pathProposals[item.Key])).ToArray();
        var provisional = _request.DraftTask with
        {
            Workflow = WorkflowDefinitionSerializer.Deserialize(workflowText),
            Tools = acceptedGroups,
            Capabilities = acceptedCapabilities,
            FileSystemAccess = acceptedPaths,
        };
        try
        {
            ScheduledTaskValidator.Validate(provisional);
        }
        catch (Exception exception) when (
            exception is TaskValidationException or WorkflowValidationException &&
            AuthorizationProposals.Any(static item => !item.IsAccepted) &&
            exception.Message.Contains("requires", StringComparison.OrdinalIgnoreCase))
        {
            // Missing opt-in authorizations remain explicit execution warnings; structural Apply is allowed.
        }
    }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (_initializing || eventArgs.PropertyName is nameof(HasChanges) or nameof(StatusText) or nameof(ToolSearchText) or nameof(SelectedArgument) or nameof(SelectedPlaceholder))
        {
            return;
        }
        HasChanges = true;
    }
}

/// <summary>Represents one generated typed argument editor.</summary>
public sealed partial class WorkflowStepArgumentEditorItem : ObservableObject
{
    /// <summary>Initializes an argument editor.</summary>
    public WorkflowStepArgumentEditorItem(ToolArgumentDefinition definition, bool isIncluded, string value, IReadOnlyList<string>? taskKnowledgeTags = null)
    {
        Definition = definition;
        IsIncluded = isIncluded;
        Value = value;
        if (definition.EditorHint == WorkflowArgumentEditorHint.KnowledgeTags)
        {
            var selected = ParseSelectedTags(value);
            var available = (taskKnowledgeTags ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in available.Concat(selected).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase))
            {
                var item = new WorkflowKnowledgeTagSelectionItem(tag, available.Contains(tag)) { IsSelected = selected.Contains(tag) };
                item.PropertyChanged += OnKnowledgeTagSelectionChanged;
                KnowledgeTagChoices.Add(item);
            }
        }
    }
    /// <summary>Gets Tool Map metadata.</summary>
    public ToolArgumentDefinition Definition { get; }
    /// <summary>Gets task-bounded choices for a Knowledge-tag editor hint.</summary>
    public ObservableCollection<WorkflowKnowledgeTagSelectionItem> KnowledgeTagChoices { get; } = [];
    /// <summary>Gets the deterministic editor mapping selected from Core metadata.</summary>
    public WorkflowArgumentEditorKind EditorKind => Definition.EditorHint == WorkflowArgumentEditorHint.KnowledgeTags
        ? WorkflowArgumentEditorKind.KnowledgeTags
        : Definition.Kind switch
        {
            WorkflowArgumentKind.Boolean => WorkflowArgumentEditorKind.Boolean,
            WorkflowArgumentKind.Enumeration => WorkflowArgumentEditorKind.Enumeration,
            WorkflowArgumentKind.Object => WorkflowArgumentEditorKind.StructuredJson,
            WorkflowArgumentKind.String when Definition.AllowedValues.Count > 0 => WorkflowArgumentEditorKind.Enumeration,
            WorkflowArgumentKind.String when Definition.IsLongText => WorkflowArgumentEditorKind.MultilineString,
            WorkflowArgumentKind.Integer => WorkflowArgumentEditorKind.Integer,
            WorkflowArgumentKind.Number => WorkflowArgumentEditorKind.Number,
            _ => WorkflowArgumentEditorKind.String,
        };
    /// <summary>Gets whether the argument can be omitted from the generated JSON object.</summary>
    public bool CanExclude => !Definition.IsRequired;
    /// <summary>Gets whether the optional argument is emitted.</summary>
    [ObservableProperty] private bool _isIncluded;
    /// <summary>Gets the invariant text representation of the typed value.</summary>
    [ObservableProperty] private string _value;
    /// <summary>Gets the field-associated validation error announced by the dialog.</summary>
    [ObservableProperty] private string _validationError = string.Empty;
    /// <summary>Gets or sets the UTF-16 selection start used for placeholder replacement.</summary>
    [ObservableProperty] private int _selectionStart;
    /// <summary>Gets or sets the UTF-16 selection length used for placeholder replacement.</summary>
    [ObservableProperty] private int _selectionLength;
    /// <summary>Gets or sets the Boolean typed value without a string-to-CheckBox binding.</summary>
    public bool BooleanValue
    {
        get => bool.TryParse(Value, out var value) && value;
        set => Value = value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
    }

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(BooleanValue));

    private void OnKnowledgeTagSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(WorkflowKnowledgeTagSelectionItem.IsSelected)) return;
        Value = JsonSerializer.Serialize(KnowledgeTagChoices.Where(static item => item.IsSelected && item.IsAvailable).Select(static item => item.Tag).ToArray());
    }

    private static IReadOnlySet<string> ParseSelectedTags(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.String).Select(static item => item.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
    /// <summary>Parses the current editor value into JSON.</summary>
    public JsonElement Parse()
    {
        if (Definition.AllowsNull && string.Equals(Value.Trim(), "null", StringComparison.Ordinal)) return JsonSerializer.SerializeToElement<object?>(null);
        if (Definition.AllowedValues.Count > 0 && !Definition.AllowedValues.Contains(Value, StringComparer.Ordinal)) throw new FormatException($"{Definition.Name} must be one of: {string.Join(", ", Definition.AllowedValues)}.");
        return Definition.Kind switch
    {
        WorkflowArgumentKind.String or WorkflowArgumentKind.Enumeration => JsonSerializer.SerializeToElement(Value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal)),
        WorkflowArgumentKind.Boolean when bool.TryParse(Value, out var flag) => JsonSerializer.SerializeToElement(flag),
        WorkflowArgumentKind.Integer => ParseInteger(),
        WorkflowArgumentKind.Number when double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) => JsonSerializer.SerializeToElement(ValidateRange(number)),
        WorkflowArgumentKind.Object => ParseStructuredJson(),
        _ => throw new FormatException($"{Definition.Name} has an invalid {Definition.Kind} value."),
    };
    }

    private JsonElement ParseInteger()
    {
        if (Definition.ValueType == typeof(ulong) && ulong.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned))
        {
            ValidateRange(unsigned);
            return JsonSerializer.SerializeToElement(unsigned);
        }
        if (!long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) throw new FormatException($"{Definition.Name} has an invalid integer value.");
        ValidateRange(integer);
        return JsonSerializer.SerializeToElement(integer);
    }

    private double ValidateRange(double value)
    {
        if (Definition.Minimum is { } minimum && value < minimum || Definition.Maximum is { } maximum && value > maximum)
        {
            throw new FormatException($"{Definition.Name} must be between {Definition.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "-∞"} and {Definition.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "∞"}.");
        }
        return value;
    }

    private JsonElement ParseStructuredJson()
    {
        using var document = JsonDocument.Parse(Value);
        var expectedArray = Definition.ValueType.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(Definition.ValueType) && Definition.ValueType != typeof(string) && Definition.ValueType != typeof(JsonElement);
        if (expectedArray && document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"{Definition.Name} must be a JSON array.");
        }
        if (!expectedArray && Definition.ValueType != typeof(JsonElement) && Definition.ValueType != typeof(string) && document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{Definition.Name} must be a JSON object.");
        }
        if ((Definition.ValueType == typeof(JsonElement) || Definition.ValueType == typeof(string)) && document.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
        {
            throw new FormatException($"{Definition.Name} must be a JSON object or array.");
        }
        return document.RootElement.Clone();
    }
}

/// <summary>Defines WPF-independent generated editor kinds.</summary>
public enum WorkflowArgumentEditorKind
{
    /// <summary>Single-line string.</summary>
    String,
    /// <summary>Multiline normalized string.</summary>
    MultilineString,
    /// <summary>Invariant integer.</summary>
    Integer,
    /// <summary>Invariant finite number.</summary>
    Number,
    /// <summary>Boolean checkbox.</summary>
    Boolean,
    /// <summary>Constrained value selection.</summary>
    Enumeration,
    /// <summary>Structured JSON object or array.</summary>
    StructuredJson,
    /// <summary>Task-bounded Knowledge tag selection.</summary>
    KnowledgeTags,
}

/// <summary>Represents one exact agent-callable prompt tool choice.</summary>
public sealed partial class WorkflowPromptToolSelectionItem(ToolDefinition tool) : ObservableObject
{
    /// <summary>Gets immutable Tool Map metadata.</summary>
    public ToolDefinition Tool { get; } = tool;
    /// <summary>Gets content-free permission and path behavior help.</summary>
    public string Details => $"Group: {Tool.ToolGroup}. Capability: {Tool.RequiredCapability?.ToString() ?? "none"}. {(Tool.LocalPathArguments.Count > 0 ? "Model-generated local paths remain runtime-authorized." : "No local-path argument.")}";
    /// <summary>Gets or sets whether the exact alias is emitted.</summary>
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Represents one task-bounded Knowledge tag choice.</summary>
public sealed partial class WorkflowKnowledgeTagSelectionItem(string tag, bool isAvailable) : ObservableObject
{
    /// <summary>Gets the normalized task tag.</summary>
    public string Tag { get; } = tag;
    /// <summary>Gets whether the tag remains authorized by the task.</summary>
    public bool IsAvailable { get; } = isAvailable;
    /// <summary>Gets an accessible label that marks stale repair entries.</summary>
    public string DisplayLabel => IsAvailable ? Tag : $"{Tag} (unavailable — deselect to repair)";
    /// <summary>Gets or sets whether the tag narrows this step.</summary>
    [ObservableProperty] private bool _isSelected;
}
