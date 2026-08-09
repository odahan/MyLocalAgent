namespace OD.MyLocalAgent.Core;
/// <summary>Represents an expected, content-free local file tool failure.</summary>
public sealed class FileContentToolException : InvalidOperationException
{
    /// <summary>Initializes an exception with a stable category.</summary>
    public FileContentToolException(string category) : base($"File content operation failed: {category}.") => Category = category;
    /// <summary>Gets the stable failure category.</summary>
    public string Category { get; }
}
