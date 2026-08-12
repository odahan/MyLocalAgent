using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Represents a pending Direct Chat attachment in the composer.</summary>
public sealed class DirectChatAttachmentItem(DirectChatAttachment attachment)
{
    /// <summary>Gets the attachment sent with the prompt.</summary>
    public DirectChatAttachment Attachment { get; } = attachment ?? throw new ArgumentNullException(nameof(attachment));

    /// <summary>Gets the source file name.</summary>
    public string FileName => Attachment.FileName;

    /// <summary>Gets a compact content-kind label.</summary>
    public string KindText => Attachment.Kind == DirectChatAttachmentKind.Image ? "Image" : "Text";
}
