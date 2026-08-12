namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines Direct Chat context-memory and compression limits.
/// </summary>
public sealed record DirectChatOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "DirectChat";

    /// <summary>Gets a value indicating whether old chat turns may be summarized.</summary>
    public bool CompressionEnabled { get; init; } = true;

    /// <summary>Gets the context-window ratio that triggers compression.</summary>
    public double CompressionTriggerRatio { get; init; } = 0.70;

    /// <summary>Gets the desired context-window ratio after compression.</summary>
    public double CompressionTargetRatio { get; init; } = 0.45;

    /// <summary>Gets the context-window ratio reserved for the assistant response.</summary>
    public double ResponseReserveRatio { get; init; } = 0.25;

    /// <summary>Gets the minimum number of recent user turns retained verbatim.</summary>
    public int MinimumRecentTurns { get; init; } = 4;

    /// <summary>Gets the requested upper bound for a generated memory summary.</summary>
    public int MaximumSummaryTokens { get; init; } = 1024;

    /// <summary>Gets the conservative character-to-token estimation divisor.</summary>
    public int EstimatedCharactersPerToken { get; init; } = 4;

    /// <summary>Gets the maximum persisted size of one generated media item.</summary>
    public long MaximumMediaBytes { get; init; } = 25 * 1024 * 1024;

    /// <summary>Gets the maximum total generated-media size in one conversation.</summary>
    public long MaximumConversationMediaBytes { get; init; } = 250 * 1024 * 1024;

    /// <summary>Determines whether a configuration instance is valid.</summary>
    public static bool IsValid(DirectChatOptions options) =>
        options is not null &&
        options.CompressionTriggerRatio > 0 &&
        options.CompressionTriggerRatio < 1 &&
        options.CompressionTargetRatio > 0 &&
        options.CompressionTargetRatio < options.CompressionTriggerRatio &&
        options.ResponseReserveRatio > 0 &&
        options.ResponseReserveRatio < 1 &&
        options.CompressionTriggerRatio + options.ResponseReserveRatio <= 1 &&
        options.MinimumRecentTurns > 0 &&
        options.MaximumSummaryTokens >= 128 &&
        options.EstimatedCharactersPerToken > 0 &&
        options.MaximumMediaBytes > 0 &&
        options.MaximumConversationMediaBytes >= options.MaximumMediaBytes;
}
