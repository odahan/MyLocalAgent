using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class StubIncidentReporter : IIncidentReporter
{
    public List<(Exception Exception, string Source)> Reports { get; } = [];

    public event EventHandler<IncidentReportedEventArgs>? IncidentReported
    {
        add
        {
        }

        remove
        {
        }
    }

    public Task<string> ReportAsync(
        Exception exception,
        string source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reports.Add((exception, source));
        return Task.FromResult(Guid.NewGuid().ToString("N"));
    }

    public Task<string> ReportAsync(
        Exception exception,
        string source,
        IncidentSeverity severity,
        CancellationToken cancellationToken) =>
        ReportAsync(exception, source, cancellationToken);
}
