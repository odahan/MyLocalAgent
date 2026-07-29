namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents the Stage 4 direct-tool-testing landing page.
/// </summary>
public sealed class ToolTesterViewModel : StageFivePageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolTesterViewModel"/> class.
    /// </summary>
    public ToolTesterViewModel()
        : base(
            "Tool tester",
            "Inspect Tool Map aliases and run one whitelisted operation.",
            "Argument-aware invocation arrives in Stage 5. Destructive and outbound actions stay opt-in.")
    {
    }
}
