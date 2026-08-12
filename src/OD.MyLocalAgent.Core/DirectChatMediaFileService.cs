namespace OD.MyLocalAgent.Core;

/// <summary>Saves persisted Direct Chat media after an explicit user action.</summary>
public interface IDirectChatMediaFileService
{
    /// <summary>Writes one complete media item to an explicit local path.</summary>
    Task SaveAsync(
        DirectChatContent content,
        string destinationPath,
        CancellationToken cancellationToken);
}

/// <summary>Provides bounded, explicit generated-media file saving.</summary>
public sealed class DirectChatMediaFileService : IDirectChatMediaFileService
{
    /// <inheritdoc />
    public async Task SaveAsync(
        DirectChatContent content,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (content.Kind is not (DirectChatContentKind.Image or DirectChatContentKind.Audio) ||
            content.Data is null ||
            content.Data.LongLength != content.SizeBytes)
        {
            throw new InvalidOperationException("The selected Direct Chat content is not complete saveable media.");
        }

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The media destination has no parent directory.");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(fullPath, content.Data, cancellationToken)
            .ConfigureAwait(false);
    }
}
