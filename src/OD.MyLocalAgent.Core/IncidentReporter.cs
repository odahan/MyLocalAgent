namespace OD.MyLocalAgent.Core;

/// <summary>
/// Builds, persists, and announces application incidents.
/// </summary>
public sealed class IncidentReporter : IIncidentReporter
{
    private readonly IIncidentLogService _logService;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncidentReporter"/> class.
    /// </summary>
    /// <param name="logService">The durable incident log.</param>
    /// <param name="timeProvider">The application clock.</param>
    public IncidentReporter(
        IIncidentLogService logService,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(logService);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _logService = logService;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public event EventHandler<IncidentReportedEventArgs>? IncidentReported;

    /// <inheritdoc />
    public Task<string> ReportAsync(
        Exception exception,
        string source,
        CancellationToken cancellationToken) =>
        ReportAsync(
            exception,
            source,
            IncidentSeverity.Error,
            cancellationToken);

    /// <inheritdoc />
    public async Task<string> ReportAsync(
        Exception exception,
        string source,
        IncidentSeverity severity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var correlationId = Guid.NewGuid().ToString("N");
        var incident = new IncidentRecord(
            _timeProvider.GetUtcNow(),
            severity,
            source,
            exception.Message,
            exception.ToString(),
            correlationId);

        await _logService.AppendAsync(incident, cancellationToken).ConfigureAwait(false);
        IncidentReported?.Invoke(this, new IncidentReportedEventArgs(incident));
        return correlationId;
    }
}
