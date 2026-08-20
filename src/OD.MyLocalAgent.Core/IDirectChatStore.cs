namespace OD.MyLocalAgent.Core;

/// <summary>Persists Direct Chat conversations and messages.</summary>
public interface IDirectChatStore
{
    /// <summary>Creates or migrates the dedicated database and recovers interrupted messages.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Returns conversations ordered from most recently updated.</summary>
    Task<IReadOnlyList<DirectChatConversation>> GetConversationsAsync(
        CancellationToken cancellationToken);

    /// <summary>Returns one conversation, or <see langword="null"/> when it does not exist.</summary>
    Task<DirectChatConversation?> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken);

    /// <summary>Creates a new empty conversation.</summary>
    Task<DirectChatConversation> CreateConversationAsync(
        string title,
        CancellationToken cancellationToken);

    /// <summary>Updates the conversation title.</summary>
    Task UpdateConversationTitleAsync(
        string conversationId,
        string title,
        CancellationToken cancellationToken);

    /// <summary>Updates the explicit agent permissions for one conversation.</summary>
    Task UpdateConversationAgentProfileAsync(
        string conversationId,
        bool agentEnabled,
        IReadOnlyList<string> tools,
        IReadOnlyList<TaskCapability> capabilities,
        IReadOnlyList<FileSystemAccessRule> fileSystemAccess,
        CancellationToken cancellationToken);

    /// <summary>Updates agent permissions including the conversation-owned Knowledge tag scope.</summary>
    Task UpdateConversationAgentProfileAsync(
        string conversationId,
        bool agentEnabled,
        IReadOnlyList<string> tools,
        IReadOnlyList<TaskCapability> capabilities,
        IReadOnlyList<FileSystemAccessRule> fileSystemAccess,
        KnowledgeAccessDefinition? knowledgeAccess,
        CancellationToken cancellationToken) =>
        UpdateConversationAgentProfileAsync(
            conversationId,
            agentEnabled,
            tools,
            capabilities,
            fileSystemAccess,
            cancellationToken);

    /// <summary>Deletes a conversation and all its messages.</summary>
    Task<bool> DeleteConversationAsync(
        string conversationId,
        CancellationToken cancellationToken);

    /// <summary>Returns a page of messages in ascending sequence order.</summary>
    Task<IReadOnlyList<DirectChatMessage>> GetMessagesAsync(
        string conversationId,
        long? beforeSequence,
        int maximumCount,
        CancellationToken cancellationToken);

    /// <summary>Appends a message and assigns its sequence.</summary>
    Task<DirectChatMessage> AppendMessageAsync(
        string conversationId,
        DirectChatMessageRole role,
        string content,
        DirectChatMessageState state,
        int? estimatedTokens,
        CancellationToken cancellationToken,
        IReadOnlyList<DirectChatAttachment>? attachments = null,
        IReadOnlyList<DirectChatContent>? contents = null);

    /// <summary>Updates message content and state during or after streaming.</summary>
    Task UpdateMessageAsync(
        string messageId,
        string content,
        DirectChatMessageState state,
        int? estimatedTokens,
        CancellationToken cancellationToken,
        IReadOnlyList<DirectChatContent>? contents = null);

    /// <summary>Atomically records the cumulative summary and its coverage boundary.</summary>
    Task UpdateSummaryAsync(
        string conversationId,
        string summary,
        long throughSequence,
        CancellationToken cancellationToken);
}
