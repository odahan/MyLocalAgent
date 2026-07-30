using Microsoft.Web.WebView2.Core;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Detects the installed WebView2 Runtime through the supported environment API.
/// </summary>
public sealed class WebView2AvailabilityService : IWebView2AvailabilityService
{
    /// <inheritdoc />
    public bool IsAvailable()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(
                CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception)
        {
            return false;
        }
    }
}
