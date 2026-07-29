namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents the Stage 4 connector-configuration landing page.
/// </summary>
public sealed class ToolsViewModel : StageFivePageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolsViewModel"/> class.
    /// </summary>
    public ToolsViewModel()
        : base(
            "Tools",
            "Connect Gmail, Calendar, Dropbox, local files, and HTTP.",
            "Credential setup and connection checks arrive in Stage 5. Safety gates remain enforced.")
    {
    }
}
