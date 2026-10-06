using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Writes one per-execution runner output log as flushed, shareable JSONL (§19.2).
/// </summary>
public sealed class RunLogSession : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly StreamWriter _writer;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private bool _completed;
    private bool _disposed;

    private RunLogSession(
        string filePath,
        StreamWriter writer,
        TimeProvider timeProvider)
    {
        FilePath = filePath;
        _writer = writer;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets the absolute run-log file path.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Starts a new run-log session for one task execution.
    /// </summary>
    /// <param name="taskId">The task id.</param>
    /// <param name="maxRunLogsPerTask">The per-task retained file count.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="rootDirectory">
    /// The run-log root directory.
    /// </param>
    /// <returns>The opened session.</returns>
    public static RunLogSession Start(
        string taskId,
        int maxRunLogsPerTask,
        TimeProvider timeProvider,
        string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRunLogsPerTask, 1);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var directory = Path.Combine(
            Path.GetFullPath(rootDirectory),
            taskId);
        Directory.CreateDirectory(directory);
        Prune(directory, maxRunLogsPerTask - 1);

        var startedUtc = timeProvider.GetUtcNow();
        var writer = CreateWriter(directory, startedUtc, out var filePath);
        var session = new RunLogSession(filePath, writer, timeProvider);
        session.WriteRecord(
            new
            {
                kind = "start",
                taskId,
                startedUtc,
                processId = Environment.ProcessId,
                version = typeof(RunLogSession).Assembly.GetName().Version?.ToString(3)
                    ?? "0.0.0",
            });
        return session;
    }

    /// <summary>
    /// Appends one output line.
    /// </summary>
    /// <param name="stream">The origin stream.</param>
    /// <param name="text">The line text without its terminator.</param>
    public void WriteLine(RunLogStream stream, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        WriteRecord(
            new
            {
                kind = "line",
                timestampUtc = _timeProvider.GetUtcNow(),
                stream = stream == RunLogStream.Error ? "err" : "out",
                text,
            });
    }

    /// <summary>
    /// Writes the resolved model-information header as the first visible task-log line.
    /// </summary>
    /// <param name="modelInfo">The resolved <c>{{ModelInfo}}</c> value.</param>
    public void WriteModelInfo(string modelInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelInfo);
        WriteLine(RunLogStream.Output, modelInfo);
    }

    /// <summary>
    /// Writes the timing analysis report as visible task-log lines.
    /// </summary>
    /// <param name="report">The formatted timing report produced by <see cref="TaskTimingReport"/>.</param>
    public void WriteTimingReport(string report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(report);
        foreach (var line in report.Split(
                     Environment.NewLine,
                     StringSplitOptions.None))
        {
            WriteLine(RunLogStream.Output, line);
        }
    }

    /// <summary>
    /// Writes the terminal end record. Subsequent calls are ignored.
    /// </summary>
    /// <param name="exitCode">The runner exit code.</param>
    public void Complete(int exitCode)
    {
        lock (_gate)
        {
            if (_completed || _disposed)
            {
                return;
            }

            _completed = true;
            _writer.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        kind = "end",
                        endedUtc = _timeProvider.GetUtcNow(),
                        exitCode,
                        hasWarnings = _hasWarnings,
                    },
                    JsonOptions));
            _writer.Flush();
        }
    }

    private bool _hasWarnings;

    /// <summary>Marks successful execution as completed with warnings in the persisted end record.</summary>
    public void MarkWarnings()
    {
        lock (_gate) _hasWarnings = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    private void WriteRecord(object record)
    {
        lock (_gate)
        {
            if (_completed || _disposed)
            {
                return;
            }

            _writer.WriteLine(JsonSerializer.Serialize(record, JsonOptions));
            _writer.Flush();
        }
    }

    private static StreamWriter CreateWriter(
        string directory,
        DateTimeOffset startedUtc,
        out string filePath)
    {
        var stamp = startedUtc.UtcDateTime.ToString(
            "yyyyMMdd-HHmmss-fff",
            CultureInfo.InvariantCulture);
        for (var attempt = 0; ; attempt++)
        {
            filePath = Path.Combine(
                directory,
                attempt == 0 ? $"{stamp}.jsonl" : $"{stamp}-{attempt}.jsonl");
            try
            {
                var stream = new FileStream(
                    filePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read);
                return new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (IOException) when (attempt < 10 && File.Exists(filePath))
            {
                // Another run of the same task started within the same millisecond.
            }
        }
    }

    private static void Prune(string directory, int keepCount)
    {
        var stale = Directory
            .EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .Skip(keepCount);
        foreach (var path in stale)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // A locked or protected file is retained; retention retries next run.
            }
        }
    }
}
