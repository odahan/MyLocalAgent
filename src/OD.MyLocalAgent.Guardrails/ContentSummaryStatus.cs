namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Identifies the outcome of complete-source oversized-content reduction.
/// </summary>
public enum ContentSummaryStatus
{
    /// <summary>
    /// The complete source was reduced without C# truncation.
    /// </summary>
    Summarized,

    /// <summary>
    /// The generated summary was capped in C#.
    /// </summary>
    SummaryTruncated,

    /// <summary>
    /// LLM processing is disabled.
    /// </summary>
    LlmProcessingDisabled,

    /// <summary>
    /// Oversized-content summarization is disabled.
    /// </summary>
    SummarizationDisabled,

    /// <summary>
    /// The source exceeds the complete-source processing limit.
    /// </summary>
    SourceTooLarge,
}
