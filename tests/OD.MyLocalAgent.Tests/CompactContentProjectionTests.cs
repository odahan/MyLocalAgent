using FluentAssertions;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies deterministic Gmail and HTTP minimization boundaries.
/// </summary>
public sealed class CompactContentProjectionTests
{
    /// <summary>
    /// Verifies canonical order, invariant time, normalization, Unicode, and bounds.
    /// </summary>
    [Fact]
    public void GmailProjectionIsCanonicalNormalizedAndBounded()
    {
        var message = new EmailMessageDto(
            "id",
            " Sender\u0001 ",
            "Reader",
            "Été\r\nupdate",
            "First  \r\n\r\n\r\n\r\nLast",
            new string('b', 3_000),
            DateTimeOffset.Parse("2026-08-03T10:15:30+02:00"),
            true);

        var projection = GmailContentProjector.Project(message, 2_048);

        projection.Content.Should().StartWith("From:  Sender\nTo: Reader\nSubject: Été\nupdate\n");
        projection.Content.Should().Contain("Received: 2026-08-03T10:15:30.0000000+02:00\n");
        projection.Content.IndexOf("Snippet:", StringComparison.Ordinal)
            .Should().BeLessThan(projection.Content.IndexOf("Body:", StringComparison.Ordinal));
        projection.Content.Should().NotContain("\r");
        projection.Content.Should().NotContain("\n\n\n\n");
        projection.ProjectedCharacterCount.Should().Be(projection.Content.Length);
        projection.ProjectedCharacterCount.Should().BeLessThanOrEqualTo(2_048);
        projection.OmittedCharacterCount.Should().BeGreaterThan(0);
        projection.ProjectionWasTruncated.Should().BeTrue();
    }

    /// <summary>
    /// Verifies recognized English, French, and original-message boundaries remove only suffixes.
    /// </summary>
    [Theory]
    [InlineData("Current request\nOn Monday, Alex wrote:\nold text")]
    [InlineData("Demande actuelle\nLe lundi, Alex a écrit :\nancien texte")]
    [InlineData("Current request\n-----Original Message-----\nold text")]
    [InlineData("Current request\n> quoted text")]
    public void GmailProjectionRemovesRecognizedQuotedSuffix(string body)
    {
        var projection = GmailContentProjector.Project(CreateMessage(body), 2_048);

        projection.QuotedHistoryWasRemoved.Should().BeTrue();
        projection.Content.Should().Contain(body.Split('\n')[0]);
        projection.Content.Should().Contain("quoted-history characters omitted");
        projection.Content.Should().NotContain("old text");
        projection.Content.Should().NotContain("ancien texte");
    }

    /// <summary>
    /// Verifies separator-like prose and the current signature remain source content.
    /// </summary>
    [Fact]
    public void GmailProjectionPreservesNonBoundaryAndSignature()
    {
        const string body =
            "We discussed On Monday, Alex wrote: as an example.\n\nRegards,\nMarie\n+33 1 23 45 67 89";

        var projection = GmailContentProjector.Project(CreateMessage(body), 2_048);

        projection.QuotedHistoryWasRemoved.Should().BeFalse();
        projection.Content.Should().Contain("Regards,");
        projection.Content.Should().Contain("+33 1 23 45 67 89");
    }

    /// <summary>
    /// Verifies JSON minification, fallback normalization, and explicit capping.
    /// </summary>
    [Fact]
    public void HttpProjectionMinifiesJsonAndCapsText()
    {
        var json = HttpContentProjector.Project("{ \"a\" : 1, \"b\" : [2] }", "application/json", 100);
        var fallback = HttpContentProjector.Project("not\r\njson\u0001", "application/json", 100);
        var capped = HttpContentProjector.Project(new string('x', 200), "text/plain", 80);

        json.Content.Should().Be("{\"a\":1,\"b\":[2]}");
        fallback.Content.Should().Be("not\njson");
        capped.Content.Should().HaveLength(80).And.Contain("characters omitted");
        capped.OmittedCharacterCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Verifies readable HTML preference, node removal, entity decoding, and block boundaries.
    /// </summary>
    [Fact]
    public void HttpProjectionExtractsReadableMainContent()
    {
        const string html =
            "<html><body><header>Header</header><article>Fallback</article>" +
            "<main><h1>Title &amp; more</h1><p>First <b>paragraph</b>.</p>" +
            "<nav>Hidden nav</nav><script>hidden()</script><li>Item</li></main></body></html>";

        var projection = HttpContentProjector.Project(html, "text/html", 2_048);

        projection.Content.Should().Be("Title & more\nFirst paragraph.\nItem");
        projection.Content.Should().NotContain("Fallback");
        projection.Content.Should().NotContain("hidden");
    }

    /// <summary>
    /// Verifies binary media is represented by application-authored content only.
    /// </summary>
    [Fact]
    public void HttpProjectionUsesUnsupportedMediaMarker()
    {
        var projection = HttpContentProjector.Project("binary canary", "image/png", 2_048);

        projection.Content.Should().Contain("unsupported or binary");
        projection.Content.Should().NotContain("binary canary");
    }

    private static EmailMessageDto CreateMessage(string body) =>
        new(
            "id",
            "from@example.com",
            "to@example.com",
            "Subject",
            "Snippet",
            body,
            DateTimeOffset.Parse("2026-08-03T08:00:00Z"),
            false);
}
