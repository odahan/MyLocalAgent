namespace OD.MyLocalAgent.Core;

/// <summary>Represents a file that cannot be attached to a Direct Chat message.</summary>
public sealed class DirectChatAttachmentException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="DirectChatAttachmentException"/> class.</summary>
    public DirectChatAttachmentException(string message)
        : base(message)
    {
    }
}
