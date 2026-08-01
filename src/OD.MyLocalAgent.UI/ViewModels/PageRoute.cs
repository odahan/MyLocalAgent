namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Identifies a top-level shell destination.
/// </summary>
public enum PageRoute
{
    /// <summary>
    /// The application overview.
    /// </summary>
    Dashboard,

    /// <summary>
    /// Scheduled task management.
    /// </summary>
    Tasks,

    /// <summary>
    /// Task definition editing.
    /// </summary>
    TaskEditor,

    /// <summary>
    /// Task prompt editing and testing.
    /// </summary>
    PromptEditor,

    /// <summary>
    /// Live application logs.
    /// </summary>
    Logs,

    /// <summary>
    /// Connector and tool configuration.
    /// </summary>
    Tools,

    /// <summary>
    /// Direct tool invocation.
    /// </summary>
    ToolTester,

    /// <summary>
    /// Raw provider prompt testing.
    /// </summary>
    AIPlayground,

    /// <summary>
    /// Human-facing prompt-injection trust screening.
    /// </summary>
    TrustFilter,

    /// <summary>
    /// Application settings and diagnostics.
    /// </summary>
    Settings,

    /// <summary>
    /// Application identity, version, and copyright.
    /// </summary>
    About,
}
