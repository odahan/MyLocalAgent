using CommunityToolkit.Mvvm.ComponentModel;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents one metadata-driven task tool-group whitelist choice.
/// </summary>
public sealed partial class ToolGroupSelectionItem : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolGroupSelectionItem"/> class.
    /// </summary>
    /// <param name="definition">The shared catalog definition.</param>
    /// <param name="requiresLocalPathAuthorization">
    /// Whether at least one tool in the group declares a local-path argument.
    /// </param>
    public ToolGroupSelectionItem(
        ToolGroupDefinition definition,
        bool requiresLocalPathAuthorization)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        RequiresLocalPathAuthorization = requiresLocalPathAuthorization;
    }

    /// <summary>
    /// Gets the shared catalog definition.
    /// </summary>
    public ToolGroupDefinition Definition { get; }

    /// <summary>
    /// Gets the stable group name stored in task JSON.
    /// </summary>
    public string Name => Definition.Name;

    /// <summary>
    /// Gets the English label shown in the task editor.
    /// </summary>
    public string DisplayName => Definition.DisplayName;

    /// <summary>
    /// Gets the English catalog description shown as contextual help.
    /// </summary>
    public string Description => Definition.Description;

    /// <summary>
    /// Gets a value indicating whether the group uses local-path authorization.
    /// </summary>
    public bool RequiresLocalPathAuthorization { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the task grants this group.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}
