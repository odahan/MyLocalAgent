using CommunityToolkit.Mvvm.ComponentModel;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Presents one task-owned sensitive permission.</summary>
public sealed partial class TaskCapabilitySelectionItem : ObservableObject
{
    /// <summary>Initializes a capability selection.</summary>
    public TaskCapabilitySelectionItem(TaskCapabilityDefinition definition) =>
        Definition = definition;

    /// <summary>Gets the shared capability metadata.</summary>
    public TaskCapabilityDefinition Definition { get; }
    /// <summary>Gets the stable capability value.</summary>
    public TaskCapability Capability => Definition.Capability;
    /// <summary>Gets the display name.</summary>
    public string DisplayName => Definition.DisplayName;
    /// <summary>Gets the side-effect description.</summary>
    public string Description => Definition.Description;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible;
}
