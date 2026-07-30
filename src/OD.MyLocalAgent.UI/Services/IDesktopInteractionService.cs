namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Abstracts user-confirmation, file-selection, clipboard, and folder operations.
/// </summary>
public interface IDesktopInteractionService
{
    /// <summary>
    /// Requests confirmation for a potentially destructive operation.
    /// </summary>
    bool Confirm(string title, string message);

    /// <summary>
    /// Lets the user select a destination file.
    /// </summary>
    string? SelectSavePath(
        string title,
        string filter,
        string defaultExtension,
        string suggestedFileName);

    /// <summary>
    /// Lets the user select an existing directory.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <returns>The selected absolute path, or <see langword="null"/>.</returns>
    string? SelectFolder(string title);

    /// <summary>
    /// Copies text to the Windows clipboard.
    /// </summary>
    void CopyText(string text);

    /// <summary>
    /// Opens a directory in the Windows shell.
    /// </summary>
    void OpenFolder(string path);

    /// <summary>
    /// Opens an absolute URI through the Windows shell after an explicit user action.
    /// </summary>
    /// <param name="uri">The absolute URI.</param>
    void OpenUri(string uri);
}
