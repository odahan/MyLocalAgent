using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the Runs page view model: list reconciliation, tailing, display limit,
/// clear, and export (§19.4).
/// </summary>
public sealed class RunMonitorViewModelTests
{
    private static readonly DateTimeOffset Started =
        new(2026, 8, 1, 6, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies refresh adds new runs, updates completion, and preserves the selection.
    /// </summary>
    [Fact]
    public void RefreshReconcilesRunListAndPreservesSelection()
    {
        var catalog = new Mock<IRunLogCatalog>();
        catalog.Setup(static c => c.GetRuns()).Returns(
        [
            new RunLogRunInfo(@"C:\logs\a\1.jsonl", "task-a", Started, null, null),
        ]);
        catalog
            .Setup(static c => c.ReadLines(It.IsAny<string>(), It.IsAny<long>()))
            .Returns(new RunLogReadResult([], 0, null, null));
        var viewModel = CreateViewModel(catalog.Object);

        viewModel.RefreshRuns();
        viewModel.SelectedRun = viewModel.Runs[0];
        catalog.Setup(static c => c.GetRuns()).Returns(
        [
            new RunLogRunInfo(@"C:\logs\b\2.jsonl", "task-b", Started.AddMinutes(5), null, null),
            new RunLogRunInfo(@"C:\logs\a\1.jsonl", "task-a", Started, Started.AddMinutes(1), 0),
        ]);
        viewModel.RefreshRuns();

        viewModel.Runs.Should().HaveCount(2);
        viewModel.Runs[0].TaskId.Should().Be("task-b");
        viewModel.Runs[0].Status.Should().Be("In progress");
        viewModel.Runs[1].Status.Should().Be("Success");
        viewModel.SelectedRun.Should().BeSameAs(viewModel.Runs[1]);
    }

    /// <summary>
    /// Verifies selection loads existing lines, polling appends only new lines, the
    /// display limit trims oldest lines first, and clear empties only the display.
    /// </summary>
    [Fact]
    public void SelectionTailsWithLineLimitAndClear()
    {
        var catalog = new Mock<IRunLogCatalog>();
        catalog.Setup(static c => c.GetRuns()).Returns(
        [
            new RunLogRunInfo(@"C:\logs\a\1.jsonl", "task-a", Started, null, null),
        ]);
        catalog
            .Setup(static c => c.ReadLines(@"C:\logs\a\1.jsonl", 0))
            .Returns(new RunLogReadResult(
                [.. Enumerable.Range(1, 5).Select(CreateLine)],
                500,
                null,
                null));
        catalog
            .Setup(static c => c.ReadLines(@"C:\logs\a\1.jsonl", 500))
            .Returns(new RunLogReadResult([CreateLine(6)], 600, Started.AddMinutes(1), 0));
        var viewModel = CreateViewModel(catalog.Object);
        viewModel.SelectedLineLimit = 3;

        viewModel.RefreshRuns();
        viewModel.SelectedRun = viewModel.Runs[0];
        var afterSelection = viewModel.Lines.ToArray();
        viewModel.PollOnce();
        var afterPoll = viewModel.Lines.ToArray();
        viewModel.ClearDisplay();

        afterSelection.Should().HaveCount(3);
        afterSelection[^1].Should().EndWith("line 5");
        afterPoll.Should().HaveCount(3);
        afterPoll[^1].Should().EndWith("line 6");
        viewModel.SelectedRun!.Status.Should().Be("Success");
        viewModel.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies export writes the complete run as plain text to the selected path.
    /// </summary>
    [Fact]
    public async Task ExportWritesSelectedRunAsPlainText()
    {
        var destination = Path.Combine(
            Path.GetTempPath(),
            $"OD.MyLocalAgent.Tests-{Guid.NewGuid():N}.txt");
        var catalog = new Mock<IRunLogCatalog>();
        catalog.Setup(static c => c.GetRuns()).Returns(
        [
            new RunLogRunInfo(@"C:\logs\a\1.jsonl", "task-a", Started, Started, 0),
        ]);
        catalog
            .Setup(static c => c.ReadLines(@"C:\logs\a\1.jsonl", It.IsAny<long>()))
            .Returns(new RunLogReadResult(
                [CreateLine(1), CreateLine(2)],
                200,
                Started,
                0));
        var desktop = new Mock<IDesktopInteractionService>();
        desktop
            .Setup(static d => d.SelectSavePath(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(destination);
        var viewModel = CreateViewModel(catalog.Object, desktop.Object);
        viewModel.RefreshRuns();
        viewModel.SelectedRun = viewModel.Runs[0];

        try
        {
            await viewModel.ExportCommand.ExecuteAsync(null);

            var lines = await File.ReadAllLinesAsync(destination);
            lines.Should().HaveCount(2);
            lines[0].Should().Contain("[out]").And.EndWith("line 1");
            viewModel.StatusText.Should().Contain(destination);
        }
        finally
        {
            File.Delete(destination);
        }
    }

    private static RunMonitorViewModel CreateViewModel(
        IRunLogCatalog catalog,
        IDesktopInteractionService? desktopInteraction = null) =>
        new(
            catalog,
            desktopInteraction ?? Mock.Of<IDesktopInteractionService>(),
            new WorkspacePaths(Path.Combine(
                Path.GetTempPath(),
                "OD.MyLocalAgent.Tests",
                "workspace")));

    private static RunLogLine CreateLine(int number) =>
        new(Started.AddSeconds(number), RunLogStream.Output, $"line {number}");
}
