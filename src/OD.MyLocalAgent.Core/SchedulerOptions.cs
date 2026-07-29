namespace OD.MyLocalAgent.Core;

/// <summary>
/// Configures Windows Task Scheduler integration.
/// </summary>
public sealed class SchedulerOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Scheduler";

    /// <summary>
    /// Gets or sets the Task Scheduler folder name.
    /// </summary>
    public string TaskFolder { get; set; } = "OD.MyLocalAgent";

    /// <summary>
    /// Gets or sets a value indicating whether tasks use S4U logon.
    /// </summary>
    public bool RunWhenLoggedOff { get; set; }

    /// <summary>
    /// Gets or sets an optional explicit runner executable path.
    /// </summary>
    public string RunnerPath { get; set; } = string.Empty;
}
