using System.Windows;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.Configuration;

namespace OD.MyLocalAgent.UI.Services;

internal sealed class ThemeService : IThemeService
{
    private const string ThemeConfigurationKey = "Appearance:Theme";
    private const string PaletteSuffix = "Palette.xaml";
    private readonly IConfiguration _configuration;
    private readonly PaletteHelper _paletteHelper = new();

    public ThemeService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public void ApplyConfiguredTheme()
    {
        ApplyTheme(
            AppThemeCatalog.ParseOrDefault(
                _configuration[ThemeConfigurationKey]));
    }

    public void ApplyTheme(AppTheme theme)
    {
        var application = Application.Current ??
            throw new InvalidOperationException(
                "The application theme cannot be changed before WPF startup.");
        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.Invoke(() => ApplyTheme(theme));
            return;
        }

        ReplaceApplicationPalette(application, theme);

        var materialTheme = _paletteHelper.GetTheme();
        materialTheme.SetBaseTheme(
            theme == AppTheme.Dark
                ? BaseTheme.Dark
                : BaseTheme.Light);
        _paletteHelper.SetTheme(materialTheme);
        CurrentTheme = theme;
    }

    private static void ReplaceApplicationPalette(
        Application application,
        AppTheme theme)
    {
        var dictionaries = application.Resources.MergedDictionaries;
        var paletteIndex = dictionaries
            .Select(
                static (dictionary, index) =>
                    new
                    {
                        Dictionary = dictionary,
                        Index = index,
                    })
            .FirstOrDefault(
                static item =>
                    item.Dictionary.Source?.OriginalString.EndsWith(
                        PaletteSuffix,
                        StringComparison.OrdinalIgnoreCase) == true)
            ?.Index ?? -1;
        var paletteName = $"{AppThemeCatalog.GetName(theme)}Palette.xaml";
        var palette = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/OD.MyLocalAgent.UI;component/Themes/{paletteName}",
                UriKind.Absolute),
        };

        if (paletteIndex >= 0)
        {
            dictionaries[paletteIndex] = palette;
            return;
        }

        dictionaries.Insert(Math.Max(0, dictionaries.Count - 1), palette);
    }
}
