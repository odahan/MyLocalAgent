namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Represents a sanitized compact-processing failure without external content or provider output.
/// </summary>
public sealed class CompactExternalContentPhaseException : Exception
{
    /// <summary>
    /// Initializes a new content-free phase exception.
    /// </summary>
    /// <param name="phase">The program-owned phase name.</param>
    /// <param name="providerName">The configured provider name.</param>
    /// <param name="modelName">The configured model name.</param>
    /// <param name="itemCount">The aggregate number of affected items.</param>
    /// <param name="elapsedMilliseconds">The aggregate elapsed time.</param>
    public CompactExternalContentPhaseException(
        string phase,
        string providerName,
        string modelName,
        int itemCount,
        long elapsedMilliseconds)
        : base(
            $"Compact Guardrails phase '{phase}' failed for provider '{providerName}', model '{modelName}', affecting {itemCount} item(s) after {elapsedMilliseconds} ms.")
    {
        Phase = phase;
        ProviderName = providerName;
        ModelName = modelName;
        ItemCount = itemCount;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    /// <summary>Gets the program-owned phase name.</summary>
    public string Phase { get; }

    /// <summary>Gets the configured provider name.</summary>
    public string ProviderName { get; }

    /// <summary>Gets the configured model name.</summary>
    public string ModelName { get; }

    /// <summary>Gets the aggregate number of affected items.</summary>
    public int ItemCount { get; }

    /// <summary>Gets the aggregate elapsed milliseconds.</summary>
    public long ElapsedMilliseconds { get; }
}
