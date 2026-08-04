namespace OD.MyLocalAgent.Core;

/// <summary>
/// Moves local files and directories to the operating-system recycle bin.
/// </summary>
public interface IRecycleBinService
{
    /// <summary>
    /// Moves one absolute local path to the recycle bin without displaying UI.
    /// </summary>
    /// <param name="absolutePath">The existing absolute file or directory path.</param>
    /// <param name="cancellationToken">The cancellation token checked before dispatch.</param>
    Task MoveToBinAsync(string absolutePath, CancellationToken cancellationToken);
}
