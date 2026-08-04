namespace OD.MyLocalAgent.Core;

/// <summary>
/// Identifies an implementation class as a tool group owner and defines its group metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ToolGroupAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolGroupAttribute"/> class.
    /// </summary>
    /// <param name="name">The stable tool-group name stored in task JSON.</param>
    /// <param name="displayName">The English label shown by user interfaces.</param>
    /// <param name="description">The English explanation shown by authoring surfaces.</param>
    /// <param name="displayOrder">The unique display order position.</param>
    public ToolGroupAttribute(
        string name,
        string displayName,
        string description,
        int displayOrder)
    {
        Name = name;
        DisplayName = displayName;
        Description = description;
        DisplayOrder = displayOrder;
    }

    /// <summary>
    /// Gets the stable group name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the UI display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the group description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the unique positive integer display order.
    /// </summary>
    public int DisplayOrder { get; }

    /// <summary>
    /// Gets or sets a value indicating whether tasks may persist the group in their whitelist.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool IsTaskWhitelistGroup { get; set; } = true;

    /// <summary>
    /// Gets or sets the target activation lifetime.
    /// Defaults to <see cref="ToolActivationKind.Singleton"/>.
    /// </summary>
    public ToolActivationKind Activation { get; set; } = ToolActivationKind.Singleton;

    /// <summary>
    /// Gets or sets the optional public service contract implemented by the class.
    /// </summary>
    public Type? ServiceContract { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to register a taskless projection for the service contract.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool RegisterTasklessProjection { get; set; } = true;
}
