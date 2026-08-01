using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Forwards writes unchanged to an inner writer and mirrors completed lines into the
/// active run-log session, excluding the private protocol channels (§19.1 S16-D5).
/// </summary>
public sealed class RunLogTeeWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly RunLogSessionHolder _holder;
    private readonly RunLogStream _stream;
    private readonly StringBuilder _buffer = new();
    private readonly Lock _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RunLogTeeWriter"/> class.
    /// </summary>
    /// <param name="inner">The destination writer.</param>
    /// <param name="holder">The active-session holder.</param>
    /// <param name="stream">The stream identity recorded for mirrored lines.</param>
    public RunLogTeeWriter(
        TextWriter inner,
        RunLogSessionHolder holder,
        RunLogStream stream)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(holder);
        _inner = inner;
        _holder = holder;
        _stream = stream;
    }

    /// <inheritdoc />
    public override Encoding Encoding => _inner.Encoding;

    /// <inheritdoc />
    public override void Write(char value)
    {
        lock (_gate)
        {
            _inner.Write(value);
            Append(value);
        }
    }

    /// <inheritdoc />
    public override void Write(string? value)
    {
        lock (_gate)
        {
            _inner.Write(value);
            AppendText(value);
        }
    }

    /// <inheritdoc />
    public override void WriteLine(string? value)
    {
        lock (_gate)
        {
            _inner.WriteLine(value);
            AppendText(value);
            Append('\n');
        }
    }

    /// <inheritdoc />
    public override void Flush()
    {
        lock (_gate)
        {
            _inner.Flush();
        }
    }

    private void AppendText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (var character in value)
        {
            Append(character);
        }
    }

    private void Append(char value)
    {
        if (value != '\n')
        {
            _ = _buffer.Append(value);
            return;
        }

        var line = _buffer.ToString().TrimEnd('\r');
        _ = _buffer.Clear();
        if (line.StartsWith(RunnerContextProtocol.Prefix, StringComparison.Ordinal) ||
            line.StartsWith(RunnerDebugProtocol.Prefix, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _holder.Session?.WriteLine(_stream, line);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException)
        {
            // Run logging must never break runner output.
        }
    }
}
