namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Applies the selected application theme to the live WPF resource graph.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Gets the currently applied theme.
    /// </summary>
    AppTheme CurrentTheme { get; }

    /// <summary>
    /// Applies the theme from the layered application configuration.
    /// </summary>
    void ApplyConfiguredTheme();

    /// <summary>
    /// Applies a theme immediately without restarting the application.
    /// </summary>
    void ApplyTheme(AppTheme theme);
}
