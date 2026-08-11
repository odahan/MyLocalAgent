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
    public WorkflowPromptEditorItem(WorkflowPromptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
        _promptText = entry.PromptText;
        _includeContext = entry.IncludeContext;
    }

    [ObservableProperty]
    private string _promptText;

    [ObservableProperty]
    private bool _includeContext;

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
    /// Gets a value indicating whether the entry carries any effective change.
    /// </summary>
    public bool IsModified => IsPromptModified || IsContextModified;

    /// <summary>
    /// Restores the entry to its extracted state.
    /// </summary>
    public void Revert()
    {
        PromptText = _entry.PromptText;
        IncludeContext = _entry.IncludeContext;
    }

    partial void OnPromptTextChanged(string value) => RaiseDerived();

    partial void OnIncludeContextChanged(bool value) => RaiseDerived();

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(IsBlank));
        OnPropertyChanged(nameof(HasContextDuplication));
        OnPropertyChanged(nameof(IsPromptModified));
        OnPropertyChanged(nameof(IsContextModified));
        OnPropertyChanged(nameof(IsModified));
    }
}
