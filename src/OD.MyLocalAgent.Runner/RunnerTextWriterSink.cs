using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace OD.MyLocalAgent.Runner;

/// <summary>
/// Emits console-style formatted events to the runner output writer so scheduled runs
/// keep their full Serilog output inside the per-run log (§19.3).
/// </summary>
internal sealed class RunnerTextWriterSink : ILogEventSink
{
    private const string OutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    private readonly TextWriter _writer;
    private readonly MessageTemplateTextFormatter _formatter = new(OutputTemplate);

    /// <summary>
    /// Initializes a new instance of the <see cref="RunnerTextWriterSink"/> class.
    /// </summary>
    /// <param name="writer">The destination writer.</param>
    public RunnerTextWriterSink(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        _formatter.Format(logEvent, _writer);
        _writer.Flush();
    }
}
