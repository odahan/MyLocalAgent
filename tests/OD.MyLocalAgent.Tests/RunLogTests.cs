using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the per-execution run-log contract: round-trip, tailing, retention,
/// protocol filtering, and malformed-line tolerance (§19).
/// </summary>
public sealed class RunLogTests : IDisposable
{
    private readonly string _rootDirectory =
        Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies start/line/end round-trip with catch-up and incremental tail reads.
    /// </summary>
    [Fact]
    public void SessionRoundTripSupportsIncrementalTailing()
    {
        var catalog = new RunLogCatalog(_rootDirectory);
        using var session = RunLogSession.Start(
            "demo-task",
            5,
            TimeProvider.System,
            _rootDirectory);
        session.WriteLine(RunLogStream.Output, "first");
        session.WriteLine(RunLogStream.Error, "second");

        var catchUp = catalog.ReadLines(session.FilePath, 0);
        session.WriteLine(RunLogStream.Output, "third");
        var tail = catalog.ReadLines(session.FilePath, catchUp.NewOffset);
        var midRun = catalog.GetRuns();
        session.Complete(3);
        var final = catalog.ReadLines(session.FilePath, tail.NewOffset);
        var completed = catalog.GetRuns();

        catchUp.Lines.Should().HaveCount(2);
        catchUp.Lines[0].Should().Match<RunLogLine>(
            static line => line.Stream == RunLogStream.Output && line.Text == "first");
        catchUp.Lines[1].Stream.Should().Be(RunLogStream.Error);
        catchUp.ExitCode.Should().BeNull();
        tail.Lines.Should().ContainSingle(static line => line.Text == "third");
        midRun.Should().ContainSingle();
        midRun[0].TaskId.Should().Be("demo-task");
        midRun[0].EndedUtc.Should().BeNull();
        final.Lines.Should().BeEmpty();
        final.ExitCode.Should().Be(3);
        final.EndedUtc.Should().NotBeNull();
        completed.Should().ContainSingle();
        completed[0].ExitCode.Should().Be(3);
    }

    /// <summary>
    /// Verifies the resolved model information is the first visible task-log line.
    /// </summary>
    [Fact]
    public void ModelInfoHeaderIsFirstVisibleLine()
    {
        const string modelInfo =
            "ModelName qwen3:8b - Temp 0.2 - TopP 0.9 - Context 32768";
        using var session = RunLogSession.Start(
            "model-info-task",
            5,
            TimeProvider.System,
            _rootDirectory);

        session.WriteModelInfo(modelInfo);
        session.WriteLine(RunLogStream.Output, "task output");

        var lines = new RunLogCatalog(_rootDirectory)
            .ReadLines(session.FilePath, 0)
            .Lines;

        lines.Select(static line => line.Text)
            .Should().Equal(modelInfo, "task output");
    }

    /// <summary>
    /// Verifies the tee mirrors completed lines but never the private protocol channels,
    /// while the inner writer receives everything unchanged.
    /// </summary>
    [Fact]
    public void TeeWriterFiltersPrivateProtocolLines()
    {
        var holder = new RunLogSessionHolder();
        using var inner = new StringWriter();
        using var tee = new RunLogTeeWriter(inner, holder, RunLogStream.Output);
        using (var session = RunLogSession.Start(
                   "tee-task",
                   5,
                   TimeProvider.System,
                   _rootDirectory))
        {
            holder.Session = session;
            tee.WriteLine("visible");
            tee.WriteLine(RunnerContextProtocol.Encode("""{"steps":[]}"""));
            tee.WriteLine(RunnerDebugProtocol.Encode("category", "name", null));
            tee.Write("split ");
            tee.Write("line\n");
            holder.Session = null;
            session.Complete(0);
        }

        var lines = new RunLogCatalog(_rootDirectory)
            .ReadLines(GetSingleFile("tee-task"), 0)
            .Lines;

        lines.Select(static line => line.Text)
            .Should().Equal("visible", "split line");
        inner.ToString().Should()
            .Contain("visible")
            .And.Contain(RunnerContextProtocol.Prefix)
            .And.Contain(RunnerDebugProtocol.Prefix);
    }

    /// <summary>
    /// Verifies retention keeps only the configured number of files per task.
    /// </summary>
    [Fact]
    public void RetentionPrunesOldestRunLogs()
    {
        var timeProvider = new SteppingTimeProvider();
        for (var run = 0; run < 4; run++)
        {
            using var session = RunLogSession.Start(
                "retained-task",
                2,
                timeProvider,
                _rootDirectory);
            session.WriteLine(RunLogStream.Output, $"run {run}");
            session.Complete(0);
        }

        var files = Directory
            .GetFiles(Path.Combine(_rootDirectory, "retained-task"), "*.jsonl")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        files.Should().HaveCount(2);
        var catalog = new RunLogCatalog(_rootDirectory);
        catalog.ReadLines(files[^1], 0).Lines
            .Should().ContainSingle(static line => line.Text == "run 3");
    }

    /// <summary>
    /// Verifies a truncated trailing record (crash mid-write) is skipped without error
    /// and the run still lists as in progress.
    /// </summary>
    [Fact]
    public void MalformedTrailingLineIsTolerated()
    {
        var directory = Path.Combine(_rootDirectory, "crashed-task");
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "20260801-060000-000.jsonl");
        File.WriteAllText(
            filePath,
            """{"kind":"start","taskId":"crashed-task","startedUtc":"2026-08-01T06:00:00Z","processId":1,"version":"1.0.0"}""" +
            "\n" +
            """{"kind":"line","timestampUtc":"2026-08-01T06:00:01Z","stream":"out","text":"ok"}""" +
            "\n" +
            """{"kind":"line","timestampUtc":"2026-08-01T06:0""");

        var catalog = new RunLogCatalog(_rootDirectory);
        var read = catalog.ReadLines(filePath, 0);
        var runs = catalog.GetRuns();

        read.Lines.Should().ContainSingle(static line => line.Text == "ok");
        read.ExitCode.Should().BeNull();
        runs.Should().ContainSingle();
        runs[0].EndedUtc.Should().BeNull();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private string GetSingleFile(string taskId) =>
        Directory
            .GetFiles(Path.Combine(_rootDirectory, taskId), "*.jsonl")
            .Should().ContainSingle().Subject;

    private sealed class SteppingTimeProvider : TimeProvider
    {
        private DateTimeOffset _now =
            new(2026, 8, 1, 6, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            _now = _now.AddSeconds(1);
            return _now;
        }
    }
}
