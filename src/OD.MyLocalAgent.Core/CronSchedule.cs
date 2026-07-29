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
}
