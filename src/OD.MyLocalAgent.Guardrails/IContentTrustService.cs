namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Screens untrusted content and safely reduces oversized sources.
/// </summary>
public interface IContentTrustService
{
    /// <summary>
    /// Classifies one content block through the active LLM provider.
    /// </summary>
    /// <param name="content">The raw untrusted content.</param>
    /// <param name="returnBase64">Whether successful content must use Base64 delivery.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The classified and wrapped content.</returns>
    Task<ContentTrustResult> ScreenAsync(
        string content,
        bool returnBase64,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reduces a complete oversized source through sequential, tool-less calls.
    /// </summary>
    /// <param name="content">The complete untrusted source.</param>
    /// <param name="maxSummaryChars">The caller-specific summary maximum.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The reduction result.</returns>
    Task<ContentSummaryResult> SummarizeAsync(
        string content,
        int maxSummaryChars,
        CancellationToken cancellationToken);

    /// <summary>
    /// Screens and summarizes ordered compact external-content items in bounded batches.
    /// </summary>
    /// <param name="items">The non-empty ordered compact items.</param>
    /// <param name="cancellationToken">A token that cancels the complete operation.</param>
    /// <returns>One ordered result per input and content-free phase failures.</returns>
    Task<CompactExternalContentBatchResult> ProcessCompactAsync(
        IReadOnlyList<CompactExternalContentItem> items,
        CancellationToken cancellationToken);
}
