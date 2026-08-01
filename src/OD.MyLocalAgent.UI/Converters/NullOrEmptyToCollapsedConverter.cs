using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OD.MyLocalAgent.UI.Converters;

/// <summary>
/// Converts a null or empty string to <see cref="Visibility.Collapsed"/>, otherwise <see cref="Visibility.Visible"/>.
/// </summary>
public sealed class NullOrEmptyToCollapsedConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
