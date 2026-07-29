using Serilog.Events;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes the bounded observable log buffer consumed by the UI.
/// </summary>
public interface IInMemoryLogSink
{
    /// <summary>
    /// Occurs when a log event is appended.
    /// </summary>
    event Action<LogEvent>? EventEmitted;

    /// <summary>
    /// Gets a stable snapshot of the current buffer.
    /// </summary>
    /// <returns>The buffered events in emission order.</returns>
    IReadOnlyList<LogEvent> GetSnapshot();

    /// <summary>
    /// Clears the in-memory buffer.
    /// </summary>
    void Clear();
}
