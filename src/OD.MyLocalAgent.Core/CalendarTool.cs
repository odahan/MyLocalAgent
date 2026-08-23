using System.ComponentModel;
using Microsoft.Extensions.Logging;
using OD.MyLocalAgent.Connectors;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes compact-JSON Google Calendar connector operations.
/// </summary>
[ToolGroup(
    "calendar",
    "Calendar",
    "Read or create Google Calendar events through the configured account.",
    200,
    Activation = ToolActivationKind.Singleton,
    ServiceContract = typeof(ICalendarTool))]
public sealed class CalendarTool : ICalendarTool
{
    private readonly ICalendarConnector _connector;
    private readonly ILogger<CalendarTool> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="CalendarTool"/> class.
    /// </summary>
    public CalendarTool(
        ICalendarConnector connector,
        ILogger<CalendarTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _connector = connector;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [Tool("calendar.today", "Reads today's Google Calendar events.", 100)]
    public Task<string> TodayAsync(CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "calendar.today",
            new { },
            async () =>
            {
                var events = await _connector.GetTodaysEventsAsync(cancellationToken)
                    .ConfigureAwait(false);
                return ToolJson.Serialize(events);
            },
            SummarizeReadResult);

    /// <inheritdoc />
    [Tool("calendar.read", "Reads Google Calendar events for the current local day, week, month, or year.", 200)]
    public Task<string> ReadAsync(
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Calendar period: Day, Week, Month, or Year.", Example = "Week")] CalendarPeriod period,
        CancellationToken cancellationToken = default) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "calendar.read",
            new { period },
            async () =>
            {
                var events = await _connector.GetEventsAsync(period, cancellationToken)
                    .ConfigureAwait(false);
                return ToolJson.Serialize(events);
            },
            SummarizeReadResult);

    /// <inheritdoc />
    [Tool("calendar.create", "Creates a Google Calendar event.", 300)]
    public Task<string> CreateAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Event summary.", Example = "Team sync")] string summary,
        [ToolArgument(WorkflowArgumentKind.String, "Event start time with offset.", Example = "2026-01-15T09:00:00+01:00", IsExcludedFromWorkflowTextEditor = true)] DateTimeOffset start,
        [ToolArgument(WorkflowArgumentKind.String, "Event end time with offset.", Example = "2026-01-15T10:00:00+01:00", IsExcludedFromWorkflowTextEditor = true)] DateTimeOffset end,
        [ToolArgument(WorkflowArgumentKind.String, "Event description.", Example = "Weekly team synchronisation.")] string description,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(description);
        if (end <= start)
        {
            throw new ArgumentException("The event end time must be after its start time.", nameof(end));
        }

        return ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "calendar.create",
            new
            {
                summaryLength = summary.Length,
                start,
                end,
                descriptionLength = description.Length,
            },
            async () =>
            {
                var calendarEvent = await _connector.CreateEventAsync(
                    summary,
                    start,
                    end,
                    description,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(calendarEvent);
            });
    }

    private static string? SummarizeReadResult(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
            ? $"Returned {document.RootElement.GetArrayLength()} calendar event(s)."
            : null;
    }
}
