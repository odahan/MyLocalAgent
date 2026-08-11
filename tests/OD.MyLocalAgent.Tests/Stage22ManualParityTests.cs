using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the standalone Stage 22 manual sections remain structurally aligned.</summary>
public sealed partial class Stage22ManualParityTests
{
    private static readonly string DocsDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Docs"));

    /// <summary>Verifies both manuals have valid anchors, standalone resources, and equal Stage 22 coverage.</summary>
    [Fact]
    public void ManualsHaveEquivalentStage22Coverage()
    {
        var french = File.ReadAllText(Path.Combine(DocsDirectory, "User-Manual.html"));
        var english = File.ReadAllText(Path.Combine(DocsDirectory, "User-Manual.en.html"));
        ValidateStructure(french);
        ValidateStructure(english);
        french.Should().Contain("User-Manual.en.html");
        english.Should().Contain("User-Manual.html");

        var frenchStage = StageSection(french);
        var englishStage = StageSection(english);
        CodeBlockRegex().Matches(frenchStage).Should().HaveCount(7);
        CodeBlockRegex().Matches(englishStage).Should().HaveCount(7);
        foreach (var alias in new[]
        {
            "archive.list", "archive.extract", "archive.extractAll", "archive.createZip", "archive.addToZip",
            "document.readDocx", "spreadsheet.listSheets", "spreadsheet.readRange", "pdf.getInfo", "pdf.readText"
        })
        {
            frenchStage.Should().Contain(alias);
            englishStage.Should().Contain(alias);
        }
        foreach (var format in new[] { "ZIP", "7z", "RAR", "TAR.LZ", "TAR.ZST", "GZIP", "BZIP2", "LZIP", "XZ", "Zstandard" })
        {
            frenchStage.Should().Contain(format);
            englishStage.Should().Contain(format);
        }
        foreach (var term in new[] { "ArchiveWrite", "100 MiB", "10,000", "64,000", "25,000", "100 PDF" })
        {
            englishStage.Should().Contain(term);
        }
        foreach (var term in new[] { "ArchiveWrite", "100 Mio", "10 000", "64 000", "25 000", "100 pages PDF" })
        {
            frenchStage.Should().Contain(term);
        }
    }

    private static void ValidateStructure(string html)
    {
        OpenSectionRegex().Matches(html).Count.Should().Be(CloseSectionRegex().Matches(html).Count);
        var ids = IdRegex().Matches(html).Select(match => match.Groups[1].Value).ToArray();
        ids.Should().OnlyHaveUniqueItems();
        var idSet = ids.ToHashSet(StringComparer.Ordinal);
        foreach (Match match in AnchorRegex().Matches(html)) idSet.Should().Contain(match.Groups[1].Value);
        ExternalResourceRegex().IsMatch(html).Should().BeFalse();
    }

    private static string StageSection(string html)
    {
        var start = html.IndexOf("<section id=\"stage22-local-files\">", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return html[start..(end + "</section>".Length)];
    }

    [GeneratedRegex("<section\\b", RegexOptions.IgnoreCase)] private static partial Regex OpenSectionRegex();
    [GeneratedRegex("</section>", RegexOptions.IgnoreCase)] private static partial Regex CloseSectionRegex();
    [GeneratedRegex("\\bid=\"([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex IdRegex();
    [GeneratedRegex("href=\"#([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex AnchorRegex();
    [GeneratedRegex("<(?:script|img|link)[^>]+(?:src|href)=\"https?://", RegexOptions.IgnoreCase)] private static partial Regex ExternalResourceRegex();
    [GeneratedRegex("<div class=\"code-block\">", RegexOptions.IgnoreCase)] private static partial Regex CodeBlockRegex();
}
