using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies program-owned SQLite option boundaries.
/// </summary>
public sealed class SQLiteOptionsTests
{
    /// <summary>
    /// Verifies every inclusive SQLite option boundary is accepted.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 4_096, 0, 256, 1)]
    [InlineData(10_000, 10_000, 1_000_000, 1_000_000, 1_000_000, 300)]
    public void InclusiveBoundariesAreValid(int defaultRows, int maxRows, int resultCharacters, int blobBytes, int sqlCharacters, int timeoutSeconds)
    {
        SQLiteOptions.IsValid(new SQLiteOptions
        {
            DefaultMaxRows = defaultRows,
            MaxRows = maxRows,
            MaxResultCharacters = resultCharacters,
            MaxBlobBytes = blobBytes,
            MaxSqlCharacters = sqlCharacters,
            CommandTimeoutSeconds = timeoutSeconds,
        }).Should().BeTrue();
    }

    /// <summary>
    /// Verifies invalid values and inconsistent row bounds are rejected.
    /// </summary>
    [Theory]
    [InlineData(0, 1, 4_096, 0, 256, 1)]
    [InlineData(2, 1, 4_096, 0, 256, 1)]
    [InlineData(1, 10_001, 4_096, 0, 256, 1)]
    [InlineData(1, 1, 4_095, 0, 256, 1)]
    [InlineData(1, 1, 4_096, -1, 256, 1)]
    [InlineData(1, 1, 4_096, 0, 255, 1)]
    [InlineData(1, 1, 4_096, 0, 256, 0)]
    public void InvalidValuesAreRejected(int defaultRows, int maxRows, int resultCharacters, int blobBytes, int sqlCharacters, int timeoutSeconds)
    {
        SQLiteOptions.IsValid(new SQLiteOptions
        {
            DefaultMaxRows = defaultRows,
            MaxRows = maxRows,
            MaxResultCharacters = resultCharacters,
            MaxBlobBytes = blobBytes,
            MaxSqlCharacters = sqlCharacters,
            CommandTimeoutSeconds = timeoutSeconds,
        }).Should().BeFalse();
    }
}
