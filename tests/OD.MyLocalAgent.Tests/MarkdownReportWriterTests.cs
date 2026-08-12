using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
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
    /// Verifies non-failing warnings receive a separate section without changing index status.
    /// </summary>
    [Fact]
    public async Task WritesContentFreeWarningsWithoutChangingSuccessStatus()
    {
        var started = new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero);
        var result = new WorkflowResult(
            true,
            [],
            "ordinary final output",
            started,
            started.AddSeconds(1))
        {
            Warnings =
            [
                new ExecutionWarning(
                    AgentPromptPlaceholderWarning.Code,
                    "Warning: 1 agent prompt placeholder occurrence was left unchanged (1 unknown).",
                    1),
            ],
        };
        var task = new ScheduledTask
        {
            Id = "warning-report",
            Name = "Warning report",
            Schedule = "0 7 * * *",
            Prompt = "prompt",
            Tools = [],
        };
        var writer = new MarkdownReportWriter(_directoryPath, TimeProvider.System);

        var written = await writer.WriteAsync(
            task,
            result,
            "Test",
            CancellationToken.None);

        written.Markdown.Should().Contain("## Warnings");
        written.Markdown.Should().Contain(AgentPromptPlaceholderWarning.Code);
        written.Markdown.Should().Contain("## Final output");
        written.Markdown.Should().Contain("ordinary final output");
        using var index = JsonDocument.Parse(
            (await File.ReadAllLinesAsync(
                Path.Combine(_directoryPath, "index.jsonl"))).Single());
        index.RootElement.GetProperty("status").GetString().Should().Be("Success");
    }

    /// <summary>
    /// Verifies an agent-only result omits the workflow table and preserves its Markdown.
    /// </summary>
    [Fact]
    public async Task OmitsStepsForAgentOnlyResultAndPreservesFinalMarkdown()
    {
        var started = new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        var result = new WorkflowResult(
            true,
            [],
            "# Status\n\n- First finding\n- Second finding",
            started,
            started.AddSeconds(1));
        var task = new ScheduledTask
        {
            Id = "agent-only-report",
            Name = "Agent-only report",
            Schedule = "0 7 * * *",
            Prompt = "prompt",
            Tools = [ToolGroups.FileSystem],
        };
        var writer = new MarkdownReportWriter(_directoryPath, TimeProvider.System);

        var written = await writer.WriteAsync(
            task,
            result,
            "Test",
            CancellationToken.None);

        written.Markdown.Should().NotContain("## Steps");
        written.Markdown.Should().NotContain("No workflow steps were executed");
        written.Markdown.Should().Contain("## Final output");
        written.Markdown.Should().Contain("# Status");
        written.Markdown.Should().Contain("- First finding");
        written.Markdown.Should().NotContain("    # Status");
    }

    /// <summary>
    /// Verifies default report storage stays inside the configured file-system workspace.
    /// </summary>
    [Fact]
    public async Task DefaultWriterStoresReportsInsideConfiguredWorkspace()
    {
        var workspace = Path.Combine(_directoryPath, "workspace");
        var options = new Mock<IOptionsMonitor<FileSystemOptions>>();
        options.SetupGet(instance => instance.CurrentValue).Returns(
            new FileSystemOptions { RootPath = workspace });
        var writer = new MarkdownReportWriter(options.Object, TimeProvider.System);
        var started = new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        var task = new ScheduledTask
        {
            Id = "workspace-report",
            Name = "Workspace report",
            Schedule = "0 7 * * *",
            Prompt = "prompt",
            Tools = [],
        };
        var result = new WorkflowResult(
            true,
            [],
            "Report body.",
            started,
            started.AddSeconds(1));

        var written = await writer.WriteAsync(
            task,
            result,
            "Test",
            CancellationToken.None);

        written.ReportPath.Should().Be(
            Path.Combine(
                workspace,
                "reports",
                "workspace-report",
                "20260812-100000.md"));
        File.Exists(written.ReportPath).Should().BeTrue();
        File.Exists(Path.Combine(workspace, "reports", "index.jsonl")).Should().BeTrue();
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
