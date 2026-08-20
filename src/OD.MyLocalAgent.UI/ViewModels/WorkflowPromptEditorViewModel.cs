using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Coordinates the deterministic, memory-only Workflow Prompt Editor session.
/// </summary>
public sealed partial class WorkflowPromptEditorViewModel : ObservableObject
{
    private IReadOnlyList<WorkflowStepDefinition> _workflow = [];
    private readonly IToolCatalog? _toolCatalog;

    /// <summary>Initializes a prompt editor backed by the declarative Tool Map.</summary>
    public WorkflowPromptEditorViewModel(IToolCatalog? toolCatalog = null) => _toolCatalog = toolCatalog;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertStepCommand))]
    private WorkflowPromptEditorItem? _selectedItem;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _caretIndex;

    /// <summary>
    /// Occurs when the dialog must be closed by its host window.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets the editable prompt entries in declaration order.
    /// </summary>
    public ObservableCollection<WorkflowPromptEditorItem> Items { get; } = [];

    /// <summary>
    /// Gets the applied result, or <see langword="null"/> when the session was cancelled.
    /// </summary>
    public WorkflowPromptEditorResult? Result { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the workflow declares no <c>ai.prompt</c> step.
    /// </summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Gets a value indicating whether at least one entry carries an effective change.
    /// </summary>
    public bool HasChanges => Items.Any(item => item.IsModified);

    /// <summary>
    /// Gets a value indicating whether every entry holds a non-blank prompt.
    /// </summary>
    public bool IsValid => Items.All(item => !item.IsBlank);

    /// <summary>
    /// Loads the workflow snapshot into the session.
    /// </summary>
    /// <param name="request">The current workflow snapshot.</param>
    /// <exception cref="WorkflowValidationException">Thrown when the workflow is not parsable.</exception>
    public void Initialize(WorkflowPromptEditorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _workflow = WorkflowDefinitionSerializer.Deserialize(request.WorkflowText);

        var enabledGroups = new HashSet<string>(request.EnabledToolGroups ?? [],StringComparer.OrdinalIgnoreCase);
        var availableTools = (_toolCatalog?.Definitions ?? [])
            .Where(definition => definition.IsAgentTool && enabledGroups.Contains(definition.ToolGroup))
            .ToArray();

        Items.Clear();
        foreach (var entry in WorkflowPromptEditingService.Extract(_workflow))
        {
            var item = new WorkflowPromptEditorItem(entry,availableTools,request.KnowledgeTags ?? []);
            item.PropertyChanged += OnItemPropertyChanged;
            Items.Add(item);
        }

        SelectedItem = Items.FirstOrDefault();
        StatusText = IsEmpty
            ? "This workflow declares no long-text arguments."
            : "Edit workflow text arguments. Apply writes every change atomically.";

        OnPropertyChanged(nameof(IsEmpty));
        RaiseSessionState();
    }

    /// <summary>
    /// Determines whether the pending changes may be applied.
    /// </summary>
    /// <returns><see langword="true"/> when the session is applicable.</returns>
    public bool CanApply() => HasChanges && IsValid;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        var edits = Items
            .Where(item => item.IsModified)
            .Select(item => new WorkflowPromptEdit(item.Position, item.PromptText, item.IncludeContext)
            {
                ArgumentName = item.ArgumentName,
                Model = item.SupportsModelOverride ? item.ModelOverride : null,
                ToolAliases = item.ToolAliases,
                KnowledgeAccess = item.KnowledgeAccess,
            })
            .ToList();

        Result = new WorkflowPromptEditorResult
        {
            WorkflowText = WorkflowPromptEditingService.Apply(_workflow, edits),
            ModifiedPromptCount = Items.Count(item => item.IsPromptModified),
            ModifiedContextCount = Items.Count(item => item.IsContextModified),
            ModifiedSteps =
            [
                .. Items
                    .Where(item => item.IsModified)
                    .Select(item => new WorkflowPromptEditorChange(item.Position, item.EffectiveId)),
            ],
            Warnings =
            [
                .. Items
                    .Where(item => item.HasContextDuplication)
                    .Select(item =>
                        $"Prompt {item.DisplayPosition} already references prior output or accumulated context."),
            ],
        };

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanRevertStep() => SelectedItem?.IsModified == true;

    [RelayCommand(CanExecute = nameof(CanRevertStep))]
    private void RevertStep() => SelectedItem?.Revert();

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkflowPromptEditorItem.IsModified) or
            nameof(WorkflowPromptEditorItem.IsBlank))
        {
            RaiseSessionState();
        }
    }

    private void RaiseSessionState()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsValid));
        ApplyCommand.NotifyCanExecuteChanged();
        RevertStepCommand.NotifyCanExecuteChanged();
    }
}
