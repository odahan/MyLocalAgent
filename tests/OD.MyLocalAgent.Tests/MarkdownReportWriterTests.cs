using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Workflows;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies Markdown report and JSON Lines index persistence.
/// </summary>
public sealed class MarkdownReportWriterTests : IDisposable
{
    private readonly string _directoryPath =
        Path.Combine(
            Path.GetTempPath(),
            "OD.MyLocalAgent.Tests",
            Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies report naming, required header content, and one appended index line.
    /// </summary>
    [Fact]
    public async Task WritesNamedMarkdownAndAppendsIndexEntry()
    {
        var started = new DateTimeOffset(
            2026,
            7,
            29,
            10,
            11,
            12,
            TimeSpan.Zero);
        var step = new WorkflowStepResult(
            "read",
            "filesystem.read",
            WorkflowStepStatus.Success,
            """{"content":"hello"}""",
            null,
            null,
            started,
            TimeSpan.FromMilliseconds(125),
            1);
        var result = new WorkflowResult(
            true,
            [step],
            "final text",
            started,
            started.AddSeconds(2));
        var task = new ScheduledTask
        {
            Id = "report-test",
            Name = "Report test",
            Schedule = "0 7 * * *",
            Prompt = "prompt",
            Tools = [ToolGroups.FileSystem],
        };
        var writer = new MarkdownReportWriter(
            _directoryPath,
            TimeProvider.System);

        var written = await writer.WriteAsync(
            task,
            result,
            "Ollama",
            CancellationToken.None);

        written.ReportPath.Should().Be(
            Path.Combine(
                _directoryPath,
                "report-test",
                "20260729-101112.md"));
        File.Exists(written.ReportPath).Should().BeTrue();
        written.Markdown.Should().Contain("Task ID: `report-test`");
        written.Markdown.Should().Contain("Task name: Report test");
        written.Markdown.Should().Contain("Started UTC: `2026-07-29T10:11:12.0000000+00:00`");
        written.Markdown.Should().Contain("Ended UTC: `2026-07-29T10:11:14.0000000+00:00`");
        written.Markdown.Should().Contain("Provider: Ollama");
        written.Markdown.Should().Contain("Status: **Success**");

        var indexLines = await File.ReadAllLinesAsync(
            Path.Combine(_directoryPath, "index.jsonl"));
        indexLines.Should().ContainSingle();
        using var indexDocument = JsonDocument.Parse(indexLines[0]);
        indexDocument.RootElement.GetProperty("taskId").GetString()
            .Should().Be("report-test");
        indexDocument.RootElement.GetProperty("durationMs").GetDouble()
            .Should().Be(2000);
        indexDocument.RootElement.GetProperty("status").GetString()
            .Should().Be("Success");
        indexDocument.RootElement.GetProperty("reportPath").GetString()
            .Should().Be(written.ReportPath);

        var secondResult = result with
        {
            StartedUtc = started.AddMinutes(1),
            EndedUtc = started.AddMinutes(1).AddSeconds(3),
        };
        _ = await writer.WriteAsync(
            task,
            secondResult,
            "Ollama",
            CancellationToken.None);
        var appendedLines = await File.ReadAllLinesAsync(
            Path.Combine(_directoryPath, "index.jsonl"));
        appendedLines.Should().HaveCount(2);
    }

    /// <summary>
    /// Deletes the isolated report directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
