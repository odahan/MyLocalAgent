namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents the Stage 4 AI-playground landing page.
/// </summary>
public sealed class AIPlaygroundViewModel : StageFivePageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIPlaygroundViewModel"/> class.
    /// </summary>
    public AIPlaygroundViewModel()
        : base(
            "AI playground",
            "Send a free-form prompt to the active provider.",
            "Streaming generation arrives in Stage 5. Provider selection and health checks are ready in Settings.")
    {
    }
}
