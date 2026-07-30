namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes the statically known kind of one local path.
/// </summary>
public enum LocalPathTargetKind
{
    /// <summary>
    /// The path identifies a file.
    /// </summary>
    File,

    /// <summary>
    /// The path identifies a directory.
    /// </summary>
    Directory,

    /// <summary>
    /// The path may identify either a file or a directory.
    /// </summary>
    FileOrDirectory,
}
