using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Runner;

/// <summary>
/// Writes opt-in detailed Runner diagnostics only to the interactive child-process output.
/// </summary>
internal sealed class RunnerExecutionDebugTrace : IExecutionDebugTrace
{
    private readonly TextWriter _output;
    private readonly object _syncRoot = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RunnerExecutionDebugTrace"/> class.
    /// </summary>
    /// <param name="output">The interactive Runner output writer.</param>
    public RunnerExecutionDebugTrace(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public void Write(string category, string name, object? details)
    {
        lock (_syncRoot)
        {
            _output.WriteLine(RunnerDebugProtocol.Encode(category, name, details));
        }
    }
}
