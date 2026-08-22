using Google.Apis.Calendar.v3.Data;
using System.Globalization;

namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Maps Google Calendar API models to connector-owned data transfer objects.
/// </summary>
internal static class CalendarEventMapper
{
    public static CalendarEventDto Map(Event calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        return new CalendarEventDto(
            calendarEvent.Id ?? string.Empty,
            calendarEvent.Summary ?? string.Empty,
            calendarEvent.Description ?? string.Empty,
            ParseDateTime(calendarEvent.Start),
            ParseDateTime(calendarEvent.End),
            calendarEvent.Location);
    }

    private static DateTimeOffset ParseDateTime(EventDateTime? eventDateTime)
    {
        if (eventDateTime?.DateTimeDateTimeOffset is { } dateTime)
        {
            return dateTime;
        }

        // Google represents all-day events as dates without an offset; interpret them in the local calendar zone.
        if (eventDateTime?.Date is not null &&
            DateTime.TryParseExact(
                eventDateTime.Date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var allDayDate))
        {
            return new DateTimeOffset(
                allDayDate,
                TimeZoneInfo.Local.GetUtcOffset(allDayDate));
        }

        return DateTimeOffset.UnixEpoch;
    }
}
