namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides low-level Google Calendar operations.
/// </summary>
public interface ICalendarConnector
{
    /// <summary>
    /// Gets today's calendar events.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized events.</returns>
    Task<IReadOnlyList<CalendarEventDto>> GetTodaysEventsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a calendar event.
    /// </summary>
    /// <param name="summary">The event summary.</param>
    /// <param name="start">The event start time.</param>
    /// <param name="end">The event end time.</param>
    /// <param name="description">The event description.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized created event.</returns>
    Task<CalendarEventDto> CreateEventAsync(
        string summary,
        DateTimeOffset start,
        DateTimeOffset end,
        string description,
        CancellationToken cancellationToken);
}
