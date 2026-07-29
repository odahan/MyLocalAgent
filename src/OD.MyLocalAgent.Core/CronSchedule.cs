using Cronos;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides pure cron occurrence calculations.
/// </summary>
public static class CronSchedule
{
    /// <summary>
    /// Calculates the first cron occurrence strictly after the supplied instant.
    /// </summary>
    /// <param name="cron">A five-field cron expression.</param>
    /// <param name="from">The starting instant.</param>
    /// <param name="timeZone">The schedule time zone.</param>
    /// <returns>The next occurrence, or <see langword="null"/> when none exists.</returns>
    public static DateTimeOffset? NextOccurrence(
        string cron,
        DateTimeOffset from,
        TimeZoneInfo timeZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cron);
        ArgumentNullException.ThrowIfNull(timeZone);
        return CronExpression.Parse(cron).GetNextOccurrence(
            from,
            timeZone,
            inclusive: false);
    }

    /// <summary>
    /// Calculates a fixed number of cron occurrences after the supplied instant.
    /// </summary>
    /// <param name="cron">A five-field cron expression.</param>
    /// <param name="from">The starting instant.</param>
    /// <param name="timeZone">The schedule time zone.</param>
    /// <param name="count">The maximum number of occurrences to return.</param>
    /// <returns>The occurrences in chronological order.</returns>
    public static IReadOnlyList<DateTimeOffset> NextOccurrences(
        string cron,
        DateTimeOffset from,
        TimeZoneInfo timeZone,
        int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cron);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var expression = CronExpression.Parse(cron);
        var occurrences = new List<DateTimeOffset>(count);
        var cursor = from;
        for (var index = 0; index < count; index++)
        {
            var next = expression.GetNextOccurrence(
                cursor,
                timeZone,
                inclusive: false);
            if (next is null)
            {
                break;
            }

            occurrences.Add(next.Value);
            cursor = next.Value;
        }

        return occurrences;
    }

    /// <summary>
    /// Describes a common five-field cron pattern in plain English.
    /// </summary>
    /// <param name="cron">A valid five-field cron expression.</param>
    /// <returns>A plain-English label, or <c>Custom schedule</c>.</returns>
    public static string DescribeCommonPattern(string cron)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cron);
        _ = CronExpression.Parse(cron);

        var fields = cron.Split(
            ' ',
            StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            return "Custom schedule";
        }

        var minute = fields[0];
        var hour = fields[1];
        var dayOfMonth = fields[2];
        var month = fields[3];
        var dayOfWeek = fields[4];

        if (minute == "*" &&
            hour == "*" &&
            dayOfMonth == "*" &&
            month == "*" &&
            dayOfWeek == "*")
        {
            return "Every minute";
        }

        if (TryParseNumber(minute, 0, 59, out var minuteValue) &&
            hour == "*" &&
            dayOfMonth == "*" &&
            month == "*" &&
            dayOfWeek == "*")
        {
            return $"Hourly at minute {minuteValue:00}";
        }

        if (!TryParseNumber(minute, 0, 59, out minuteValue) ||
            !TryParseNumber(hour, 0, 23, out var hourValue) ||
            month != "*")
        {
            return "Custom schedule";
        }

        var time = $"{hourValue:00}:{minuteValue:00}";
        if (dayOfMonth == "*" && dayOfWeek == "1-5")
        {
            return $"Weekdays at {time}";
        }

        if (dayOfMonth == "*" &&
            TryGetWeekday(dayOfWeek, out var weekday))
        {
            return $"Weekly on {weekday} at {time}";
        }

        if (dayOfWeek == "*" &&
            TryParseNumber(dayOfMonth, 1, 31, out var dayValue))
        {
            return $"Monthly on day {dayValue} at {time}";
        }

        return dayOfMonth == "*" && dayOfWeek == "*"
            ? $"Daily at {time}"
            : "Custom schedule";
    }

    private static bool TryParseNumber(
        string value,
        int minimum,
        int maximum,
        out int result) =>
        int.TryParse(
            value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out result) &&
        result >= minimum &&
        result <= maximum;

    private static bool TryGetWeekday(
        string value,
        out string weekday)
    {
        weekday = value.ToUpperInvariant() switch
        {
            "0" or "7" or "SUN" => "Sunday",
            "1" or "MON" => "Monday",
            "2" or "TUE" => "Tuesday",
            "3" or "WED" => "Wednesday",
            "4" or "THU" => "Thursday",
            "5" or "FRI" => "Friday",
            "6" or "SAT" => "Saturday",
            _ => string.Empty,
        };
        return weekday.Length > 0;
    }
}
