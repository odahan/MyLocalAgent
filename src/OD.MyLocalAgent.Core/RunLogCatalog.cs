using System.Text;
using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements shared-read run-log enumeration and incremental tailing.
/// </summary>
public sealed class RunLogCatalog : IRunLogCatalog
{
    private const int TailProbeBytes = 8192;

    private readonly string _rootDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunLogCatalog"/> class.
    /// </summary>
    public RunLogCatalog()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunLogCatalog"/> class.
    /// </summary>
    /// <param name="rootDirectory">
    /// An optional run-log root override; defaults to
    /// <see cref="LocalAppPaths.RunLogsDirectory"/>.
    /// </param>
    public RunLogCatalog(string? rootDirectory)
    {
        _rootDirectory = rootDirectory ?? LocalAppPaths.RunLogsDirectory;
    }

    /// <inheritdoc />
    public IReadOnlyList<RunLogRunInfo> GetRuns()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        var runs = new List<RunLogRunInfo>();
        foreach (var taskDirectory in Directory.EnumerateDirectories(_rootDirectory))
        {
            foreach (var filePath in Directory.EnumerateFiles(
                         taskDirectory,
                         "*.jsonl",
                         SearchOption.TopDirectoryOnly))
            {
                var info = TryReadRunInfo(
                    filePath,
                    Path.GetFileName(taskDirectory));
                if (info is not null)
                {
                    runs.Add(info);
                }
            }
        }

        return runs
            .OrderByDescending(static run => run.StartedUtc)
            .ThenByDescending(static run => run.FilePath, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public RunLogReadResult ReadLines(string filePath, long offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        using var stream = OpenShared(filePath);
        if (offset >= stream.Length)
        {
            return new RunLogReadResult([], Math.Min(offset, stream.Length), null, null);
        }

        _ = stream.Seek(offset, SeekOrigin.Begin);
        var pending = new byte[stream.Length - offset];
        stream.ReadExactly(pending);

        var consumed = Array.LastIndexOf(pending, (byte)'\n') + 1;
        if (consumed == 0)
        {
            return new RunLogReadResult([], offset, null, null);
        }

        var lines = new List<RunLogLine>();
        DateTimeOffset? endedUtc = null;
        int? exitCode = null;
        foreach (var rawLine in Encoding.UTF8
                     .GetString(pending, 0, consumed)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var record = TryParse(rawLine);
            if (record is null)
            {
                continue;
            }

            switch (GetKind(record.Value))
            {
                case "line" when TryReadLine(record.Value, out var line):
                    lines.Add(line);
                    break;
                case "end":
                    endedUtc = GetDateTime(record.Value, "endedUtc");
                    exitCode = GetInt(record.Value, "exitCode");
                    break;
                default:
                    break;
            }
        }

        return new RunLogReadResult(lines, offset + consumed, endedUtc, exitCode);
    }

    private static RunLogRunInfo? TryReadRunInfo(string filePath, string fallbackTaskId)
    {
        try
        {
            using var stream = OpenShared(filePath);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var start = TryParse(reader.ReadLine());
            if (start is null || GetKind(start.Value) != "start")
            {
                return null;
            }

            var startedUtc = GetDateTime(start.Value, "startedUtc");
            if (startedUtc is null)
            {
                return null;
            }

            var (endedUtc, exitCode) = ReadTailEnd(stream);
            return new RunLogRunInfo(
                filePath,
                GetString(start.Value, "taskId") ?? fallbackTaskId,
                startedUtc.Value,
                endedUtc,
                exitCode);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (DateTimeOffset? EndedUtc, int? ExitCode) ReadTailEnd(
        FileStream stream)
    {
        var probeLength = (int)Math.Min(TailProbeBytes, stream.Length);
        _ = stream.Seek(-probeLength, SeekOrigin.End);
        var tail = new byte[probeLength];
        stream.ReadExactly(tail);
        var segments = Encoding.UTF8
            .GetString(tail)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            var record = TryParse(segments[index]);
            if (record is null)
            {
                continue;
            }

            return GetKind(record.Value) == "end"
                ? (GetDateTime(record.Value, "endedUtc"), GetInt(record.Value, "exitCode"))
                : (null, null);
        }

        return (null, null);
    }

    private static FileStream OpenShared(string filePath) =>
        new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

    private static JsonElement? TryParse(string? rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawLine);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadLine(JsonElement record, out RunLogLine line)
    {
        line = null!;
        var timestamp = GetDateTime(record, "timestampUtc");
        var text = GetString(record, "text");
        if (timestamp is null || text is null)
        {
            return false;
        }

        line = new RunLogLine(
            timestamp.Value,
            GetString(record, "stream") == "err"
                ? RunLogStream.Error
                : RunLogStream.Output,
            text);
        return true;
    }

    private static string GetKind(JsonElement record) =>
        GetString(record, "kind") ?? string.Empty;

    private static string? GetString(JsonElement record, string name) =>
        record.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? GetDateTime(JsonElement record, string name) =>
        record.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        value.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : null;

    private static int? GetInt(JsonElement record, string name) =>
        record.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}
