using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Runner;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies recovery preserves reservations and immutable exports without rerunning collection.</summary>
public sealed class EditorialRecoveryTests
{
    /// <summary>Uses the original run identity and date while keeping the stored workflow unchanged.</summary>
    [Fact]
    public void ReservedRunResumesOnlyDraftAndExport()
    {
        var task = Sample();
        var original = ScheduledTaskSerializer.Serialize(task);
        var state = State("Reserved");
        var recovery = EditorialRecoveryTaskBuilder.Build(task, state, TimeZoneInfo.Utc);

        recovery.Workflow.Select(step => step.EffectiveId).Should().Equal(
            "draftApprovedSubjects", "prepareEditorialReport", "exportEditorialReport", "confirmEditorialExport");
        var prompt = recovery.Workflow[0].Args.GetProperty("prompt").GetString();
        prompt.Should().Contain("subject-one").And.Contain("Original fact")
            .And.NotContain("{{reserveEditorialSubjects.output}}");
        recovery.Workflow[1].Args.GetProperty("runId").GetString().Should().Be(state.Reservation.RunId);
        recovery.Workflow[2].Args.GetProperty("path").GetString().Should()
            .Contain("2026\\10\\VeilleAuto-20261019-" + state.Reservation.RunId);
        recovery.Workflow[0].GenerationOptions!.ReasoningEnabled.Should().BeFalse();
        ScheduledTaskSerializer.Serialize(task).Should().Be(original);
    }

    /// <summary>Exports the persisted Markdown without calling the model a second time.</summary>
    [Fact]
    public void PreparedRunUsesDurableMarkdown()
    {
        var state = State("ReadyToExport") with { ReportPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".md") };
        var recovery = EditorialRecoveryTaskBuilder.Build(Sample(), state, TimeZoneInfo.Utc);
        recovery.Workflow.Select(step => step.Step).Should().Equal("filesystem.write", "sqlite.confirmEditorialExport");
        recovery.Workflow[0].Args.GetProperty("content").GetString().Should().Be(state.Markdown);
        recovery.Workflow[0].Args.GetProperty("mode").GetString().Should().Be("createNew");
    }

    /// <summary>Never overwrites an existing report; hash verification remains the confirmation tool's responsibility.</summary>
    [Theory]
    [InlineData("ReadyToExport")]
    [InlineData("Exported")]
    public void ExistingExportIsOnlyConfirmed(string status)
    {
        var path = Path.GetTempFileName();
        try
        {
            var state = State(status) with { ReportPath = path };
            var recovery = EditorialRecoveryTaskBuilder.Build(Sample(), state, TimeZoneInfo.Utc);
            recovery.Workflow.Should().ContainSingle().Which.Step.Should().Be("sqlite.confirmEditorialExport");
        }
        finally { File.Delete(path); }
    }

    /// <summary>Fails closed for unknown or terminal failed state.</summary>
    [Fact]
    public void FailedRunCannotBeRedrafted()
    {
        var action = () => EditorialRecoveryTaskBuilder.Build(Sample(), State("Failed"), TimeZoneInfo.Utc);
        action.Should().Throw<InvalidOperationException>();
    }

    /// <summary>Rejects malformed recovery ids before opening application services.</summary>
    [Fact]
    public void RecoveryCommandRequiresValidRunIdentity()
    {
        var root = RunnerCommandFactory.CreateRootCommand();
        root.Parse(["resume-editorial", "--task", "editorial-watch-daily-db", "--run-id", "bad"]).Errors.Should().NotBeEmpty();
        root.Parse(["resume-editorial", "--task", "editorial-watch-daily-db", "--run-id", State("Reserved").Reservation.RunId])
            .Errors.Should().BeEmpty();
    }

    private static EditorialRecoveryState State(string status) => new(
        new EditorialReservationResult("d7cbe9a55274477baf716c27a83f6d06", status,
            [new ReservedEditorialProposal("subject-one", 70, "Original title", "Original fact",
                [new EditorialSource("source-one", "https://example.org/fact")], EditorialNoveltyDecision.New)],
            [], "embedding-test", 3, "editorial-event-v2", 0),
        DateTimeOffset.Parse("2026-10-19T16:00:00Z"), "# Durable report", null);

    private static ScheduledTask Sample()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OD.MyLocalAgent.sln")))
            directory = directory.Parent;
        return ScheduledTaskSerializer.Deserialize(File.ReadAllText(Path.Combine(directory!.FullName,
            "Docs", "samples", "editorial-watch-daily-db.json")));
    }
}
