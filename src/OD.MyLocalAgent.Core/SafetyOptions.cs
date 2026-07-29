namespace OD.MyLocalAgent.Core;

/// <summary>
/// Configures opt-in gates for operations that have destructive or outbound side effects.
/// </summary>
public sealed class SafetyOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Safety";

    /// <summary>
    /// Gets or sets a value indicating whether email sending is allowed.
    /// </summary>
    public bool AllowSendEmail { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether file deletion is allowed.
    /// </summary>
    public bool AllowFileDelete { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether HTTP POST requests are allowed.
    /// </summary>
    public bool AllowHttpPost { get; set; }
}
