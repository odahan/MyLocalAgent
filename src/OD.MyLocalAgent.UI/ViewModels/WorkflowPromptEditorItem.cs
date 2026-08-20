using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Represents one editable <c>ai.prompt</c> occurrence inside the Workflow Prompt Editor.
/// </summary>
public sealed partial class WorkflowPromptEditorItem : ObservableObject
{
    private readonly WorkflowPromptEntry _entry;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowPromptEditorItem"/> class.
    /// </summary>
    /// <param name="entry">The extracted prompt entry.</param>
    /// <param name="availableTools">The catalog tools enabled by the owning task groups.</param>
    /// <param name="taskKnowledgeTags">The task Knowledge tags available for optional narrowing.</param>
    public WorkflowPromptEditorItem(
        WorkflowPromptEntry entry,
        IReadOnlyList<ToolDefinition>? availableTools = null,
        IReadOnlyList<string>? taskKnowledgeTags = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
        _promptText = entry.PromptText;
        _includeContext = entry.IncludeContext;
        _modelOverride = entry.Model ?? string.Empty;
        _toolAliasesText = string.Join(", ", entry.ToolAliases);
        _knowledgeTagsText = string.Join(", ", entry.KnowledgeAccess?.AllowedTags ?? []);
        TaskKnowledgeTags = taskKnowledgeTags ?? [];
        foreach (var definition in availableTools ?? [])
        {
            var selection = new WorkflowPromptToolSelection(definition, entry.ToolAliases.Contains(definition.Alias, StringComparer.OrdinalIgnoreCase));
            selection.PropertyChanged += OnToolSelectionChanged;
            ToolSelections.Add(selection);
        }
    }

    [ObservableProperty]
    private string _promptText;

    [ObservableProperty]
    private bool _includeContext;

    [ObservableProperty]
    private string _modelOverride;

    [ObservableProperty]
    private string _toolAliasesText;

    [ObservableProperty]
    private string _knowledgeTagsText;

    /// <summary>Gets catalog-driven tools whose groups are enabled by the owning task.</summary>
    public ObservableCollection<WorkflowPromptToolSelection> ToolSelections { get; } = [];

    /// <summary>Gets the task Knowledge tags that a prompt step may inherit or narrow.</summary>
    public IReadOnlyList<string> TaskKnowledgeTags { get; }

    /// <summary>Gets whether the declarative catalog offers tools for this prompt step.</summary>
    public bool HasToolChoices => SupportsModelOverride && ToolSelections.Count > 0;

