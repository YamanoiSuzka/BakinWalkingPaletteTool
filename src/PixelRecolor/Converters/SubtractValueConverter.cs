using System.Globalization;
using System.Windows.Data;

namespace PixelRecolor.Converters;

/// <summary>
/// レイアウト上の実幅から、指定した余白分を差し引きます。
/// </summary>
public sealed class SubtractValueConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not double sourceValue
            || !double.TryParse(
                parameter?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var subtraction))
        {
            return 0d;
        }

        return Math.Max(0, sourceValue - subtraction);
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
