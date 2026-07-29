namespace OD.MyLocalAgent.Core;

/// <summary>
/// Configures application logging.
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Logging";

    /// <summary>
    /// Gets or sets the minimum Serilog level.
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>
    /// Gets or sets the maximum number of events retained by the observable in-memory sink.
    /// </summary>
    public int InMemoryCapacity { get; set; } = 2_000;
}
