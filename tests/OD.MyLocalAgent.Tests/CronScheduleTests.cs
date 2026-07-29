using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies deterministic cron translation with an explicit clock instant and time zone.
/// </summary>
public sealed class CronScheduleTests
{
    /// <summary>
    /// Verifies a local daily schedule is translated to the correct absolute instant.
    /// </summary>
    [Fact]
    public void NextOccurrenceUsesSuppliedTimeZone()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Fixed-Plus-Two",
            TimeSpan.FromHours(2),
            "Fixed +02",
            "Fixed +02");
        var from = DateTimeOffset.Parse("2026-07-29T05:30:00Z");

        var next = CronSchedule.NextOccurrence("0 8 * * *", from, timeZone);

        next.Should().Be(DateTimeOffset.Parse("2026-07-29T08:00:00+02:00"));
    }

    /// <summary>
    /// Verifies the direct plain-language mappings for common cron patterns.
    /// </summary>
    [Theory]
    [InlineData("* * * * *", "Every minute")]
    [InlineData("15 * * * *", "Hourly at minute 15")]
    [InlineData("30 8 * * *", "Daily at 08:30")]
    [InlineData("0 9 * * 1-5", "Weekdays at 09:00")]
    [InlineData("0 10 * * 1", "Weekly on Monday at 10:00")]
    [InlineData("45 6 1 * *", "Monthly on day 1 at 06:45")]
    public void DescribeCommonPatternReturnsExpectedLabel(
        string cron,
        string expected)
    {
        CronSchedule.DescribeCommonPattern(cron).Should().Be(expected);
    }

    /// <summary>
    /// Verifies that cron preview generation returns an ordered fixed-size sequence.
    /// </summary>
    [Fact]
    public void NextOccurrencesReturnsRequestedSequence()
    {
        var from = new DateTimeOffset(2026, 7, 29, 8, 0, 0, TimeSpan.Zero);

        var occurrences = CronSchedule.NextOccurrences(
            "0 * * * *",
            from,
            TimeZoneInfo.Utc,
            3);

        occurrences.Should().Equal(
            new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 29, 11, 0, 0, TimeSpan.Zero));
    }
}
