using CommunityToolkit.Mvvm.ComponentModel;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Represents one conversation in the Direct Chat navigation list.</summary>
public sealed partial class DirectChatConversationItem : ObservableObject
{
    /// <summary>Initializes an item from its persisted conversation.</summary>
    public DirectChatConversationItem(DirectChatConversation conversation) =>
        Update(conversation);

    /// <summary>Gets the stable conversation identifier.</summary>
    public string Id { get; private set; } = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _updatedUtc;

    /// <summary>Gets a compact local-time label.</summary>
    public string UpdatedText => UpdatedUtc.ToLocalTime().ToString("g");

    /// <summary>Updates this item from current persisted data.</summary>
    public void Update(DirectChatConversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        Id = conversation.Id;
        Title = conversation.Title;
        UpdatedUtc = conversation.UpdatedUtc;
        OnPropertyChanged(nameof(UpdatedText));
    }
}
