namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Allows a page to confirm or cancel navigation while asynchronous work is required.
/// </summary>
internal interface IAsyncNavigationGuard
{
    /// <summary>
    /// Determines whether the current page can be left.
    /// </summary>
    /// <returns><see langword="true"/> when navigation may continue.</returns>
    Task<bool> CanNavigateAwayAsync();
}
