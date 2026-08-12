namespace OD.MyLocalAgent.Core;

/// <summary>Identifies a persisted Direct Chat message author.</summary>
public enum DirectChatMessageRole
{
    /// <summary>The human user.</summary>
    User,

    /// <summary>The language-model assistant.</summary>
    Assistant,
}

/// <summary>Identifies the persistence state of a Direct Chat message.</summary>
public enum DirectChatMessageState
{
    /// <summary>The message has been created but processing has not started.</summary>
    Pending,

    /// <summary>The assistant response is currently streaming.</summary>
    Streaming,

    /// <summary>The message completed normally.</summary>
    Completed,

    /// <summary>The user canceled generation.</summary>
    Canceled,

    /// <summary>Generation failed.</summary>
    Failed,
}

/// <summary>Describes one persisted Direct Chat conversation.</summary>
public sealed record DirectChatConversation
{
    /// <summary>Gets the stable conversation identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the human-facing conversation title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the UTC creation time.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>Gets the UTC last-modified time.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>Gets the cumulative memory summary, when one exists.</summary>
    public string? SummaryText { get; init; }

    /// <summary>Gets the greatest message sequence represented by the summary.</summary>
    public long? SummaryThroughSequence { get; init; }

    /// <summary>Gets the UTC time at which the summary was last changed.</summary>
    public DateTimeOffset? SummaryUpdatedUtc { get; init; }
}

/// <summary>Describes one persisted message in a Direct Chat conversation.</summary>
public sealed record DirectChatMessage
{
    /// <summary>Gets the stable message identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the owning conversation identifier.</summary>
    public required string ConversationId { get; init; }

    /// <summary>Gets the monotonically increasing sequence within the conversation.</summary>
    public required long Sequence { get; init; }

    /// <summary>Gets the message author.</summary>
    public required DirectChatMessageRole Role { get; init; }

    /// <summary>Gets the message content.</summary>
    public required string Content { get; init; }

    /// <summary>Gets the current persistence state.</summary>
    public required DirectChatMessageState State { get; init; }

    /// <summary>Gets the UTC creation time.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>Gets the estimated token count, when calculated.</summary>
    public int? EstimatedTokens { get; init; }
}
