using FluentAssertions;
using Google.Apis.Gmail.v1.Data;
using OD.MyLocalAgent.Connectors;
using System.Text.Json;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies representative Gmail API payload mapping.
/// </summary>
public sealed class GmailMessageMapperTests
{
    /// <summary>
    /// Verifies headers, metadata, unread state, and nested plain-text extraction.
    /// </summary>
    [Fact]
    public async Task MapConvertsRepresentativeGooglePayload()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "gmail-message.json");
        await using var stream = File.OpenRead(path);
        var message = await JsonSerializer.DeserializeAsync<Message>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var result = GmailMessageMapper.Map(message!);

        result.Id.Should().Be("message-123");
        result.From.Should().Be("Ada Lovelace <ada@example.com>");
        result.To.Should().Be("Agent <agent@example.com>");
        result.Subject.Should().Be("Representative message");
        result.Snippet.Should().Be("Hello from fixture. Second line.");
        result.BodyPlain.Should().Be("Hello from fixture.\nSecond line.");
        result.ReceivedAt.Should().Be(DateTimeOffset.Parse("2026-07-29T08:15:00Z"));
        result.IsUnread.Should().BeTrue();
    }
}
