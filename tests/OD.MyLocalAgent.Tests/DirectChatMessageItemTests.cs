using FluentAssertions;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies Direct Chat Markdown display state.</summary>
public sealed class DirectChatMessageItemTests
{
    /// <summary>Verifies completed responses render by default and can return to source mode.</summary>
    [Fact]
    public void CompletedAssistantMessageCanToggleNativeMarkdownRendering()
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
        var item = new DirectChatMessageItem(message);

        item.Update("**Rendered answer**", DirectChatMessageState.Completed);

        item.ShowsRenderedMarkdown.Should().BeTrue();
        item.Contents.Should().ContainSingle();
        item.Contents[0].Text.Should().Be("**Rendered answer**");

        item.ToggleMarkdownRenderingCommand.Execute(null);

        item.ShowsRenderedMarkdown.Should().BeFalse();
    }
}
