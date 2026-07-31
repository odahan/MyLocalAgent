namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Emits ephemeral, user-requested execution diagnostics without writing them to normal logs.
/// </summary>
public interface IExecutionDebugTrace
{
    /// <summary>
    /// Gets a value indicating whether detailed execution tracing is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Emits one diagnostic event.
    /// </summary>
    /// <param name="category">The stable event category.</param>
    /// <param name="name">The operation name.</param>
    /// <param name="details">The sanitized diagnostic details.</param>
    void Write(string category, string name, object? details);
}

/// <summary>
/// Disables detailed execution tracing outside an explicitly enabled interactive Runner run.
/// </summary>
public sealed class NullExecutionDebugTrace : IExecutionDebugTrace
{
    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public void Write(string category, string name, object? details)
    {
    }
}
