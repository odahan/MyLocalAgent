namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides low-level Gmail operations.
/// </summary>
public interface IGmailConnector
{
    /// <summary>Collects one bounded batch for a decision reader, retaining pagination coverage.</summary>
    async Task<GmailReadBatch> GetDecisionBatchAsync(string selection, int daysBack, int maxMessages, CancellationToken cancellationToken)
    {
        var messages = selection switch
        {
            "Today" => await GetTodaysMessagesAsync(maxMessages, cancellationToken).ConfigureAwait(false),
            "Unread" => await GetUnreadMessagesAsync(maxMessages, cancellationToken).ConfigureAwait(false),
            "Recent" => await GetRecentMessagesAsync(daysBack, maxMessages, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException("Unknown Gmail selection.", nameof(selection)),
        };
        return new GmailReadBatch(messages, messages.Count >= maxMessages);
    }

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
