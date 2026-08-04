namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines deterministic local-path semantics for one Tool Map argument.
/// </summary>
/// <param name="ArgumentName">The argument containing the source path.</param>
/// <param name="TargetKind">The statically known target kind.</param>
/// <param name="RequiredAccess">The access needed at runtime.</param>
/// <param name="Derivation">How the analyzed path is derived.</param>
/// <param name="ValueArgumentName">The auxiliary argument used by a derived path.</param>
public sealed record LocalPathArgumentDefinition(
    string ArgumentName,
    LocalPathTargetKind TargetKind,
    FileSystemAccessMode RequiredAccess,
    LocalPathDerivationKind Derivation = LocalPathDerivationKind.Direct,
    string? ValueArgumentName = null)
{
    /// <summary>
    /// Gets the fixed fallback value used by an optional derived path.
    /// </summary>
    public string? DefaultValue { get; init; }
}
