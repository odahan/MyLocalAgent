using FluentAssertions;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies application-theme persistence names and fallback behavior.
/// </summary>
public sealed class AppThemeCatalogTests
{
    /// <summary>
    /// Verifies supported theme names are parsed without case sensitivity.
    /// </summary>
    [Theory]
    [InlineData("Dark", AppTheme.Dark)]
    [InlineData("dark", AppTheme.Dark)]
    [InlineData("Light", AppTheme.Light)]
    [InlineData("LIGHT", AppTheme.Light)]
    public void TryParseAcceptsSupportedNames(
        string value,
        AppTheme expectedTheme)
    {
        var parsed = AppThemeCatalog.TryParse(value, out var theme);

        parsed.Should().BeTrue();
        theme.Should().Be(expectedTheme);
    }

    /// <summary>
    /// Verifies missing or unsupported values retain the dark default.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("System")]
    public void ParseOrDefaultUsesDarkForUnsupportedValues(string? value)
    {
        var theme = AppThemeCatalog.ParseOrDefault(value);

        theme.Should().Be(AppTheme.Dark);
    }

    /// <summary>
    /// Verifies the Settings list exposes the expected stable order.
    /// </summary>
    [Fact]
    public void NamesExposeDarkThenLight()
    {
        AppThemeCatalog.Names.Should().Equal("Dark", "Light");
    }

    /// <summary>
    /// Verifies Trust filter is ordered between AI playground and Settings.
    /// </summary>
    [Fact]
    public void TrustFilterRoutePrecedesSettingsAfterPlayground()
    {
        var routes = Enum.GetValues<PageRoute>();

        routes.Should().ContainInOrder(
            PageRoute.AIPlayground,
            PageRoute.TrustFilter,
            PageRoute.Settings);
    }
}
