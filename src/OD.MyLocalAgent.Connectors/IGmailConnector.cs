namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides low-level Gmail operations.
/// </summary>
public interface IGmailConnector
{
    /// <summary>
    /// Gets messages received today.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized messages.</returns>
    Task<IReadOnlyList<EmailMessageDto>> GetTodaysMessagesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets unread messages.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized unread messages.</returns>
    Task<IReadOnlyList<EmailMessageDto>> GetUnreadMessagesAsync(CancellationToken cancellationToken);

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
