using System.Text.Json.Serialization;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines the operations allowed beneath a task-authorized directory.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FileSystemAccessMode>))]
public enum FileSystemAccessMode
{
    /// <summary>
    /// Allows directory listing and file reads.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Allows reads and mutations, subject to independent task capabilities.
    /// </summary>
    ReadWrite,
}
