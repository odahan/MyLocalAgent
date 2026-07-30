namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes how a local path is obtained from workflow arguments.
/// </summary>
public enum LocalPathDerivationKind
{
    /// <summary>
    /// The argument value is the path.
    /// </summary>
    Direct,

    /// <summary>
    /// A destination is formed in the source directory from a leaf-name argument.
    /// </summary>
    SameDirectoryLeafName,

    /// <summary>
    /// A destination is formed by replacing the source extension.
    /// </summary>
    SameDirectoryExtension,
}
