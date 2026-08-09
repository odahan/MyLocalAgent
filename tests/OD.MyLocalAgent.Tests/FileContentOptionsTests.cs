using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies Stage 22 file-content option boundaries.</summary>
public sealed class FileContentOptionsTests
{
    /// <summary>Verifies the archive-write Safety key remains fail-closed by default.</summary>
    [Fact]
    public void ArchiveWriteSafetyIsDisabledByDefaultAndRecognizedWhenEnabled()
    {
        new SafetyOptions().IsEnabled("Safety:AllowArchiveWrite").Should().BeFalse();
        new SafetyOptions { AllowArchiveWrite = true }.IsEnabled("Safety:AllowArchiveWrite").Should().BeTrue();
    }

    /// <summary>Verifies shipped defaults form a valid bounded configuration.</summary>
    [Fact]
    public void DefaultsAreValid() => FileContentOptions.IsValid(new FileContentOptions()).Should().BeTrue();

    /// <summary>Verifies invalid archive and content bounds fail closed.</summary>
    [Theory]
    [InlineData(0, 1000, 256, 25000)]
    [InlineData(104857600, 0, 256, 25000)]
    [InlineData(104857600, 1000, 0, 25000)]
    [InlineData(104857600, 1000, 256, 0)]
    public void InvalidValuesAreRejected(long inputBytes, int rows, int columns, int cells)
    {
        var options = new FileContentOptions
        {
            MaxInputFileBytes = inputBytes,
            MaxSpreadsheetRows = rows,
            MaxSpreadsheetColumns = columns,
            MaxSpreadsheetCells = cells,
        };
        FileContentOptions.IsValid(options).Should().BeFalse();
    }

    /// <summary>Verifies spreadsheet-cell limits cannot exceed the configured grid.</summary>
    [Fact]
    public void CellLimitCannotExceedGridCapacity()
    {
        var options = new FileContentOptions { MaxSpreadsheetRows = 2, MaxSpreadsheetColumns = 2, MaxSpreadsheetCells = 5 };
        FileContentOptions.IsValid(options).Should().BeFalse();
    }
}
