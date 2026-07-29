namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Identifies a supported application color theme.
/// </summary>
public enum AppTheme
{
    /// <summary>
    /// Uses the dark application palette.
    /// </summary>
    Dark,

    /// <summary>
    /// Uses the light application palette.
    /// </summary>
    Light,
}

/// <summary>
/// Provides stable persisted names and parsing for application themes.
/// </summary>
public static class AppThemeCatalog
{
    /// <summary>
    /// Gets the persisted dark-theme name.
    /// </summary>
    public const string DarkName = nameof(AppTheme.Dark);

    /// <summary>
    /// Gets the persisted light-theme name.
    /// </summary>
    public const string LightName = nameof(AppTheme.Light);

    /// <summary>
    /// Gets the themes exposed by the Settings page.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } =
        [DarkName, LightName];

    /// <summary>
    /// Parses a persisted theme name.
    /// </summary>
    public static bool TryParse(string? value, out AppTheme theme)
    {
        return Enum.TryParse(value, ignoreCase: true, out theme) &&
            Enum.IsDefined(theme);
    }

    /// <summary>
    /// Parses a persisted theme name, falling back to dark.
    /// </summary>
    public static AppTheme ParseOrDefault(string? value)
    {
        return TryParse(value, out var theme)
            ? theme
            : AppTheme.Dark;
    }

    /// <summary>
    /// Gets the stable persisted name for a theme.
    /// </summary>
    public static string GetName(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Dark => DarkName,
            AppTheme.Light => LightName,
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
        };
    }
}
