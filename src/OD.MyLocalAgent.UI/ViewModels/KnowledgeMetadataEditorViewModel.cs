using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Coordinates one memory-only Knowledge metadata editing session.</summary>
public sealed partial class KnowledgeMetadataEditorViewModel : ObservableObject
{
    private string _originalName = string.Empty;
    private string _originalDescription = string.Empty;
    private string _originalTags = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _tagsText = string.Empty;

    [ObservableProperty]
    private string _validationText = "Change one or more values, then apply.";

    /// <summary>Occurs when the dialog host should close the window.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Gets the applied metadata, or null when the session was cancelled.</summary>
    public KnowledgeMetadataEditorResult? Result { get; private set; }

    /// <summary>Gets whether the current values differ from the initial snapshot.</summary>
    public bool HasChanges =>
        !string.Equals(Name, _originalName, StringComparison.Ordinal) ||
        !string.Equals(Description, _originalDescription, StringComparison.Ordinal) ||
        !string.Equals(TagsText, _originalTags, StringComparison.Ordinal);

    /// <summary>Loads one immutable metadata snapshot into the editor.</summary>
    /// <param name="request">The existing document metadata.</param>
    public void Initialize(KnowledgeMetadataEditorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _originalName = request.Name;
        _originalDescription = request.Description;
        _originalTags = string.Join(", ", request.Tags);
        Name = _originalName;
        Description = _originalDescription;
        TagsText = _originalTags;
        ValidationText = "Change one or more values, then apply.";
        ApplyCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasChanges));
    }

    private bool CanApply() => HasChanges && TryValidate(out _, updateMessage: false);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (!TryValidate(out var tags, updateMessage: true))
        {
            return;
        }

        Result = new KnowledgeMetadataEditorResult(
            Name.Trim(),
            Description.Trim(),
            tags);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnNameChanged(string value) => RaiseSessionState();
    partial void OnDescriptionChanged(string value) => RaiseSessionState();
    partial void OnTagsTextChanged(string value) => RaiseSessionState();

    private void RaiseSessionState()
    {
        OnPropertyChanged(nameof(HasChanges));
        _ = TryValidate(out _, updateMessage: true);
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private bool TryValidate(out IReadOnlyList<string> tags, bool updateMessage)
    {
        tags = [];
        if (string.IsNullOrWhiteSpace(Name))
        {
            if (updateMessage)
            {
                ValidationText = "Document name is required.";
            }
            return false;
        }

        try
        {
            tags = KnowledgeTagPolicy.NormalizeScope(
                TagsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            if (tags.Count == 0)
            {
                if (updateMessage)
                {
                    ValidationText = "Enter at least one authorization tag.";
                }
                return false;
            }
        }
        catch (ArgumentException exception)
        {
            if (updateMessage)
            {
                ValidationText = exception.Message;
            }
            return false;
        }

        if (updateMessage)
        {
            ValidationText = HasChanges
                ? "Metadata is valid and ready to apply."
                : "Change one or more values, then apply.";
        }
        return true;
    }
}
