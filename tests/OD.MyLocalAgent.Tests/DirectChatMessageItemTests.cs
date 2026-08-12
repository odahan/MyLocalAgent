using FluentAssertions;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies completed Direct Chat messages remain safely text-rendered.</summary>
public sealed class DirectChatMessageItemTests
{
    /// <summary>Verifies completion keeps the response content without a scrolling WebView2 host.</summary>
    [Fact]
    public void CompletedAssistantMessageDoesNotUseMarkdownWebView()
    {
        var message = new DirectChatMessage
        {
            Id = "0123456789abcdef0123456789abcdef",
            ConversationId = "0123456789abcdef0123456789abcdef",
            Sequence = 1,
            Role = DirectChatMessageRole.Assistant,
            Content = "**Rendered answer**",
            State = DirectChatMessageState.Streaming,
            CreatedUtc = DateTimeOffset.UtcNow,
        };
        var item = new DirectChatMessageItem(
            message,
            new MarkdownRenderingService(),
            isMarkdownAvailable: true);

        item.Update("**Rendered answer**", DirectChatMessageState.Completed);

        item.ShowsRenderedMarkdown.Should().BeFalse();
        item.RenderedHtml.Should().BeEmpty();
        item.RenderHeight.Should().BeGreaterThan(0);
    }
}
