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

/// <summary>Identifies one ordered content part in a Direct Chat message.</summary>
public enum DirectChatContentKind
{
    /// <summary>Markdown-capable text.</summary>
    Text,

    /// <summary>Raster image data.</summary>
    Image,

    /// <summary>Playable audio data.</summary>
    Audio,

    /// <summary>Reserved for a future video implementation.</summary>
    Video,

    /// <summary>Reserved for a future generic file implementation.</summary>
    File,
}

/// <summary>Describes one persisted, ordered message content part.</summary>
public sealed record DirectChatContent
{
    /// <summary>Gets the zero-based position within the message.</summary>
    public required int Position { get; init; }

    /// <summary>Gets the content kind.</summary>
    public required DirectChatContentKind Kind { get; init; }

    /// <summary>Gets the MIME media type.</summary>
    public required string MediaType { get; init; }

    /// <summary>Gets optional display or download file name.</summary>
    public string? FileName { get; init; }

    /// <summary>Gets text data for a text part.</summary>
    public string? Text { get; init; }

    /// <summary>Gets binary data for a media part.</summary>
    public byte[]? Data { get; init; }

    /// <summary>Gets the persisted byte size.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Gets the media duration when the provider or local parser supplied it.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Gets whether the content was obtained from a cloud provider.</summary>
    public bool IsCloudContent { get; init; }

    /// <summary>Gets the provider display name without credentials or request content.</summary>
    public string? ProviderName { get; init; }
}

/// <summary>Identifies the kind of content attached to a Direct Chat message.</summary>
public enum DirectChatAttachmentKind
{
    /// <summary>Text extracted locally from a document.</summary>
    Text,

    /// <summary>An image sent to a vision-capable model.</summary>
    Image,
}

/// <summary>Describes content retained with a Direct Chat message.</summary>
public sealed record DirectChatAttachment
{
    /// <summary>Gets the original file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the MIME media type.</summary>
    public required string MediaType { get; init; }

    /// <summary>Gets the attachment kind.</summary>
    public required DirectChatAttachmentKind Kind { get; init; }

    /// <summary>Gets extracted text for a text document.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the retained bytes for an image.</summary>
    public byte[]? Data { get; init; }
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

    /// <summary>Gets whether this conversation may invoke the shared agent tools.</summary>
    public bool AgentEnabled { get; init; }

    /// <summary>Gets the tool groups explicitly granted to this conversation.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>Gets sensitive capabilities explicitly granted to this conversation.</summary>
    public IReadOnlyList<TaskCapability> Capabilities { get; init; } = [];

    /// <summary>Gets absolute local locations explicitly authorized for this conversation.</summary>
    public IReadOnlyList<FileSystemAccessRule> FileSystemAccess { get; init; } = [];

    /// <summary>Gets the deny-by-default Knowledge tag authorization for this conversation.</summary>
    public KnowledgeAccessDefinition? KnowledgeAccess { get; init; }
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

    /// <summary>Gets files attached to this message.</summary>
    public IReadOnlyList<DirectChatAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// Gets ordered message contents. Legacy text-only rows are projected as one text part.
    /// </summary>
    public IReadOnlyList<DirectChatContent> Contents { get; init; } = [];
}
