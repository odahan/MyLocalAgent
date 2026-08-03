namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Describes how the user wants to handle unsaved settings changes.
/// </summary>
public enum UnsavedChangesChoice
{
    /// <summary>
    /// Keeps the user on the current page without changing the settings state.
    /// </summary>
    Stay,

    /// <summary>
    /// Saves the changes before continuing.
    /// </summary>
    Save,

    /// <summary>
    /// Discards the changes before continuing.
    /// </summary>
    Discard,
}
