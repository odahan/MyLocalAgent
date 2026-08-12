using CommunityToolkit.Mvvm.ComponentModel;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Represents one Direct Chat message displayed in the conversation.</summary>
public sealed partial class DirectChatMessageItem : ObservableObject
{
    private readonly IMarkdownRenderingService _markdown;
    private readonly bool _isMarkdownAvailable;

    /// <summary>Initializes an item from a persisted message.</summary>
    public DirectChatMessageItem(
        DirectChatMessage message,
        IMarkdownRenderingService markdown,
        bool isMarkdownAvailable)
    {
        ArgumentNullException.ThrowIfNull(message);
        _markdown = markdown;
        _isMarkdownAvailable = isMarkdownAvailable;
        Id = message.Id;
        ConversationId = message.ConversationId;
        Sequence = message.Sequence;
        Role = message.Role;
        CreatedUtc = message.CreatedUtc;
        _content = message.Content;
        _state = message.State;
        RefreshProjection();
    }

    /// <summary>Gets the stable message identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the owning conversation identifier.</summary>
    public string ConversationId { get; }

    /// <summary>Gets the conversation-local sequence.</summary>
    public long Sequence { get; }

    /// <summary>Gets the message author.</summary>
    public DirectChatMessageRole Role { get; }

    /// <summary>Gets the UTC creation time.</summary>
    public DateTimeOffset CreatedUtc { get; }

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private DirectChatMessageState _state;

    [ObservableProperty]
    private string _renderedHtml = string.Empty;

    /// <summary>Gets a value indicating whether this is a user message.</summary>
    public bool IsUser => Role == DirectChatMessageRole.User;

    /// <summary>
    /// Gets a value indicating whether completed assistant Markdown can be rendered.
    /// WebView2 cannot be hosted safely inside the WPF scrolling message list.
    /// </summary>
    public bool ShowsRenderedMarkdown => false;

    /// <summary>Gets a height estimate for the isolated Markdown browser.</summary>
    public double RenderHeight => Math.Clamp(
        70 + Content.Count(static character => character == '\n') * 20,
        90,
        420);

    /// <summary>Gets a human-facing state suffix.</summary>
    public string StateText => State switch
    {
        DirectChatMessageState.Streaming => "Generating…",
        DirectChatMessageState.Canceled => "Canceled",
        DirectChatMessageState.Failed => "Failed",
        _ => string.Empty,
    };

    /// <summary>Updates the streamed content and state.</summary>
    public void Update(string content, DirectChatMessageState state)
    {
        Content = content;
        State = state;
        RefreshProjection();
    }

    partial void OnContentChanged(string value) => RefreshProjection();

    partial void OnStateChanged(DirectChatMessageState value) => RefreshProjection();

    private void RefreshProjection()
    {
        RenderedHtml = ShowsRenderedMarkdown
            ? _markdown.ToRestrictedHtml(Content)
            : string.Empty;
        OnPropertyChanged(nameof(ShowsRenderedMarkdown));
        OnPropertyChanged(nameof(RenderHeight));
        OnPropertyChanged(nameof(StateText));
    }
}
