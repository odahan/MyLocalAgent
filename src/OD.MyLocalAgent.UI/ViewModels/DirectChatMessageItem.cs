using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Represents one Direct Chat message displayed in the conversation.</summary>
public sealed partial class DirectChatMessageItem : ObservableObject
{
    /// <summary>Initializes an item from a persisted message.</summary>
    public DirectChatMessageItem(DirectChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Id = message.Id;
        ConversationId = message.ConversationId;
        Sequence = message.Sequence;
        Role = message.Role;
        CreatedUtc = message.CreatedUtc;
        _content = message.Content;
        _state = message.State;
        SetContents(message.Contents.Count == 0 && !string.IsNullOrEmpty(message.Content)
            ? [CreateTextContent(message.Content)]
            : message.Contents);
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

    /// <summary>Gets ordered text and media parts.</summary>
    public ObservableCollection<DirectChatContentItem> Contents { get; } = [];

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private DirectChatMessageState _state;

    [ObservableProperty]
    private bool _prefersRenderedMarkdown = true;

    /// <summary>Gets a value indicating whether this is a user message.</summary>
    public bool IsUser => Role == DirectChatMessageRole.User;

    /// <summary>
    /// Gets a value indicating whether completed assistant Markdown is rendered.
    /// </summary>
    public bool ShowsRenderedMarkdown =>
        !IsUser &&
        PrefersRenderedMarkdown &&
        State == DirectChatMessageState.Completed &&
        !string.IsNullOrWhiteSpace(Content);

    /// <summary>Gets a human-facing state suffix.</summary>
    public string StateText => State switch
    {
        DirectChatMessageState.Pending => "Waiting…",
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
        SetContents(string.IsNullOrEmpty(content) ? [] : [CreateTextContent(content)]);
        RefreshProjection();
    }

    /// <summary>Updates completed text and media parts.</summary>
    public void UpdateContents(
        string content,
        IReadOnlyList<DirectChatContent> contents,
        DirectChatMessageState state)
    {
        Content = content;
        State = state;
        SetContents(contents);
        RefreshProjection();
    }

    partial void OnContentChanged(string value) => RefreshProjection();

    partial void OnStateChanged(DirectChatMessageState value) => RefreshProjection();

    partial void OnPrefersRenderedMarkdownChanged(bool value) =>
        OnPropertyChanged(nameof(ShowsRenderedMarkdown));

    /// <summary>Switches this response between rendered Markdown and its source.</summary>
    [RelayCommand]
    private void ToggleMarkdownRendering()
    {
        if (!IsUser && State == DirectChatMessageState.Completed)
        {
            PrefersRenderedMarkdown = !PrefersRenderedMarkdown;
        }
    }

    private void RefreshProjection()
    {
        OnPropertyChanged(nameof(ShowsRenderedMarkdown));
        OnPropertyChanged(nameof(StateText));
    }

    private void SetContents(IEnumerable<DirectChatContent> contents)
    {
        Contents.Clear();
        foreach (var content in contents.OrderBy(static item => item.Position))
        {
            Contents.Add(new DirectChatContentItem(content));
        }
    }

    private static DirectChatContent CreateTextContent(string text) => new()
    {
        Position = 0,
        Kind = DirectChatContentKind.Text,
        MediaType = "text/markdown",
        Text = text,
        SizeBytes = System.Text.Encoding.UTF8.GetByteCount(text),
    };
}
