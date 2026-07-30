namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes one statically derived absolute-directory authorization proposal.
/// </summary>
/// <param name="DirectoryPath">The normalized directory to authorize.</param>
/// <param name="RequiredAccess">The minimum required access.</param>
/// <param name="SourceAliases">The workflow aliases that require the proposal.</param>
/// <param name="IsBroaderScope">Whether target ambiguity forced a containing-directory proposal.</param>
/// <param name="IsVolumeRoot">Whether the proposal covers an entire volume.</param>
/// <param name="IsUnc">Whether the proposal is a UNC path.</param>
public sealed record WorkflowPathRequirement(
    string DirectoryPath,
    FileSystemAccessMode RequiredAccess,
    IReadOnlyList<string> SourceAliases,
    bool IsBroaderScope,
    bool IsVolumeRoot,
    bool IsUnc);