    /// <summary>Gets whether the selected aliases include Knowledge search.</summary>
    public bool SelectsKnowledge => ToolAliases.Contains("knowledge.search", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the zero-based workflow position, which is the stable edit identity.
    /// </summary>
    public int Position => _entry.Position;

    /// <summary>
    /// Gets the one-based position shown in the list.
    /// </summary>
    public int DisplayPosition => _entry.DisplayPosition;

    /// <summary>
    /// Gets the effective step id.
    /// </summary>
    public string EffectiveId => _entry.EffectiveId;

    /// <summary>Gets the Tool Map alias.</summary>
    public string Alias => _entry.Alias;

    /// <summary>Gets the edited argument name.</summary>
    public string ArgumentName => _entry.ArgumentName;

    /// <summary>Gets whether includeContext is applicable.</summary>
    public bool SupportsIncludeContext => _entry.SupportsIncludeContext;

    /// <summary>Gets whether this item can override a model.</summary>
    public bool SupportsModelOverride => string.Equals(
        Alias,
        ToolMap.PromptAlias,
        StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets whether this prompt step inherits its model.</summary>
    public bool UsesInheritedModel => string.IsNullOrWhiteSpace(ModelOverride);

    /// <summary>Gets whether the local PowerShell warning is shown.</summary>
    public bool IsPowerShell => _entry.IsPowerShell;

    /// <summary>
    /// Gets the effective identifiers declared before this step.
    /// </summary>
    public IReadOnlyList<string> PriorStepIds => _entry.PriorStepIds;

    /// <summary>
    /// Gets a value indicating whether the source step had no usable <c>prompt</c> argument.
    /// </summary>
    public bool IsRepairCase => _entry.IsRepairCase;

    /// <summary>
    /// Gets a value indicating whether the original prompt contained carriage returns.
    /// </summary>
    public bool RequiresLineEndingNormalization => _entry.RequiresLineEndingNormalization;

    /// <summary>
    /// Gets the deterministic single-line preview of the current working text.
    /// </summary>
    public string Preview => WorkflowPromptPreviewProjection.Create(PromptText);

    /// <summary>
    /// Gets the character count of the current working text.
    /// </summary>
    public int CharacterCount => PromptText.Length;

    /// <summary>
    /// Gets the line count of the current working text.
    /// </summary>
    public int LineCount =>
        PromptText.Length == 0 ? 1 : PromptText.Count(character => character == '\n') + 1;

    /// <summary>
    /// Gets a value indicating whether the working text is empty or blank.
    /// </summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(PromptText);

    /// <summary>
    /// Gets a value indicating whether the prompt already carries prior output or context.
    /// </summary>
    public bool HasContextDuplication =>
        SupportsIncludeContext && IncludeContext &&
        WorkflowPromptEditingService.ReferencesWorkflowOutput(PromptText, PriorStepIds);

    /// <summary>
    /// Gets a value indicating whether the prompt text changed during the session.
    /// </summary>
    public bool IsPromptModified =>
        !string.Equals(PromptText, _entry.PromptText, StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether <c>includeContext</c> changed during the session.
    /// </summary>
    public bool IsContextModified => IncludeContext != _entry.IncludeContext;

    /// <summary>
    /// Gets a value indicating whether the model override changed during the session.
    /// </summary>
    public bool IsModelModified => !string.Equals(
        ModelOverride.Trim(),
        _entry.Model ?? string.Empty,
        StringComparison.Ordinal);

    /// <summary>Gets exact normalized aliases parsed from the editor.</summary>
    public IReadOnlyList<string> ToolAliases => ToolSelections.Count > 0
        ? ToolSelections.Where(item => item.IsSelected).Select(item => item.Alias).ToArray()
        : ParseList(ToolAliasesText);

    /// <summary>Gets optional Knowledge narrowing parsed from the editor.</summary>
    public KnowledgeAccessDefinition? KnowledgeAccess => ParseList(KnowledgeTagsText) is { Count: > 0 } tags ? new KnowledgeAccessDefinition { AllowedTags = tags } : null;

    /// <summary>Gets whether prompt-step tool policy changed.</summary>
    public bool IsToolPolicyModified => !ToolAliases.SequenceEqual(_entry.ToolAliases, StringComparer.OrdinalIgnoreCase) || !(KnowledgeAccess?.AllowedTags ?? []).SequenceEqual(_entry.KnowledgeAccess?.AllowedTags ?? [], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether the entry carries any effective change.
    /// </summary>
    public bool IsModified => IsPromptModified || IsContextModified || IsModelModified || IsToolPolicyModified;

    /// <summary>
    /// Restores the entry to its extracted state.
    /// </summary>
    public void Revert()
    {
        PromptText = _entry.PromptText;
        IncludeContext = _entry.IncludeContext;
        ModelOverride = _entry.Model ?? string.Empty;
        ToolAliasesText = string.Join(", ", _entry.ToolAliases);
        foreach (var selection in ToolSelections) selection.IsSelected = _entry.ToolAliases.Contains(selection.Alias, StringComparer.OrdinalIgnoreCase);
        KnowledgeTagsText = string.Join(", ", _entry.KnowledgeAccess?.AllowedTags ?? []);
    }

    partial void OnPromptTextChanged(string value) => RaiseDerived();

    partial void OnIncludeContextChanged(bool value) => RaiseDerived();

    partial void OnModelOverrideChanged(string value) => RaiseDerived();
    partial void OnToolAliasesTextChanged(string value) => RaiseDerived();
    partial void OnKnowledgeTagsTextChanged(string value) => RaiseDerived();

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(IsBlank));
        OnPropertyChanged(nameof(HasContextDuplication));
        OnPropertyChanged(nameof(IsPromptModified));
        OnPropertyChanged(nameof(IsContextModified));
        OnPropertyChanged(nameof(IsModelModified));
        OnPropertyChanged(nameof(ToolAliases));
        OnPropertyChanged(nameof(SelectsKnowledge));
        OnPropertyChanged(nameof(KnowledgeAccess));
        OnPropertyChanged(nameof(IsToolPolicyModified));
        OnPropertyChanged(nameof(UsesInheritedModel));
        OnPropertyChanged(nameof(IsModified));
    }

    private static IReadOnlyList<string> ParseList(string value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private void OnToolSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkflowPromptToolSelection.IsSelected)) RaiseDerived();
    }
}

/// <summary>Projects one declarative Tool Map entry into a prompt-step selection.</summary>
public sealed partial class WorkflowPromptToolSelection : ObservableObject
{
    /// <summary>Initializes one catalog-backed selection.</summary>
    public WorkflowPromptToolSelection(ToolDefinition definition, bool isSelected)
    {
        Alias = definition.Alias; Group = definition.ToolGroup; Description = definition.Description;
        Arguments = string.Join(", ", definition.Arguments); Capability = definition.RequiredCapability?.ToString() ?? "None";
        HasDynamicPath = definition.LocalPathArguments.Count > 0; _isSelected = isSelected;
    }
    /// <summary>Gets the exact Tool Map alias.</summary>
    public string Alias { get; }
    /// <summary>Gets the owning whitelist group.</summary>
    public string Group { get; }
    /// <summary>Gets the catalog description.</summary>
    public string Description { get; }
    /// <summary>Gets the ordered argument summary.</summary>
    public string Arguments { get; }
    /// <summary>Gets the required capability summary.</summary>
    public string Capability { get; }
    /// <summary>Gets whether model-chosen path arguments remain runtime-authorized.</summary>
    public bool HasDynamicPath { get; }
    [ObservableProperty] private bool _isSelected;
}
