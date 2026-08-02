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

    /// <summary>
    /// Determines whether the gate identified by a Tool Map safety flag is enabled.
    /// </summary>
    /// <param name="configurationKey">The Tool Map safety flag, such as <c>Safety:AllowHttpPost</c>.</param>
    /// <returns><see langword="true"/> when the gate is enabled; otherwise <see langword="false"/>.</returns>
    /// <remarks>An unknown key is reported disabled so an unmapped gate can never widen access.</remarks>
    public bool IsEnabled(string configurationKey) =>
        configurationKey switch
        {
            "Safety:AllowSendEmail" => AllowSendEmail,
            "Safety:AllowFileDelete" => AllowFileDelete,
            "Safety:AllowHttpPost" => AllowHttpPost,
            _ => false,
        };
}
