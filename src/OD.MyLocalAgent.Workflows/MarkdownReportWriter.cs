using System.Text;
using System.Text.Json;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Writes per-task Markdown reports and a process-safe JSON Lines index.
/// </summary>
public sealed class MarkdownReportWriter : IReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _reportsDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _indexGate = new(1, 1);

    /// <summary>
    /// Initializes a writer using the configured workspace paths.
    /// </summary>
    /// <param name="workspacePaths">The configured application workspace paths.</param>
    /// <param name="timeProvider">The time provider used for lock retries.</param>
    public MarkdownReportWriter(
        WorkspacePaths workspacePaths,
        TimeProvider timeProvider)
        : this(
            workspacePaths?.ReportsDirectory
                ?? throw new ArgumentNullException(nameof(workspacePaths)),
            timeProvider)
    {
    }

    /// <summary>
    /// Initializes a writer using an explicit reports directory.
    /// </summary>
    /// <param name="reportsDirectory">The reports root directory.</param>
    /// <param name="timeProvider">The time provider used for lock retries.</param>
    public MarkdownReportWriter(
        string reportsDirectory,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportsDirectory);
        _reportsDirectory = Path.GetFullPath(reportsDirectory);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<ReportWriteResult> WriteAsync(
        ScheduledTask task,
        WorkflowResult result,
        string providerName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var reportsDirectory = _reportsDirectory;
        var taskDirectory = Path.Combine(reportsDirectory, task.Id);
        Directory.CreateDirectory(taskDirectory);
        var reportPath = Path.Combine(
            taskDirectory,
            $"{result.StartedUtc.UtcDateTime:yyyyMMdd-HHmmss}.md");
        var markdown = BuildMarkdown(task, result, providerName);
        await File.WriteAllTextAsync(
            reportPath,
            markdown,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);

        var indexEntry = new
        {
            taskId = task.Id,
            startedUtc = result.StartedUtc.ToUniversalTime(),
            durationMs = Math.Max(0, result.Duration.TotalMilliseconds),
            status = result.Success ? "Success" : "Failure",
            reportPath,
        };
        var indexLine = JsonSerializer.Serialize(indexEntry, SerializerOptions) +
            Environment.NewLine;
        await AppendIndexAsync(
            Path.Combine(reportsDirectory, "index.jsonl"),
            indexLine,
            cancellationToken).ConfigureAwait(false);
        return new ReportWriteResult(reportPath, markdown);
    }

    private static string BuildMarkdown(
        ScheduledTask task,
        WorkflowResult result,
        string providerName)
    {
        var status = result.Success ? "Success" : "Failure";
        var builder = new StringBuilder();
        builder.AppendLine("# OD.MyLocalAgent execution report");
        builder.AppendLine();
        builder.AppendLine($"- Task ID: `{task.Id}`");
        builder.AppendLine($"- Task name: {EscapeInline(task.Name)}");
        builder.AppendLine($"- Started UTC: `{result.StartedUtc.ToUniversalTime():O}`");
        builder.AppendLine($"- Ended UTC: `{result.EndedUtc.ToUniversalTime():O}`");
        builder.AppendLine($"- Provider: {EscapeInline(providerName)}");
        builder.AppendLine($"- Status: **{status}**");
        builder.AppendLine();
        if (result.Steps.Count > 0)
        {
            builder.AppendLine("## Steps");
            builder.AppendLine();
            builder.AppendLine("| Step | Alias | Status | Attempts | Duration (ms) | Output bytes | Error |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---|");
            foreach (var step in result.Steps)
            {
                builder.AppendLine(
                    $"| `{EscapeInline(step.Id)}` | `{EscapeInline(step.Alias)}` | {step.Status} | {step.Attempts} | {step.Duration.TotalMilliseconds:F0} | {step.OutputSize} | {EscapeInline(step.ErrorMessage ?? string.Empty)} |");
            }
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            builder.AppendLine();
            builder.AppendLine("## Error");
            builder.AppendLine();
            AppendIndented(builder, result.ErrorMessage);
        }

        if (result.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Warnings");
            builder.AppendLine();
            foreach (var warning in result.Warnings)
            {
                builder.Append("- `");
                builder.Append(EscapeInline(warning.Code));
                builder.Append("` (");
                builder.Append(warning.OccurrenceCount);
                builder.Append("): ");
                builder.AppendLine(EscapeInline(warning.Message));
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Final output");
        builder.AppendLine();
        if (string.IsNullOrEmpty(result.FinalText))
        {
            builder.AppendLine("_No final output._");
        }
        else
        {
            builder.Append(result.FinalText);
            if (!result.FinalText.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private async Task AppendIndexAsync(
        string indexPath,
        string line,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            const int maximumAttempts = 20;
            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = new FileStream(
                        indexPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.Read,
                        4096,
                        FileOptions.Asynchronous | FileOptions.WriteThrough);
                    stream.Seek(0, SeekOrigin.End);
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (IOException) when (attempt < maximumAttempts)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(25 * attempt),
                        _timeProvider,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _indexGate.Release();
        }
    }

    private static string EscapeInline(string value) =>
        value
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static void AppendIndented(StringBuilder builder, string value)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            builder.Append("    ");
            builder.AppendLine(line);
        }
    }
}
