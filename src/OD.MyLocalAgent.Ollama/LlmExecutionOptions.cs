namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Defines the process-wide limit for simultaneous model requests.
/// </summary>
public sealed record LlmExecutionOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "LlmExecution";

    /// <summary>
    /// Gets the maximum number of model requests that may run at once.
    /// </summary>
    public int MaxConcurrentRequests { get; init; } = 1;
}
