using System.ComponentModel;
using Microsoft.Extensions.Logging;
using OD.MyLocalAgent.Connectors;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes compact-JSON Google Calendar connector operations.
/// </summary>
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
    [Description("Reads today's Google Calendar events.")]
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
            });

    /// <inheritdoc />
    [Description("Creates a Google Calendar event.")]
    public Task<string> CreateAsync(
        [Description("Event summary.")] string summary,
        [Description("Event start time with offset.")] DateTimeOffset start,
        [Description("Event end time with offset.")] DateTimeOffset end,
        [Description("Event description.")] string description,
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
}
