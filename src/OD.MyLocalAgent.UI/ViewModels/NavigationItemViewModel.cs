using MahApps.Metro.IconPacks;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Describes one destination in the shell navigation rail.
/// </summary>
/// <param name="Title">The user-facing title.</param>
/// <param name="Icon">The Material icon.</param>
/// <param name="Route">The destination route.</param>
public sealed record NavigationItemViewModel(
    string Title,
    PackIconMaterialKind Icon,
    PageRoute Route);
