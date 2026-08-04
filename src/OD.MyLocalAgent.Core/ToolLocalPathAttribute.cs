namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines deterministic local-path authorization semantics for a tool method argument.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ToolLocalPathAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolLocalPathAttribute"/> class.
    /// </summary>
    /// <param name="argumentName">The argument name containing the target path.</param>
    /// <param name="targetKind">The statically known target path kind.</param>
    /// <param name="requiredAccess">The file system access mode required at runtime.</param>
    public ToolLocalPathAttribute(
        string argumentName,
        LocalPathTargetKind targetKind,
        FileSystemAccessMode requiredAccess)
    {
        ArgumentName = argumentName;
        TargetKind = targetKind;
        RequiredAccess = requiredAccess;
    }

    /// <summary>
    /// Gets the argument name containing the source path.
    /// </summary>
    public string ArgumentName { get; }

    /// <summary>
    /// Gets the target path kind.
    /// </summary>
    public LocalPathTargetKind TargetKind { get; }

    /// <summary>
    /// Gets the required file system access mode.
    /// </summary>
    public FileSystemAccessMode RequiredAccess { get; }

    /// <summary>
    /// Gets or sets how the analyzed path is derived.
    /// Defaults to <see cref="LocalPathDerivationKind.Direct"/>.
    /// </summary>
    public LocalPathDerivationKind Derivation { get; set; } = LocalPathDerivationKind.Direct;

    /// <summary>
    /// Gets or sets the auxiliary argument used by a derived path.
    /// </summary>
    public string? ValueArgumentName { get; set; }

    /// <summary>
    /// Gets or sets the fixed fallback value used by an optional derived path.
    /// </summary>
    public string? DefaultValue { get; set; }
}
