namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Represents a normalized email message.
/// </summary>
/// <param name="Id">The provider message identifier.</param>
/// <param name="From">The sender header.</param>
/// <param name="To">The recipient header.</param>
/// <param name="Subject">The subject header.</param>
/// <param name="Snippet">The provider-generated message snippet.</param>
/// <param name="BodyPlain">The extracted plain-text body.</param>
/// <param name="ReceivedAt">The message receipt time.</param>
/// <param name="IsUnread">Whether the message is unread.</param>
public sealed record EmailMessageDto(
    string Id,
    string From,
    string To,
    string Subject,
    string Snippet,
    string BodyPlain,
    DateTimeOffset ReceivedAt,
    bool IsUnread);
