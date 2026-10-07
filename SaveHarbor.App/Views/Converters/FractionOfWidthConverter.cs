using System.Globalization;
using System.Windows.Data;

namespace SaveHarbor.App.Views.Converters;

// Values: a fraction (0 to 1) and a width; the result is that share of the width, plus the optional numeric parameter
// as an offset (e.g. to centre a dot on the point).
public sealed class FractionOfWidthConverter : IMultiValueConverter
{
    public static FractionOfWidthConverter Instance { get; } = new();

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var offset = parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        return values is [double fraction, double width] && !double.IsNaN(width)
            ? Math.Max(0, Math.Clamp(fraction, 0, 1) * width + offset)
            : 0d;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
