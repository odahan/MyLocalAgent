using CommunityToolkit.Mvvm.ComponentModel;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Presents one current or stale Knowledge authorization tag.</summary>
public sealed partial class KnowledgeTagSelectionItem : ObservableObject
{
    /// <summary>Initializes a Knowledge tag choice.</summary>
    public KnowledgeTagSelectionItem(string name, bool isAvailable, bool isSelected)
    {
        Name = name;
        IsAvailable = isAvailable;
        _isSelected = isSelected;
    }

    /// <summary>Gets the normalized persisted tag.</summary>
    public string Name { get; }

    /// <summary>Gets whether at least one current document owns the tag.</summary>
    public bool IsAvailable { get; }

    /// <summary>Gets an explanatory state for unavailable persisted tags.</summary>
    public string AvailabilityText => IsAvailable ? string.Empty : "Unavailable — remove or recreate this tag";

    /// <summary>Gets or sets whether this tag is authorized.</summary>
    [ObservableProperty]
    private bool _isSelected;
}
