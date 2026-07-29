using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies direct exception adaptation and durable JSONL incident operations.
/// </summary>
public sealed class IncidentPipelineTests : IDisposable
{
    private readonly string _directoryPath =
        Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies direct dispatcher and background failures produce durable correlated incidents.
    /// </summary>
    [Fact]
    public async Task DirectHandlersAppendNotifyAndSurviveReopen()
    {
        Directory.CreateDirectory(_directoryPath);
        var path = Path.Combine(_directoryPath, "incidents.jsonl");
        var firstLog = new JsonlIncidentLogService(path, TimeProvider.System);
        var reporter = new IncidentReporter(firstLog, TimeProvider.System);
        var notifications = new List<IncidentRecord>();
        reporter.IncidentReported += (_, args) => notifications.Add(args.Incident);
        AppExceptionHandlers.Initialize(reporter);

        var dispatcherHandled = AppExceptionHandlers.HandleDispatcherException(
            new InvalidOperationException("dispatcher failed"),
            "TaskEditor");
        var backgroundCorrelationId = AppExceptionHandlers.Handle(
            new ApplicationException("background failed"),
            "BackgroundTask");

        var secondLog = new JsonlIncidentLogService(path, TimeProvider.System);
        var incidents = await secondLog.ReadAsync(CancellationToken.None);

        dispatcherHandled.Should().BeTrue();
        notifications.Should().HaveCount(2);
        incidents.Should().HaveCount(2);
        incidents.Select(static incident => incident.CorrelationId)
            .Should().OnlyHaveUniqueItems()
            .And.Contain(backgroundCorrelationId);
        incidents.Should().Contain(
            incident => incident.StackTrace.Contains(
                "InvalidOperationException",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies export and clear preserve a valid JSONL snapshot.
    /// </summary>
    [Fact]
    public async Task ExportAndClearManageIncidentFile()
    {
        Directory.CreateDirectory(_directoryPath);
        var path = Path.Combine(_directoryPath, "incidents.jsonl");
        var exportPath = Path.Combine(_directoryPath, "export.jsonl");
        var log = new JsonlIncidentLogService(path, TimeProvider.System);
        await log.AppendAsync(
            new IncidentRecord(
                DateTimeOffset.Parse("2026-07-29T08:00:00Z"),
                IncidentSeverity.Error,
                "Test",
                "Message",
                "Stack",
                "correlation"),
            CancellationToken.None);

        await log.ExportAsync(exportPath, CancellationToken.None);
        await log.ClearAsync(CancellationToken.None);

        (await File.ReadAllTextAsync(exportPath)).Should().Contain("correlation");
        (await log.ReadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    /// <summary>
    /// Deletes the isolated test directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
