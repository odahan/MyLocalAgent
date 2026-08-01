using System.Globalization;
using System.Windows.Data;

namespace OD.MyLocalAgent.UI.Converters;

/// <summary>
/// Computes the minimum height a scrolled page needs so its star-sized row
/// fills the viewport, optionally extended to force the scrollbar to appear.
/// </summary>
public sealed class ViewportFillMinHeightConverter : IMultiValueConverter
{
    /// <summary>
    /// The extra height applied when the extended flag is set.
    /// </summary>
    public double ExtendedDelta { get; set; } = 300;

    /// <inheritdoc />
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (values is not [double viewportHeight, ..])
        {
            return 0d;
        }

        var isExtended = values.Length > 1 && values[1] is true;
        return viewportHeight + (isExtended ? ExtendedDelta : 0);
    }

    /// <inheritdoc />
    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
