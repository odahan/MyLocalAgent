using Serilog.Core;
using Serilog.Events;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements a bounded thread-safe observable Serilog sink.
/// </summary>
public sealed class InMemoryLogSink : IInMemoryLogSink, ILogEventSink
{
    private readonly int _capacity;
    private readonly Queue<LogEvent> _events = new();
    private readonly object _syncRoot = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryLogSink"/> class.
    /// </summary>
    /// <param name="capacity">The maximum retained event count.</param>
    public InMemoryLogSink(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <inheritdoc />
    public event Action<LogEvent>? EventEmitted;

    /// <inheritdoc />
    public IReadOnlyList<LogEvent> GetSnapshot()
    {
        lock (_syncRoot)
        {
            return _events.ToArray();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_syncRoot)
        {
            _events.Clear();
        }
    }

    /// <summary>
    /// Appends one Serilog event to the bounded buffer.
    /// </summary>
    /// <param name="logEvent">The emitted event.</param>
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        lock (_syncRoot)
        {
            _events.Enqueue(logEvent);
            while (_events.Count > _capacity)
            {
                _ = _events.Dequeue();
            }
        }

        EventEmitted?.Invoke(logEvent);
    }
}
