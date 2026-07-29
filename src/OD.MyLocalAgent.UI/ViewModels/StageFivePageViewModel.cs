using CommunityToolkit.Mvvm.ComponentModel;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Defines the complete informational state for a feature page implemented in Stage 5.
/// </summary>
public abstract class StageFivePageViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StageFivePageViewModel"/> class.
    /// </summary>
    protected StageFivePageViewModel(
        string title,
        string description,
        string nextStageSummary)
    {
        Title = title;
        Description = description;
        NextStageSummary = nextStageSummary;
    }

    /// <summary>
    /// Gets the page title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the page description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the disabled content-area summary.
    /// </summary>
    public string NextStageSummary { get; }
}
