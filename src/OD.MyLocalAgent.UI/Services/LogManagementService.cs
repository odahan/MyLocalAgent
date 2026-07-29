using System.Globalization;
using System.IO;
using System.Text;
using OD.MyLocalAgent.Core;
using Serilog.Events;

namespace OD.MyLocalAgent.UI.Services;

internal sealed class LogManagementService : ILogManagementService
{
    private readonly IInMemoryLogSink _logSink;
    private readonly TimeProvider _timeProvider;

    public LogManagementService(
        IInMemoryLogSink logSink,
        TimeProvider timeProvider)
    {
        _logSink = logSink;
        _timeProvider = timeProvider;
    }

    public async Task ExportAsync(
        string destinationPath,
        IReadOnlyList<LogEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(events);

        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var logEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer
                .WriteLineAsync(Format(logEvent).AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(LocalAppPaths.LogsDirectory);
        var rollingFiles = Directory
            .EnumerateFiles(
                LocalAppPaths.LogsDirectory,
                "application-*.log",
                SearchOption.TopDirectoryOnly)
            .ToArray();
        if (rollingFiles.Length > 0)
        {
            var archiveDirectory = Path.Combine(
                LocalAppPaths.LogsDirectory,
                "archive",
                _timeProvider.GetLocalNow().ToString(
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture));
            Directory.CreateDirectory(archiveDirectory);
            foreach (var sourcePath in rollingFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var archivePath = Path.Combine(
                    archiveDirectory,
                    Path.GetFileName(sourcePath));
                await CopySharedFileAsync(
                        sourcePath,
                        archivePath,
                        cancellationToken)
                    .ConfigureAwait(false);
                await TruncateSharedFileAsync(sourcePath, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        _logSink.Clear();
    }

    private static string Format(LogEvent logEvent)
    {
        var source = GetScalar(logEvent, "SourceContext");
        var taskId = GetScalar(logEvent, "TaskId");
        var builder = new StringBuilder();
        _ = builder
            .Append(logEvent.Timestamp.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss.fff zzz",
                CultureInfo.InvariantCulture))
            .Append(" [")
            .Append(logEvent.Level)
            .Append("] ");
        if (!string.IsNullOrEmpty(source))
        {
            _ = builder.Append('[').Append(source).Append("] ");
        }

        if (!string.IsNullOrEmpty(taskId))
        {
            _ = builder.Append("[Task ").Append(taskId).Append("] ");
        }

        _ = builder.Append(logEvent.RenderMessage(CultureInfo.InvariantCulture));
        if (logEvent.Exception is not null)
        {
            _ = builder.AppendLine().Append(logEvent.Exception);
        }

        return builder.ToString();
    }

    private static string GetScalar(LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out var value) &&
        value is ScalarValue scalar
            ? Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;

    private static async Task CopySharedFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task TruncateSharedFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        stream.SetLength(0);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
