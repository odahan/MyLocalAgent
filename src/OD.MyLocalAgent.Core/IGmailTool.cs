namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides Gmail operations to agents and workflows.
/// </summary>
public interface IGmailTool
{
    /// <summary>
    /// Reads messages received today.
    /// </summary>
    Task<string> ReadTodayAsync(
        int? maxMessages = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads unread messages.
    /// </summary>
    Task<string> ReadUnreadAsync(
        int? maxMessages = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads messages since the start of the local day a number of days ago.
    /// </summary>
    Task<string> ReadRecentAsync(
        int daysBack,
        int? maxMessages = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an email when the safety gate is enabled.
    /// </summary>
    Task<string> SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken);
}
