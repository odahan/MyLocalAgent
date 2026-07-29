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
}
