namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Represents the observed result of one workflow step.
/// </summary>
/// <param name="Id">The step output identifier.</param>
/// <param name="Alias">The Tool Map alias.</param>
/// <param name="Status">The final step status.</param>
/// <param name="Output">The output, when successful.</param>
/// <param name="ErrorMessage">The error message, when failed.</param>
/// <param name="Error">The captured error, when failed.</param>
/// <param name="StartedUtc">The UTC start time.</param>
/// <param name="Duration">The total duration including retries and backoff.</param>
/// <param name="Attempts">The number of execution attempts.</param>
public sealed record WorkflowStepResult(
    string Id,
    string Alias,
    WorkflowStepStatus Status,
    string? Output,
    string? ErrorMessage,
    Exception? Error,
    DateTimeOffset StartedUtc,
    TimeSpan Duration,
    int Attempts)
{
    /// <summary>
    /// Gets the UTF-8 output size in bytes.
    /// </summary>
    public int OutputSize =>
        Output is null ? 0 : System.Text.Encoding.UTF8.GetByteCount(Output);
}
