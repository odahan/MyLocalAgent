using FluentAssertions;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Tests provider-reported token usage in execution timing reports.
/// </summary>
public sealed class TaskTimingReportTests
{
    /// <summary>
    /// Verifies totals and individual model calls are rendered.
    /// </summary>
    [Fact]
    public void Build_includes_global_and_per_call_provider_reported_token_usage()
    {
        var report = TaskTimingReport.Build(
            new DateTimeOffset(2026, 8, 9, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 9, 8, 0, 2, TimeSpan.Zero),
            [],
            [
                new TaskLlmUsageEntry("draft", 120, 30, 150),
                new TaskLlmUsageEntry("review", 80, 20, 100),
            ]);

        report.Should().Contain("LLM token usage (provider-reported):");
        report.Should().Contain("Total (2 calls): input 200, output 50, total 250");
        report.Should().Contain("draft: input 120, output 30, total 150");
        report.Should().Contain("review: input 80, output 20, total 100");
    }

    /// <summary>
    /// Verifies unknown provider usage is not estimated.
    /// </summary>
    [Fact]
    public void Build_marks_missing_provider_usage_without_estimating_tokens()
    {
        var report = TaskTimingReport.Build(
            new DateTimeOffset(2026, 8, 9, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 9, 8, 0, 1, TimeSpan.Zero),
            [],
            [new TaskLlmUsageEntry("ai.prompt", null, null, null)]);

        report.Should().Contain("Total (1 calls): input not fully reported, output not fully reported, total not fully reported");
        report.Should().Contain("ai.prompt: input not reported, output not reported, total not reported");
    }
}
