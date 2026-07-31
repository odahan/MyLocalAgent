namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides low-level Gmail operations.
/// </summary>
public interface IGmailConnector
{
    /// <summary>
    /// Verifies that the current Google credential can read Gmail metadata.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task CheckReadAccessAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets messages received today.
    /// </summary>
    /// <param name="maxMessages">The optional maximum number of messages to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized messages.</returns>
    Task<IReadOnlyList<EmailMessageDto>> GetTodaysMessagesAsync(
        int? maxMessages,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets unread messages.
    /// </summary>
    /// <param name="maxMessages">The optional maximum number of messages to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized unread messages.</returns>
    Task<IReadOnlyList<EmailMessageDto>> GetUnreadMessagesAsync(
        int? maxMessages,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets messages received since the start of the local day a number of days ago.
    /// </summary>
    /// <param name="daysBack">The number of local calendar days to include before today.</param>
    /// <param name="maxMessages">The optional maximum number of messages to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized messages.</returns>
    Task<IReadOnlyList<EmailMessageDto>> GetRecentMessagesAsync(
        int daysBack,
        int? maxMessages,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends a plain-text email message.
    /// </summary>
    /// <param name="to">The recipient address.</param>
    /// <param name="subject">The message subject.</param>
    /// <param name="body">The plain-text body.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized sent message.</returns>
    Task<EmailMessageDto> SendMessageAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken);
}
