namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Configures prompt-injection screening and oversized-content reduction.
/// </summary>
public sealed class GuardrailsOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Guardrails";

    /// <summary>
    /// Gets or sets whether LLM-backed screening and summarization are enabled.
    /// </summary>
    public bool ScreeningEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum score for marked plaintext delivery.
    /// </summary>
    public int CleanThreshold { get; set; } = 80;

    /// <summary>
    /// Gets or sets the minimum score for marked Base64 delivery.
    /// </summary>
    public int CautionThreshold { get; set; } = 50;

    /// <summary>
    /// Gets or sets the largest content block accepted by one screening call.
    /// </summary>
    public int MaxContentChars { get; set; } = 8_000;

    /// <summary>
    /// Gets or sets whether oversized content may be reduced by the LLM.
    /// </summary>
    public bool SummarizeOversizedContent { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum generated summary length.
    /// </summary>
    public int MaxSummaryChars { get; set; } = 4_000;

    /// <summary>
    /// Gets or sets the largest source eligible for safe complete-source reduction.
    /// </summary>
    public int MaxSummarizationSourceChars { get; set; } = 100_000;

    /// <summary>
    /// Gets or sets the independent timeout for each guardrail provider call.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets whether verdicts and summaries are cached in memory.
    /// </summary>
    public bool CacheResults { get; set; } = true;

    /// <summary>
    /// Gets or sets the Gmail screening limit, where zero means unlimited.
    /// </summary>
    public int MaxGmailMessagesPerOperation { get; set; }
}
