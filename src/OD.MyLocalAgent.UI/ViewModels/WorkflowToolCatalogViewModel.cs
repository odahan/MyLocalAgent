using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Coordinates the deterministic, memory-only Tool catalog session.
/// </summary>
public sealed partial class WorkflowToolCatalogViewModel : ObservableObject
{
    private static readonly string[] GroupOrder =
    [
        ToolGroups.Gmail,
        ToolGroups.Calendar,
        ToolGroups.Dropbox,
        ToolGroups.FileSystem,
        ToolGroups.Http,
        ToolGroups.Web,
        ToolGroups.Ai,
    ];

    private readonly List<WorkflowToolCatalogInsertion> _insertions = [];
    private readonly List<string> _warnings = [];
    private readonly HashSet<string> _acceptedGroups =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IDesktopInteractionService _desktopInteraction;

    private WorkflowToolCatalogRequest? _request;
    private string _workingText = string.Empty;
    private int _workingCaret;
    private bool _canonicalFormattingApplied;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowToolCatalogViewModel"/> class.
    /// </summary>
    /// <param name="desktopInteraction">The desktop interaction service used by explicit copy.</param>
    public WorkflowToolCatalogViewModel(IDesktopInteractionService desktopInteraction)
    {
        ArgumentNullException.ThrowIfNull(desktopInteraction);
        _desktopInteraction = desktopInteraction;
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InsertCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertAndCloseCommand))]
    private WorkflowToolCatalogItem? _selectedItem;

    [ObservableProperty]
    private bool _includeOptionalArguments;

    [ObservableProperty]
    private bool _includeErrorPolicy;

    [ObservableProperty]
    private bool _includeGenerationOptions;

    [ObservableProperty]
    private bool _includeAccumulatedContext = true;

    [ObservableProperty]
    private bool _enableMissingToolGroup;

    [ObservableProperty]
    private string _snippetPreview = string.Empty;

    [ObservableProperty]
    private string _statusText =
        "Select a workflow step. Insert accumulates without closing the dialog.";

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Occurs when the dialog must be closed by its host window.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets the catalog entries in fixed group order.
    /// </summary>
    public ObservableCollection<WorkflowToolCatalogItem> Items { get; } = [];

    /// <summary>
    /// Gets the grouped, filtered catalog view bound by the dialog list.
    /// </summary>
    public ICollectionView ItemsView { get; private set; } = null!;

    /// <summary>
    /// Gets the shared placeholder templates offered for explicit clipboard copy.
    /// </summary>
    public IReadOnlyList<WorkflowPlaceholderDefinition> Placeholders { get; } =
        WorkflowPlaceholderCatalog.Entries;

    /// <summary>
    /// Gets a value indicating whether the Steps tab is active.
    /// </summary>
    /// <remarks>
    /// Insertion only applies to workflow steps. The Placeholders tab exposes explicit clipboard
    /// copy instead, so the insertion commands are hidden there and Close becomes the primary
    /// dismissal.
    /// </remarks>
    public bool IsStepsTabSelected => SelectedTabIndex == 0;

    /// <summary>
    /// Gets the ordered argument rows of the selected entry.
    /// </summary>
    public ObservableCollection<WorkflowToolArgumentRow> SelectedArguments { get; } = [];

    /// <summary>
    /// Gets the task authorization rules shown as read-only context.
    /// </summary>
    public ObservableCollection<FileSystemAccessRule> AuthorizationContext { get; } = [];

    /// <summary>
    /// Gets the accumulated session result, or <see langword="null"/> when nothing changed.
    /// </summary>
    public WorkflowToolCatalogResult? Result { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the working copy differs from the request.
    /// </summary>
    public bool HasEffectiveChanges =>
        _insertions.Count > 0 || _acceptedGroups.Count > 0;

    /// <summary>
    /// Gets the agent function name or the workflow-only label of the selected entry.
    /// </summary>
    public string SelectedFunctionName =>
        SelectedItem is null
            ? string.Empty
            : SelectedItem.Tool.AgentFunctionName ?? "workflow-only AI step";

    /// <summary>
    /// Gets the Safety snapshot line of the selected entry.
    /// </summary>
    public string SelectedSafetyText
    {
        get
        {
            if (SelectedItem?.Tool.SafetyFlag is not { } flag || _request is null)
            {
                return "No Safety gate is required.";
            }

            return _request.Safety.IsEnabled(flag)
                ? $"{flag} is enabled."
                : $"{flag} is disabled; execution will fail until it is enabled in Settings.";
        }
    }

    /// <summary>
    /// Gets the task whitelist status line of the selected entry.
    /// </summary>
    public string SelectedWhitelistText
    {
        get
        {
            if (SelectedItem is null)
            {
                return string.Empty;
            }

            if (SelectedItem.IsBuiltInAi)
            {
                return "Built-in AI steps require no task tool-group permission.";
            }

            return SelectedItem.IsWhitelisted
                ? $"The task already allows the '{SelectedItem.ToolGroup}' tool group."
                : $"The task does not allow the '{SelectedItem.ToolGroup}' tool group yet.";
        }
    }

    /// <summary>
    /// Gets a value indicating whether the missing-tool-group opt-in must be shown.
    /// </summary>
    public bool ShowsToolGroupOptIn => SelectedItem?.ShowsMissingWhitelistLock == true;

    /// <summary>
    /// Gets a value indicating whether the generation-options toggle applies.
    /// </summary>
    public bool IsGenerationOptionsEnabled => SelectedItem?.IsBuiltInAi == true;

    /// <summary>
    /// Gets a value indicating whether the accumulated-context toggle applies.
    /// </summary>
    public bool IsAccumulatedContextEnabled =>
        SelectedItem is not null &&
        string.Equals(
            SelectedItem.Alias,
            ToolMap.PromptAlias,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the static local authorization requirements of the selected entry.
    /// </summary>
    public string SelectedLocalPathText =>
        SelectedItem is null || SelectedItem.Tool.LocalPathArguments.Count == 0
            ? "This step does not access the local filesystem."
            : string.Join(
                Environment.NewLine,
                SelectedItem.Tool.LocalPathArguments.Select(
                    static argument =>
                        $"'{argument.ArgumentName}' needs {argument.RequiredAccess} access to its {argument.TargetKind}."));

    /// <summary>
    /// Initializes the session from the current task draft snapshot.
    /// </summary>
    /// <param name="request">The task draft snapshot.</param>
    public void Initialize(WorkflowToolCatalogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        _workingText = request.WorkflowText;
        _workingCaret = WorkflowCaretInsertionService.ClampCaret(
            request.WorkflowText,
            request.CaretIndex);

        var granted = new HashSet<string>(request.ToolGroups, StringComparer.OrdinalIgnoreCase);
        Items.Clear();
        foreach (var tool in ToolMap.Definitions
                     .OrderBy(static tool => Array.IndexOf(GroupOrder, tool.ToolGroup))
                     .ThenBy(static tool => tool.Alias, StringComparer.OrdinalIgnoreCase))
        {
            Items.Add(
                new WorkflowToolCatalogItem(
                    tool,
                    tool.IsAiStep || granted.Contains(tool.ToolGroup)));
        }

        AuthorizationContext.Clear();
        foreach (var rule in request.FileSystemAccess)
        {
            AuthorizationContext.Add(rule);
        }

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WorkflowToolCatalogItem.ToolGroup)));
        ItemsView.Filter = item => item is WorkflowToolCatalogItem entry && entry.Matches(SearchText);
        OnPropertyChanged(nameof(ItemsView));
        SelectedItem = Items.FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value) => ItemsView?.Refresh();

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsStepsTabSelected));
        StatusText = IsStepsTabSelected
            ? "Select a workflow step. Insert accumulates without closing the dialog."
            : "Placeholders are copied to the clipboard with Copy. Insertion applies to steps only.";
    }

    partial void OnSelectedItemChanged(WorkflowToolCatalogItem? value)
    {
        SelectedArguments.Clear();
        if (value is not null)
        {
            foreach (var argument in value.Tool.ArgumentDefinitions)
            {
                SelectedArguments.Add(new WorkflowToolArgumentRow(argument));
            }
        }

        EnableMissingToolGroup = value is not null && _acceptedGroups.Contains(value.ToolGroup);
        if (!IsGenerationOptionsEnabled)
        {
            IncludeGenerationOptions = false;
        }

        OnPropertyChanged(nameof(SelectedFunctionName));
        OnPropertyChanged(nameof(SelectedSafetyText));
        OnPropertyChanged(nameof(SelectedWhitelistText));
        OnPropertyChanged(nameof(SelectedLocalPathText));
        OnPropertyChanged(nameof(ShowsToolGroupOptIn));
        OnPropertyChanged(nameof(IsGenerationOptionsEnabled));
        OnPropertyChanged(nameof(IsAccumulatedContextEnabled));
        UpdatePreview();
    }

    partial void OnIncludeOptionalArgumentsChanged(bool value) => UpdatePreview();

    partial void OnIncludeErrorPolicyChanged(bool value) => UpdatePreview();

    partial void OnIncludeGenerationOptionsChanged(bool value) => UpdatePreview();

    partial void OnIncludeAccumulatedContextChanged(bool value) => UpdatePreview();

    [RelayCommand(CanExecute = nameof(CanInsert))]
    private void Insert() => InsertCore();

    [RelayCommand(CanExecute = nameof(CanInsert))]
    private void InsertAndClose()
    {
        if (InsertCore())
        {
            Result = BuildResult();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Close()
    {
        Result = HasEffectiveChanges ? BuildResult() : null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Discards the complete working copy and closes the session.
    /// </summary>
    [RelayCommand]
    public void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CopyPlaceholder(WorkflowPlaceholderDefinition? placeholder)
    {
        if (placeholder is null)
        {
            return;
        }

        _desktopInteraction.CopyText(placeholder.InsertText);
        StatusText = $"Placeholder template '{placeholder.InsertText}' copied.";
    }

    private bool CanInsert() => SelectedItem is not null;

    private bool InsertCore()
    {
        if (SelectedItem is not { } item || _request is null)
        {
            return false;
        }

        try
        {
            var workflow = ParseWorkingWorkflow();
            var step = WorkflowSnippetBuilder.Create(
                item.Tool,
                new WorkflowSnippetOptions
                {
                    IncludeOptionalArguments = IncludeOptionalArguments,
                    IncludeErrorPolicy = IncludeErrorPolicy,
                    IncludeGenerationOptions = IncludeGenerationOptions && item.IsBuiltInAi,
                    IncludeAccumulatedContext = IncludeAccumulatedContext,
                },
                workflow,
                _request.ErrorPolicy,
                _request.Temperature,
                _request.TopP);

            var insertion = WorkflowCaretInsertionService.Insert(_workingText, _workingCaret, step);
            _workingText = insertion.Text;
            _workingCaret = insertion.CaretIndex;
            _canonicalFormattingApplied |= insertion.CanonicalFormattingApplied;
            _insertions.Add(
                new WorkflowToolCatalogInsertion(
                    item.Alias,
                    step.EffectiveId,
                    insertion.InsertedIndex,
                    !insertion.IsStructured));

            if (insertion.Warning is { } warning && !_warnings.Contains(warning))
            {
                _warnings.Add(warning);
            }

            if (!item.IsBuiltInAi && EnableMissingToolGroup)
            {
                _ = _acceptedGroups.Add(item.ToolGroup);
            }

            StatusText = insertion.IsStructured
                ? $"Step '{item.Alias}' inserted at position {insertion.InsertedIndex + 1}."
                : WorkflowCaretInsertionService.FallbackWarning;
            OnPropertyChanged(nameof(HasEffectiveChanges));
            return true;
        }
        catch (WorkflowValidationException exception)
        {
            StatusText = exception.Message;
            return false;
        }
    }

    private IReadOnlyList<WorkflowStepDefinition> ParseWorkingWorkflow()
    {
        if (string.IsNullOrWhiteSpace(_workingText))
        {
            return [];
        }

        try
        {
            return WorkflowDefinitionSerializer.Deserialize(_workingText);
        }
        catch (WorkflowValidationException)
        {
            return [];
        }
    }

    private WorkflowToolCatalogResult BuildResult()
    {
        var required = new HashSet<string>(
            _insertions
                .Select(static insertion => ToolMap.Find(insertion.Alias)?.ToolGroup)
                .Where(static group => group is not null)!,
            StringComparer.OrdinalIgnoreCase);

        return new WorkflowToolCatalogResult
        {
            WorkflowText = _workingText,
            CaretIndex = _workingCaret,
            AcceptedToolGroups = [.. _acceptedGroups.Where(required.Contains).Order(StringComparer.OrdinalIgnoreCase)],
            Insertions = [.. _insertions],
            Warnings = [.. _warnings],
            CanonicalFormattingApplied = _canonicalFormattingApplied,
        };
    }

    private void UpdatePreview()
    {
        if (SelectedItem is not { } item || _request is null)
        {
            SnippetPreview = string.Empty;
            return;
        }

        var step = WorkflowSnippetBuilder.Create(
            item.Tool,
            new WorkflowSnippetOptions
            {
                IncludeOptionalArguments = IncludeOptionalArguments,
                IncludeErrorPolicy = IncludeErrorPolicy,
                IncludeGenerationOptions = IncludeGenerationOptions && item.IsBuiltInAi,
                IncludeAccumulatedContext = IncludeAccumulatedContext,
            },
            ParseWorkingWorkflow(),
            _request.ErrorPolicy,
            _request.Temperature,
            _request.TopP);
        SnippetPreview = WorkflowDefinitionSerializer.Serialize([step]);
    }
}
