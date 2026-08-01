using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Exposes application identity details for the About page.
/// </summary>
public sealed class AboutViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViewModel"/> class.
    /// </summary>
    public AboutViewModel()
    {
        var assembly = Assembly.GetExecutingAssembly();
        Version = FormatVersion(assembly);
    }

    /// <summary>
    /// Gets the user-facing application name.
    /// </summary>
    public string AppName => "OD.MyLocalAgent";

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the copyright notice.
    /// </summary>
    public string Copyright => "© 2026 Olivier Dahan";

    private static string FormatVersion(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the source-revision suffix appended by the SDK.
            var plusIndex = informational.IndexOf('+', StringComparison.Ordinal);
            var version = plusIndex >= 0
                ? informational[..plusIndex]
                : informational;
            return $"Version {version}";
        }

        return $"Version {assembly.GetName().Version?.ToString(3) ?? "?"}";
    }
}
