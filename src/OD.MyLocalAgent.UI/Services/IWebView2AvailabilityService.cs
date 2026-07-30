namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Detects whether the WebView2 Runtime is available without downloading or installing it.
/// </summary>
public interface IWebView2AvailabilityService
{
    /// <summary>
    /// Determines whether a WebView2 Runtime can be detected.
    /// </summary>
    /// <returns><see langword="true"/> when a runtime is available.</returns>
    bool IsAvailable();
}
