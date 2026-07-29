namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides Gmail operations to agents and workflows.
/// </summary>
public interface IGmailTool
{
    /// <summary>
    /// Reads messages received today.
    /// </summary>
    Task<string> ReadTodayAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads unread messages.
    /// </summary>
    Task<string> ReadUnreadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends an email when the safety gate is enabled.
    /// </summary>
    Task<string> SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken);
}
