using Microsoft.Extensions.Logging;

namespace OD.MyLocalAgent.Tests.TestDoubles;

/// <summary>Captures rendered diagnostics for deterministic logging assertions.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception.Message));
    }
}
