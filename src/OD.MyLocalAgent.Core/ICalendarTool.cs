namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides Google Calendar operations to agents and workflows.
/// </summary>
public interface ICalendarTool
{
    /// <summary>
    /// Reads today's events.
    /// </summary>
    Task<string> TodayAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates an event.
    /// </summary>
    Task<string> CreateAsync(
        string summary,
        DateTimeOffset start,
        DateTimeOffset end,
        string description,
        CancellationToken cancellationToken);
}
