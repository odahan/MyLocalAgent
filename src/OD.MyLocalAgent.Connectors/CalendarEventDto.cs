namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Represents a normalized calendar event.
/// </summary>
/// <param name="Id">The provider event identifier.</param>
/// <param name="Summary">The event summary.</param>
/// <param name="Description">The event description.</param>
/// <param name="Start">The event start time.</param>
/// <param name="End">The event end time.</param>
/// <param name="Location">The optional event location.</param>
public sealed record CalendarEventDto(
    string Id,
    string Summary,
    string Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Location);
