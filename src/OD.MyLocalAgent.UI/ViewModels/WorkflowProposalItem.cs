using CommunityToolkit.Mvvm.ComponentModel;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents one required tool-group or filesystem proposal for explicit approval.
/// </summary>
public sealed partial class WorkflowProposalItem : ObservableObject
{
    /// <summary>
    /// Initializes a proposal item.
    /// </summary>
    /// <param name="kind">The proposal kind.</param>
    /// <param name="key">The stable group or normalized path.</param>
    /// <param name="description">The user-facing description.</param>
    public WorkflowProposalItem(string kind, string key, string description)
    {
        Kind = kind;
        Key = key;
        Description = description;
    }

    /// <summary>
    /// Gets the proposal kind.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the stable group or normalized path.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the user-facing proposal description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the user explicitly accepts the proposal.
    /// </summary>
    [ObservableProperty]
    private bool _isAccepted;
}
