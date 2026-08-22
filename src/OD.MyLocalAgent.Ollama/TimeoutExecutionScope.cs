namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Holds a temporary request-timeout override for one logical execution flow.
/// </summary>
public static class TimeoutExecutionScope
{
    private static readonly AsyncLocal<int?> CurrentTimeoutSeconds = new();

    /// <summary>
    /// Gets the timeout override in seconds, or <see langword="null"/> when the provider default applies.
    /// </summary>
    public static int? TimeoutSeconds => CurrentTimeoutSeconds.Value;

    /// <summary>
    /// Temporarily selects a timeout override and restores the previous value when disposed.
    /// </summary>
    /// <param name="timeoutSeconds">The positive timeout override in seconds, or <see langword="null"/>.</param>
    /// <returns>A scope that restores the preceding override.</returns>
    public static IDisposable Push(int? timeoutSeconds)
    {
        if (timeoutSeconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds),
                "The timeout override must be positive when supplied.");
        }

        var previous = CurrentTimeoutSeconds.Value;
        CurrentTimeoutSeconds.Value = timeoutSeconds;
        return new Scope(previous);
    }

    private sealed class Scope(int? previous) : IDisposable
    {
        public void Dispose() => CurrentTimeoutSeconds.Value = previous;
    }
}
