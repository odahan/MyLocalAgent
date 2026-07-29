namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Represents a normalized Dropbox file-system item.
/// </summary>
/// <param name="Name">The item name.</param>
/// <param name="Path">The display path.</param>
/// <param name="IsFolder">Whether the item is a folder.</param>
/// <param name="Size">The file size in bytes, or <see langword="null"/> for a folder.</param>
/// <param name="ModifiedAt">The client modification time for a file, when available.</param>
public sealed record DropboxItemDto(
    string Name,
    string Path,
    bool IsFolder,
    ulong? Size,
    DateTimeOffset? ModifiedAt);
